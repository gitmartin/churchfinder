using Branches.Core.Authentication;
using Microsoft.EntityFrameworkCore;
using Faith.UI.Logic.Data;
using Faith.UI.Logic.Models;

namespace Faith.UI.Logic.Auth;

public sealed class PasskeyStore(IDbContextFactory<AppDbContext> factory) : IPasskeyCredentialStore<Guid>
{
  private static PasskeyCredential<Guid> Map(UserPasskey x) => new(x.UserId, x.CredentialId, x.PublicKey, x.SignCount, x.UserHandle, x.Name, x.User.Enabled && x.User.EmailVerified);

  public async ValueTask<IReadOnlyList<PasskeyCredential<Guid>>> FindByUserAsync(Guid userKey, CancellationToken cancellationToken = default)
  {
    await using var db = await factory.CreateDbContextAsync(cancellationToken);
    return (await db.Passkeys.Include(x => x.User).Where(x => x.UserId == userKey).ToArrayAsync(cancellationToken)).Select(Map).ToArray();
  }

  public async ValueTask<PasskeyCredential<Guid>?> FindByCredentialIdAsync(string credentialId, CancellationToken cancellationToken = default)
  {
    await using var db = await factory.CreateDbContextAsync(cancellationToken);
    var row = await db.Passkeys.Include(x => x.User).SingleOrDefaultAsync(x => x.CredentialId == credentialId, cancellationToken);
    return row is null ? null : Map(row);
  }

  public async ValueTask<bool> IsUserHandleOwnerOfCredentialAsync(Guid userKey, string userHandle, string credentialId, CancellationToken cancellationToken = default)
  {
    await using var db = await factory.CreateDbContextAsync(cancellationToken);
    return await db.Passkeys.AnyAsync(x => x.UserId == userKey && x.UserHandle == userHandle && x.CredentialId == credentialId && x.User.Enabled, cancellationToken);
  }

  public async ValueTask AddAsync(PasskeyCredential<Guid> credential, CancellationToken cancellationToken = default)
  {
    await using var db = await factory.CreateDbContextAsync(cancellationToken);
    db.Passkeys.Add(new() { CredentialId = credential.CredentialId, UserId = credential.UserKey, PublicKey = credential.PublicKey, SignCount = credential.SignCount, UserHandle = credential.UserHandle, Name = credential.Name });
    await db.SaveChangesAsync(cancellationToken);
  }

  public async ValueTask<bool> TryUpdateSignCountAsync(string credentialId, uint expectedSignCount, uint newSignCount, CancellationToken cancellationToken = default)
  {
    await using var db = await factory.CreateDbContextAsync(cancellationToken);
    return await db.Passkeys.Where(x => x.CredentialId == credentialId && x.SignCount == expectedSignCount)
      .ExecuteUpdateAsync(s => s.SetProperty(x => x.SignCount, newSignCount), cancellationToken) == 1;
  }
}
