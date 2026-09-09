using Microsoft.EntityFrameworkCore;
using Faith.UI.Logic.Auth;
using Faith.UI.Logic.Communications;
using Faith.UI.Logic.Data;

namespace Faith.UI.Web;

public static class DatabaseSetup
{
  public static async Task InitializeAsync(IServiceProvider services, IConfiguration config)
  {
    await using var db = await services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
    if (config.GetValue("Database:MigrateOnStartup", true)) await db.Database.MigrateAsync();
    if (!await db.Settings.AnyAsync()) db.Settings.Add(new());
    foreach (var template in EmailService.Defaults())
      if (!await db.EmailTemplates.AnyAsync(x => x.Key == template.Key)) db.EmailTemplates.Add(template);
    await db.SaveChangesAsync();
    string? email = config["Bootstrap:AdminEmail"];
    string? password = config["Bootstrap:AdminPassword"];
    if (!string.IsNullOrWhiteSpace(email) && !await db.Users.AnyAsync(x => x.IsAdmin))
    {
      if (!AccountService.ValidPassword(password) || !Branches.Core.Authentication.AuthenticationProviders.IsValidIdentifier(Branches.Core.Authentication.AuthenticationProvider.Email, email))
        throw new InvalidOperationException("Bootstrap requires a valid AdminEmail and AdminPassword of 12–128 characters.");
      db.Users.Add(new() { Email = email.Trim().ToLowerInvariant(), DisplayName = "Administrator", PasswordHash = services.GetRequiredService<Branches.Core.Authentication.IAuthenticationSecretProtector>().Protect(password!), EmailVerified = true, IsAdmin = true });
      await db.SaveChangesAsync();
    }
  }
}

public sealed class RetentionWorker(IDbContextFactory<AppDbContext> factory) : BackgroundService
{
  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
    try
    {
      do
      {
        await using var db = await factory.CreateDbContextAsync(stoppingToken);
        await db.Tokens.Where(x => x.ExpiresUtc < DateTime.UtcNow).ExecuteDeleteAsync(stoppingToken);
        var cutoff = DateTime.UtcNow.AddDays(-90);
        await db.Monitoring.Where(x => x.TimestampUtc < cutoff).ExecuteDeleteAsync(stoppingToken);
        await db.Emails.Where(x => x.CreatedUtc < cutoff && x.Status != "Pending").ExecuteDeleteAsync(stoppingToken);
        await db.Contacts.Where(x => x.CreatedUtc < cutoff).ExecuteDeleteAsync(stoppingToken);
      } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
  }
}
