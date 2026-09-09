using Branches.Core.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Faith.UI.Logic.Data;

namespace Faith.UI.Logic.Auth;

public sealed class SecretProtector : IAuthenticationSecretProtector
{
  private readonly PasswordHasher<object> hasher = new();
  public string Protect(string secret) => hasher.HashPassword(this, secret);
  public bool Verify(string suppliedSecret, string protectedSecret)
  {
    try { return hasher.VerifyHashedPassword(this, protectedSecret, suppliedSecret) != PasswordVerificationResult.Failed; }
    catch (FormatException) { return false; }
  }
}

public sealed class IdentityStore(IDbContextFactory<AppDbContext> factory) : IAuthenticationIdentityStore<Guid>
{
  public async ValueTask<IReadOnlyList<AuthenticationIdentity<Guid>>> FindByIdentifierAsync(
    AuthenticationProvider provider, string normalizedIdentifier, CancellationToken cancellationToken = default)
  {
    if (provider != AuthenticationProvider.Email) return [];
    await using var db = await factory.CreateDbContextAsync(cancellationToken);
    var users = await db.Users.AsNoTracking().Where(x => x.Email == normalizedIdentifier).ToListAsync(cancellationToken);
    return users.Select(x => new AuthenticationIdentity<Guid>(x.Id, provider, x.Email, x.PasswordHash, x.EmailVerified, x.Enabled)).ToArray();
  }
}
