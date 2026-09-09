using Microsoft.EntityFrameworkCore;
using Faith.UI.Logic.Models;

namespace Faith.UI.Logic.Data;

public class AppDbContext(DbContextOptions options) : DbContext(options)
{
  public DbSet<AppUser> Users => Set<AppUser>();
  public DbSet<UserPasskey> Passkeys => Set<UserPasskey>();
  public DbSet<OneTimeToken> Tokens => Set<OneTimeToken>();
  public DbSet<ApplicationSettings> Settings => Set<ApplicationSettings>();
  public DbSet<EmailTemplate> EmailTemplates => Set<EmailTemplate>();
  public DbSet<EmailMessage> Emails => Set<EmailMessage>();
  public DbSet<ContactMessage> Contacts => Set<ContactMessage>();
  public DbSet<MonitoringEntry> Monitoring => Set<MonitoringEntry>();

  protected override void OnModelCreating(ModelBuilder model)
  {
    model.Entity<AppUser>().HasIndex(x => x.Email).IsUnique();
    model.Entity<AppUser>().Property(x => x.Email).HasMaxLength(254);
    model.Entity<AppUser>().Property(x => x.DisplayName).HasMaxLength(100);
    model.Entity<UserPasskey>().HasKey(x => x.CredentialId);
    model.Entity<UserPasskey>().HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    model.Entity<OneTimeToken>().HasKey(x => x.Hash);
    model.Entity<OneTimeToken>().HasIndex(x => x.ExpiresUtc);
    model.Entity<EmailTemplate>().HasKey(x => x.Key);
    model.Entity<EmailMessage>().HasIndex(x => new { x.Status, x.NextAttemptUtc });
    model.Entity<MonitoringEntry>().HasIndex(x => new { x.Kind, x.Application, x.TimestampUtc });
    model.Entity<MonitoringEntry>().Property(x => x.Id).HasMaxLength(450);
    model.Entity<MonitoringEntry>().Property(x => x.Kind).HasMaxLength(20);
    model.Entity<MonitoringEntry>().Property(x => x.Application).HasMaxLength(100);
    model.Entity<UserPasskey>().Property(x => x.CredentialId).HasMaxLength(450);
    model.Entity<OneTimeToken>().Property(x => x.Hash).HasMaxLength(64);
    model.Entity<EmailTemplate>().Property(x => x.Key).HasMaxLength(40);
    model.Entity<EmailMessage>().Property(x => x.Status).HasMaxLength(20);
    model.Entity<ApplicationSettings>().Property(x => x.Id).ValueGeneratedNever();
  }
}
