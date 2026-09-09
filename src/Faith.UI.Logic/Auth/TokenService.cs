using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Faith.UI.Logic.Data;
using Faith.UI.Logic.Models;

namespace Faith.UI.Logic.Auth;

public sealed class TokenService(IDbContextFactory<AppDbContext> factory)
{
  public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

  public async Task<string> IssueAsync(string purpose, Guid? userId, string payload, TimeSpan lifetime)
  {
    var raw = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    await using var db = await factory.CreateDbContextAsync();
    db.Tokens.Add(new() { Hash = Hash(raw), Purpose = purpose, UserId = userId, Payload = payload, ExpiresUtc = DateTime.UtcNow.Add(lifetime) });
    await db.SaveChangesAsync();
    return raw;
  }

  // Caller supplies its transaction so consuming a grant and changing the account are atomic.
  public static async Task<OneTimeToken?> ConsumeAsync(AppDbContext db, string raw, string purpose)
  {
    if (string.IsNullOrWhiteSpace(raw) || raw.Length > 256) return null;
    string hash = Hash(raw);
    var token = await db.Tokens.AsNoTracking().SingleOrDefaultAsync(x => x.Hash == hash && x.Purpose == purpose && x.ExpiresUtc > DateTime.UtcNow);
    if (token is null) return null;
    return await db.Tokens.Where(x => x.Hash == hash).ExecuteDeleteAsync() == 1 ? token : null;
  }
}
