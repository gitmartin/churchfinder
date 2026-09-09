using System.Security.Cryptography;
using System.Text.Json;
using Branches.Core.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Faith.UI.Contracts;
using Faith.UI.Logic.Communications;
using Faith.UI.Logic.Data;
using Faith.UI.Logic.Models;
using Faith.UI.Logic.Services;

namespace Faith.UI.Logic.Auth;

public sealed class AccountService(IDbContextFactory<AppDbContext> factory, AuthenticationHelper<Guid> authentication,
  IAuthenticationSecretProtector secrets, TokenService tokens, EmailService emails, SettingsService settings,
  IDataProtectionProvider protection, IConfiguration config, ILogger<AccountService> logger)
{
  private sealed record PendingRegistration(string Email, string DisplayName, string PasswordHash);
  private readonly IDataProtector pendingProtector = protection.CreateProtector("Faith.UI.PendingRegistration.v2");
  private static readonly string DummyHash = new SecretProtector().Protect(Guid.NewGuid().ToString());
  public static UserProfile Profile(AppUser user) => new(user.Id, user.Email, user.DisplayName, user.IsAdmin, user.Enabled, user.EmailVerified);
  public string Url(string path) => config["Site:PublicOrigin"]!.TrimEnd('/') + path;
  public static bool ValidPassword(string? password) => password is { Length: >= 12 and <= 128 };
  public static bool ValidName(string? name) => !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 100;

  public async Task<ApiReply> BeginRegistrationAsync(RegistrationRequest request)
  {
    if (!(await settings.GetAsync()).AllowRegistration) return new(false, "Registration is currently closed.");
    if (!request.AcceptTerms || !ValidPassword(request.Password) || !ValidName(request.DisplayName) ||
      !AuthenticationProviders.IsValidIdentifier(AuthenticationProvider.Email, request.Email))
      return new(false, "Enter your name, a valid email, a password of 12–128 characters, and accept the terms.");
    if (await authentication.CheckRegistrationAsync(AuthenticationProvider.Email, request.Email) == AuthenticationRegistrationStatus.Available)
    {
      var pending = new PendingRegistration(
        AuthenticationProviders.NormalizeIdentifier(AuthenticationProvider.Email, request.Email),
        request.DisplayName.Trim(), secrets.Protect(request.Password));
      var payload = pendingProtector.Protect(JsonSerializer.Serialize(pending));
      var token = await tokens.IssueAsync("register", null, payload, TimeSpan.FromMinutes(30));
      await emails.QueueAsync("verify", pending.Email, pending.DisplayName, Url("/confirm-email?token=" + token));
    }
    return new(true, "Check your email for the next step. If an account already exists, use sign in or password reset.");
  }

  public async Task<ApiReply> CompleteRegistrationAsync(string raw)
  {
    await using var db = await factory.CreateDbContextAsync();
    await using var transaction = await db.Database.BeginTransactionAsync();
    var token = await TokenService.ConsumeAsync(db, raw, "register");
    if (token is null) return new(false, "This confirmation link is invalid or expired.");
    PendingRegistration? pending;
    try { pending = JsonSerializer.Deserialize<PendingRegistration>(pendingProtector.Unprotect(token.Payload)); }
    catch (Exception ex) when (ex is CryptographicException or JsonException)
    {
      return new(false, "This confirmation link is invalid or expired. Request a new registration email.");
    }
    if (pending is null || !ValidName(pending.DisplayName) || string.IsNullOrWhiteSpace(pending.PasswordHash) ||
      await authentication.CheckRegistrationAsync(AuthenticationProvider.Email, pending.Email) != AuthenticationRegistrationStatus.Available)
      return new(false, "This account could not be created. Try signing in.");
    var user = new AppUser
    {
      Email = AuthenticationProviders.NormalizeIdentifier(AuthenticationProvider.Email, pending.Email),
      DisplayName = pending.DisplayName.Trim(), PasswordHash = pending.PasswordHash,
      EmailVerified = true, TermsAcceptedUtc = DateTime.UtcNow
    };
    db.Users.Add(user);
    await db.SaveChangesAsync();
    await transaction.CommitAsync();
    await QueueWelcomeAsync(user.Id, user.Email, user.DisplayName);
    return new(true, "Email confirmed. You can now sign in.");
  }

  private async Task QueueWelcomeAsync(Guid userId, string email, string name)
  {
    try { await emails.QueueAsync("welcome", email, name, Url("/dashboard")); }
    catch (Exception ex)
    {
      logger.LogError(ex, "Welcome email could not be queued after account creation for user {UserId}.", userId);
    }
  }

  private ValueTask<AuthenticationRegistrationResult<Guid>> RegisterAsync(AppDbContext db, string email, string password, string name) =>
    authentication.RegisterAsync(new AuthenticationRegistrationRequest<string>(AuthenticationProvider.Email, email, password, true, name.Trim()),
      async (context, ct) =>
      {
        var user = new AppUser { Email = context.NormalizedIdentifier, DisplayName = context.Profile, PasswordHash = context.ProtectedSecret, EmailVerified = true, TermsAcceptedUtc = DateTime.UtcNow };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return user.Id;
      });

  public async Task<AppUser?> SignInAsync(SignInRequest request)
  {
    if (request.Password is null || request.Password.Length > 128 || request.Email is null || request.Email.Length > 254) return null;
    string email = AuthenticationProviders.NormalizeIdentifier(AuthenticationProvider.Email, request.Email);
    await using var db = await factory.CreateDbContextAsync();
    var user = await db.Users.SingleOrDefaultAsync(x => x.Email == email);
    if (user is null || !user.Enabled || user.LockedUntilUtc > DateTime.UtcNow)
    {
      secrets.Verify(request.Password, DummyHash);
      return null;
    }
    var result = await authentication.AuthenticateSecretAsync(AuthenticationProvider.Email, email, request.Password);
    if (!result.Succeeded)
    {
      await db.Users.Where(x => x.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.FailedSignIns, x => x.FailedSignIns + 1));
      await db.Users.Where(x => x.Id == user.Id && x.FailedSignIns >= 5).ExecuteUpdateAsync(s => s.SetProperty(x => x.LockedUntilUtc, DateTime.UtcNow.AddMinutes(15)));
      return null;
    }
    user.FailedSignIns = 0;
    user.LockedUntilUtc = null;
    user.LastSignInUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return user;
  }

  public async Task<ApiReply> BeginResetAsync(string email)
  {
    email = AuthenticationProviders.NormalizeIdentifier(AuthenticationProvider.Email, email);
    await using var db = await factory.CreateDbContextAsync();
    var user = await db.Users.SingleOrDefaultAsync(x => x.Email == email && x.Enabled && x.EmailVerified);
    if (user is not null)
    {
      var raw = await tokens.IssueAsync("reset", user.Id, user.SecurityVersion.ToString(), TimeSpan.FromMinutes(30));
      await emails.QueueAsync("reset", user.Email, user.DisplayName, Url("/reset-password?token=" + raw));
    }
    return new(true, "If the account is eligible, a password reset link is on its way.");
  }

  public async Task<ApiReply> ResetAsync(PasswordResetRequest request)
  {
    if (!ValidPassword(request.Password)) return new(false, "Use a password of 12–128 characters.");
    await using var db = await factory.CreateDbContextAsync();
    await using var transaction = await db.Database.BeginTransactionAsync();
    var token = await TokenService.ConsumeAsync(db, request.Token, "reset");
    if (token is null) return new(false, "This reset link is invalid or expired.");
    var user = await db.Users.SingleOrDefaultAsync(x => x.Id == token.UserId && x.Enabled);
    if (user is null || token.Payload != user.SecurityVersion.ToString()) return new(false, "This reset link is invalid or expired.");
    user.PasswordHash = secrets.Protect(request.Password);
    user.SecurityVersion++;
    user.FailedSignIns = 0;
    user.LockedUntilUtc = null;
    await db.SaveChangesAsync();
    await transaction.CommitAsync();
    return new(true, "Password updated. Your previous sessions have been signed out.");
  }

  public async Task<ApiReply> InviteAsync(string email)
  {
    if (!AuthenticationProviders.IsValidIdentifier(AuthenticationProvider.Email, email)) return new(false, "Enter a valid email address.");
    if (await authentication.CheckRegistrationAsync(AuthenticationProvider.Email, email) != AuthenticationRegistrationStatus.Available) return new(false, "An account already uses this email.");
    var raw = await tokens.IssueAsync("invite", null, AuthenticationProviders.NormalizeIdentifier(AuthenticationProvider.Email, email), TimeSpan.FromHours(48));
    await emails.QueueAsync("invite", email.Trim(), "there", Url("/accept-invite?token=" + raw));
    return new(true, "Invitation queued.");
  }

  public async Task<ApiReply> AcceptInviteAsync(AcceptInvitationRequest request)
  {
    if (!request.AcceptTerms || !ValidName(request.DisplayName) || !ValidPassword(request.Password)) return new(false, "Enter your name, a password of 12–128 characters, and accept the terms.");
    await using var db = await factory.CreateDbContextAsync();
    await using var transaction = await db.Database.BeginTransactionAsync();
    var token = await TokenService.ConsumeAsync(db, request.Token, "invite");
    if (token is null) return new(false, "This invitation is invalid or expired.");
    var result = await RegisterAsync(db, token.Payload, request.Password, request.DisplayName);
    if (!result.Succeeded) return new(false, "An account already exists for this invitation.");
    await transaction.CommitAsync();
    await QueueWelcomeAsync(result.UserKey, token.Payload, request.DisplayName);
    return new(true, "Your account is ready. You can sign in.");
  }
}
