using System.Security.Claims;
using System.Text.Encodings.Web;
using Branches.Core.State;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Faith.UI.Logic.Auth;
using Faith.UI.Logic.Data;
using Faith.UI.Logic.Models;

namespace Faith.UI.Web.Auth;

public sealed class CurrentAccount(StateService<SessionState> state, IDbContextFactory<AppDbContext> factory)
{
  public async Task<AppUser?> GetAsync()
  {
    var session = state.state.Data;
    if (session?.UserId is not Guid id || !state.IsStateAccessAllowed || state.state.RevokedAt.HasValue) return null;
    await using var db = await factory.CreateDbContextAsync();
    return await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.Enabled && x.EmailVerified && x.SecurityVersion == session.SecurityVersion);
  }
  public async Task<bool> IsAdminAsync() => (await GetAsync())?.IsAdmin == true;
}

public sealed class BranchesAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
  UrlEncoder encoder, CurrentAccount account) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
  protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
  {
    var user = await account.GetAsync();
    if (user is null) return AuthenticateResult.NoResult();
    List<Claim> claims = [new(ClaimTypes.NameIdentifier, user.Id.ToString()), new(ClaimTypes.Name, user.DisplayName)];
    if (user.IsAdmin) claims.Add(new(ClaimTypes.Role, "Admin"));
    return AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name));
  }
}
