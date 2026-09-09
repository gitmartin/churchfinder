using System.Collections.Concurrent;
using Branches.Core.Authentication;
using Microsoft.Extensions.Options;

namespace Faith.UI.Web.Auth;

// The Branches helper retains ceremony state. Keep one helper per ceremony across HTTP requests,
// bound to the originating Branches session. Never register one shared helper for all accounts.
public sealed class PasskeyCeremonies(IOptions<PasskeyAuthenticationOptions> options, IPasskeyCredentialStore<Guid> store)
{
  private sealed record Pending(string Session, string Kind, Guid? UserId, int Version, DateTime Expires, PasskeyAuthenticationHelper<Guid> Helper);
  private readonly ConcurrentDictionary<Guid, Pending> pending = new();
  public PasskeyAuthenticationHelper<Guid> Create() => new(options, store);
  public void Add(Guid id, string session, string kind, Guid? userId, int version, PasskeyAuthenticationHelper<Guid> helper)
  {
    foreach (var entry in pending.Where(x => x.Value.Expires <= DateTime.UtcNow)) pending.TryRemove(entry.Key, out _);
    if (pending.Count >= 1000) throw new InvalidOperationException("Too many pending passkey requests.");
    pending[id] = new(session, kind, userId, version, DateTime.UtcNow.AddMinutes(5), helper);
  }
  public PasskeyAuthenticationHelper<Guid>? Take(Guid id, string session, string kind, Guid? userId, int version)
  {
    if (!pending.TryRemove(id, out var value) || value.Session != session || value.Kind != kind || value.Expires <= DateTime.UtcNow || value.UserId != userId || value.Version != version) return null;
    return value.Helper;
  }
}
