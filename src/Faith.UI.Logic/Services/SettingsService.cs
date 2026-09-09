using Microsoft.EntityFrameworkCore;
using Faith.UI.Contracts;
using Faith.UI.Logic.Data;

namespace Faith.UI.Logic.Services;

public sealed class SettingsService(IDbContextFactory<AppDbContext> factory)
{
  public async Task<SiteSettings> GetAsync()
  {
    await using var db = await factory.CreateDbContextAsync();
    var x = await db.Settings.AsNoTracking().SingleAsync();
    return new(x.SiteName, x.Theme, x.AllowRegistration, x.ContactEmail);
  }

  public async Task SaveAsync(SiteSettings settings)
  {
    if (!Themes.IsValid(settings.Theme) || string.IsNullOrWhiteSpace(settings.SiteName) || settings.SiteName.Length > 80 ||
      !Branches.Core.Authentication.AuthenticationProviders.IsValidIdentifier(Branches.Core.Authentication.AuthenticationProvider.Email, settings.ContactEmail))
      throw new ArgumentException("Enter a site name, a valid contact email, and one of the five themes.");
    await using var db = await factory.CreateDbContextAsync();
    var row = await db.Settings.SingleAsync();
    row.SiteName = settings.SiteName.Trim();
    row.Theme = settings.Theme;
    row.AllowRegistration = settings.AllowRegistration;
    row.ContactEmail = settings.ContactEmail.Trim();
    await db.SaveChangesAsync();
  }
}
