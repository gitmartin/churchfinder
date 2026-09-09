using System.Text.Json;
using System.Text.Json.Nodes;
using Branches.Core.ActivityMonitoring;
using Branches.Core.Authentication;
using Branches.Core.State;
using Fido2NetLib;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using Faith.UI.Contracts;
using Faith.UI.Logic.Auth;
using Faith.UI.Logic.Communications;
using Faith.UI.Logic.Data;
using Faith.UI.Logic.Models;
using Faith.UI.Logic.Services;
using Faith.UI.Web.Auth;

namespace Faith.UI.Web.Endpoints;

public static class StarterApi
{
  public static void MapStarterApi(this WebApplication app)
  {
    var api = app.MapGroup("/api");
    api.AddEndpointFilter(async (context, next) =>
    {
      var http = context.HttpContext;
      if (!HttpMethods.IsGet(http.Request.Method))
      {
        try { await http.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(http); }
        catch (AntiforgeryValidationException) { return Results.Json(new ApiReply(false, "Your form expired. Refresh and try again."), statusCode: 400); }
      }
      try { return await next(context); }
      catch (ArgumentException) { return Results.BadRequest(new ApiReply(false, "Check the submitted values and try again.")); }
      catch (DbUpdateException) { return Results.Conflict(new ApiReply(false, "The record changed or already exists. Refresh and try again.")); }
    });
    api.MapGet("/csrf", (HttpContext http, IAntiforgery antiforgery) => Results.Ok(new { token = antiforgery.GetAndStoreTokens(http).RequestToken }));
    api.MapGet("/session", async (CurrentAccount current, SettingsService settings, StateService<SessionState> state) =>
    {
      var user = await current.GetAsync();
      return new SessionInfo(user is null ? null : AccountService.Profile(user), await settings.GetAsync(),
        user is null && state.state.Data?.UserId is not null);
    });
    api.MapGet("/settings", (SettingsService settings) => settings.GetAsync());

    var auth = api.MapGroup("/auth").RequireRateLimiting("auth");
    auth.MapPost("/register", (RegistrationRequest request, AccountService accounts) => accounts.BeginRegistrationAsync(request));
    auth.MapPost("/confirm-email", (CompleteRegistrationRequest request, AccountService accounts) => accounts.CompleteRegistrationAsync(request.Token));
    auth.MapPost("/forgot-password", (ResetRequest request, AccountService accounts) => accounts.BeginResetAsync(request.Email));
    auth.MapPost("/reset-password", (PasswordResetRequest request, AccountService accounts) => accounts.ResetAsync(request));
    auth.MapPost("/accept-invite", (AcceptInvitationRequest request, AccountService accounts) => accounts.AcceptInviteAsync(request));
    auth.MapPost("/signin", async (SignInRequest request, AccountService accounts, HttpContext http) =>
    {
      var user = await accounts.SignInAsync(request);
      return user is null ? Results.Json(new ApiReply(false, "Sign-in failed. Check your credentials and confirmed email, or try again later."), statusCode: 401) : await SignInAsync(http, user);
    });
    auth.MapPost("/signout", (HttpContext http, StateService<SessionState> state, ActivityTracker activity) =>
    {
      activity.EndSession(state.state.SecurityRecordId ?? state.state.key);
      state.EndCurrentSession();
      return new ApiReply(true, "Signed out.");
    });

    auth.MapPost("/passkeys/signin/begin", async (ResetRequest request, HttpContext http, AuthenticationHelper<Guid> authentication, PasskeyCeremonies ceremonies, StateService<SessionState> state) =>
    {
      var helper = ceremonies.Create();
      if (!helper.IsConfiguredForOrigin(http.Request.Headers.Origin)) return Results.BadRequest(new ApiReply(false, "Passkeys are not configured for this origin."));
      var identity = await authentication.FindIdentityAsync(AuthenticationProvider.Email, request.Email);
      if (!identity.Succeeded) return Results.BadRequest(new ApiReply(false, "No available passkey for these credentials."));
      var start = await helper.BeginSignInAsync(identity.Identity!.UserKey);
      if (start is null) return Results.BadRequest(new ApiReply(false, "No available passkey for these credentials."));
      ceremonies.Add(start.CeremonyId, state.state.key, "signin", null, 0, helper);
      return Results.Ok(new { start.CeremonyId, options = JsonNode.Parse(start.Options.ToJson()) });
    });
    auth.MapPost("/passkeys/signin/complete", async (PasskeyFinish request, HttpContext http, PasskeyCeremonies ceremonies, StateService<SessionState> state, IDbContextFactory<AppDbContext> factory) =>
    {
      var helper = ceremonies.Take(request.CeremonyId, state.state.key, "signin", null, 0);
      if (helper is null || !helper.IsConfiguredForOrigin(http.Request.Headers.Origin)) return Results.BadRequest(new ApiReply(false, "Passkey request expired. Try again."));
      try
      {
        var result = await helper.CompleteSignInAsync(request.CeremonyId, request.Credential.Deserialize<AuthenticatorAssertionRawResponse>(JsonOptions)!);
        await using var db = await factory.CreateDbContextAsync();
        var user = result is null ? null : await db.Users.SingleOrDefaultAsync(x => x.Id == result.UserKey && x.Enabled && x.EmailVerified);
        return user is null ? Results.Unauthorized() : await SignInAsync(http, user);
      }
      catch (Exception ex) when (ex is Fido2VerificationException or JsonException or FormatException) { return Results.BadRequest(new ApiReply(false, "Passkey verification failed. Try again.")); }
    });

    var userApi = api.MapGroup("/user").RequireAuthorization();
    userApi.MapPost("/profile", async (ProfileUpdate request, CurrentAccount current, IDbContextFactory<AppDbContext> factory) =>
    {
      if (!AccountService.ValidName(request.DisplayName)) return new ApiReply(false, "Enter a name of 1–100 characters.");
      var user = (await current.GetAsync())!;
      await using var db = await factory.CreateDbContextAsync();
      await db.Users.Where(x => x.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.DisplayName, request.DisplayName.Trim()));
      return new ApiReply(true, "Profile saved.");
    });
    userApi.MapGet("/passkeys", async (CurrentAccount current, IDbContextFactory<AppDbContext> factory) =>
    {
      var user = (await current.GetAsync())!;
      await using var db = await factory.CreateDbContextAsync();
      return await db.Passkeys.Where(x => x.UserId == user.Id).Select(x => new PasskeyInfo(x.CredentialId, x.Name)).ToArrayAsync();
    });
    userApi.MapPost("/passkeys/remove", async (PasskeyInfo request, CurrentAccount current, IDbContextFactory<AppDbContext> factory) =>
    {
      var user = (await current.GetAsync())!;
      await using var db = await factory.CreateDbContextAsync();
      await db.Passkeys.Where(x => x.UserId == user.Id && x.CredentialId == request.CredentialId).ExecuteDeleteAsync();
      return new ApiReply(true, "Passkey removed.");
    });
    userApi.MapPost("/passkeys/begin", async (HttpContext http, CurrentAccount current, PasskeyCeremonies ceremonies, StateService<SessionState> state) =>
    {
      var user = (await current.GetAsync())!;
      var helper = ceremonies.Create();
      if (!helper.IsConfiguredForOrigin(http.Request.Headers.Origin)) return Results.BadRequest(new ApiReply(false, "Passkeys require the configured website origin."));
      var start = await helper.BeginRegistrationAsync(new(user.Id, user.Id.ToString("N"), user.Email, user.DisplayName));
      if (start is null) return Results.BadRequest(new ApiReply(false, "You already have the maximum number of passkeys."));
      ceremonies.Add(start.CeremonyId, state.state.key, "register", user.Id, user.SecurityVersion, helper);
      return Results.Ok(new { start.CeremonyId, options = JsonNode.Parse(start.Options.ToJson()) });
    });
    userApi.MapPost("/passkeys/complete", async (PasskeyFinish request, HttpContext http, CurrentAccount current, PasskeyCeremonies ceremonies, StateService<SessionState> state) =>
    {
      var user = (await current.GetAsync())!;
      var helper = ceremonies.Take(request.CeremonyId, state.state.key, "register", user.Id, user.SecurityVersion);
      if (helper is null || !helper.IsConfiguredForOrigin(http.Request.Headers.Origin)) return new ApiReply(false, "Passkey request expired. Try again.");
      try
      {
        var result = await helper.CompleteRegistrationAsync(request.CeremonyId, request.Credential.Deserialize<AuthenticatorAttestationRawResponse>(JsonOptions)!);
        return new ApiReply(result is not null, result is null ? "Passkey could not be saved." : "Passkey added.");
      }
      catch (Exception ex) when (ex is Fido2VerificationException or JsonException or FormatException) { return new ApiReply(false, "Passkey verification failed. Try again."); }
    });
    api.MapPost("/contact", async (ContactRequest request, IDbContextFactory<AppDbContext> factory) =>
    {
      if (!AccountService.ValidName(request.Name) || !AuthenticationProviders.IsValidIdentifier(AuthenticationProvider.Email, request.Email) || string.IsNullOrWhiteSpace(request.Message) || request.Message.Length > 5000)
        return new ApiReply(false, "Enter your name, a valid email, and a message of up to 5,000 characters.");
      await using var db = await factory.CreateDbContextAsync();
      db.Contacts.Add(new() { Name = request.Name.Trim(), Email = request.Email.Trim(), Message = request.Message.Trim() });
      await db.SaveChangesAsync();
      return new ApiReply(true, "Thanks. Your message has been received.");
    }).RequireRateLimiting("auth");
    MapAdmin(api);
  }

  private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
  public sealed record PasskeyFinish(Guid CeremonyId, JsonElement Credential);

  private static async Task<IResult> SignInAsync(HttpContext http, AppUser user)
  {
    var state = http.RequestServices.GetRequiredService<StateService<SessionState>>();
    if (state.state.Data?.UserId is not null) return Results.Conflict(new ApiReply(false, "Sign out before starting another session."));
    var grant = state.IssueAuthenticationAdmission("signin", user.Id.ToString(), TimeSpan.FromMinutes(1));
    var result = state.CompleteAuthenticationAdmission("signin", grant.Token, _ => new(
      new SessionState { UserId = user.Id, SecurityVersion = user.SecurityVersion }, user.IsAdmin ? ["Admin"] : ["User"]));
    if (!result.WasCompleted) return Results.Conflict(new ApiReply(false, "Your session changed. Refresh and try again."));
    var accounts = http.RequestServices.GetRequiredService<AccountService>();
    try
    {
      await http.RequestServices.GetRequiredService<EmailService>().QueueAsync("signin", user.Email, user.DisplayName, accounts.Url("/forgot-password"));
    }
    catch (Exception exception)
    {
      // Notification persistence must not hide an already completed sign-in.
      http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("AuthenticationNotifications")
        .LogError(exception, "A completed sign-in notification could not be queued.");
    }
    return Results.Ok(new ApiReply(true, "Signed in."));
  }

  private static void MapAdmin(RouteGroupBuilder api)
  {
    var admin = api.MapGroup("/admin").RequireAuthorization("Admin");
    admin.MapGet("/summary", async (IDbContextFactory<AppDbContext> factory) =>
    {
      await using var db = await factory.CreateDbContextAsync();
      return new DashboardSummary(await db.Users.CountAsync(), await db.Users.CountAsync(x => x.Enabled), await db.Emails.CountAsync(x => x.Status == "Pending"), await db.Contacts.CountAsync());
    });
    admin.MapGet("/users", async (IDbContextFactory<AppDbContext> factory, int page = 0) =>
    {
      await using var db = await factory.CreateDbContextAsync();
      return (await db.Users.AsNoTracking().OrderBy(x => x.Email).Skip(Math.Clamp(page, 0, 10000) * 50).Take(50).ToArrayAsync()).Select(AccountService.Profile);
    });
    admin.MapPost("/users/{id:guid}", async (Guid id, UserUpdate request, CurrentAccount current, IDbContextFactory<AppDbContext> factory) =>
    {
      var actor = (await current.GetAsync())!;
      if (id == actor.Id && (!request.Enabled || !request.IsAdmin)) return new ApiReply(false, "You cannot disable or remove your own administrator access.");
      await using var db = await factory.CreateDbContextAsync();
      var user = await db.Users.FindAsync(id);
      if (user is null) return new ApiReply(false, "User not found.");
      user.Enabled = request.Enabled;
      user.IsAdmin = request.IsAdmin;
      user.SecurityVersion++;
      await db.SaveChangesAsync();
      return new ApiReply(true, "User updated. Existing sessions revoked.");
    });
    admin.MapPost("/invite", (InvitationRequest request, AccountService accounts) => accounts.InviteAsync(request.Email));
    admin.MapPost("/settings", async (SiteSettings request, SettingsService settings) => { await settings.SaveAsync(request); return new ApiReply(true, "Settings saved."); });
    admin.MapGet("/emails", async (IDbContextFactory<AppDbContext> factory) =>
    {
      await using var db = await factory.CreateDbContextAsync();
      return await db.Emails.OrderByDescending(x => x.CreatedUtc).Take(100).Select(x => new MessageSummary(x.Id, x.Recipient, x.Subject, x.Status, x.CreatedUtc, x.LastError)).ToArrayAsync();
    });
    admin.MapPost("/emails/{id:guid}/retry", async (Guid id, IDbContextFactory<AppDbContext> factory) =>
    {
      await using var db = await factory.CreateDbContextAsync();
      var count = await db.Emails.Where(x => x.Id == id && x.Status == "Failed").ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "Pending").SetProperty(x => x.Attempts, 0).SetProperty(x => x.NextAttemptUtc, DateTime.UtcNow));
      return new ApiReply(count == 1, count == 1 ? "Email queued for retry." : "Only failed messages can be retried.");
    });
    admin.MapGet("/templates", async (IDbContextFactory<AppDbContext> factory) =>
    {
      await using var db = await factory.CreateDbContextAsync();
      return await db.EmailTemplates.OrderBy(x => x.Key).Select(x => new EmailTemplateInfo(x.Key, x.Subject, x.Body)).ToArrayAsync();
    });
    admin.MapPost("/templates", async (EmailTemplateInfo request, IDbContextFactory<AppDbContext> factory) =>
    {
      if (string.IsNullOrWhiteSpace(request.Subject) || request.Subject.Length > 200 || string.IsNullOrWhiteSpace(request.Body) || request.Body.Length > 30000) return new ApiReply(false, "Enter a subject of up to 200 characters and a template of up to 30,000 characters.");
      await using var db = await factory.CreateDbContextAsync();
      var template = await db.EmailTemplates.FindAsync(request.Key);
      if (template is null) return new ApiReply(false, "Template not found.");
      template.Subject = request.Subject;
      template.Body = request.Body;
      await db.SaveChangesAsync();
      return new ApiReply(true, "Template saved.");
    });
    admin.MapGet("/contacts", async (IDbContextFactory<AppDbContext> factory) =>
    {
      await using var db = await factory.CreateDbContextAsync();
      return await db.Contacts.OrderByDescending(x => x.CreatedUtc).Take(100).Select(x => new ContactSummary(x.Id, x.Name, x.Email, x.Message, x.CreatedUtc)).ToArrayAsync();
    });
  }

}
