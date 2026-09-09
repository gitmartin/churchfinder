namespace Faith.UI.Logic.Models;

public sealed class AppUser
{
  public Guid Id { get; set; } = Guid.NewGuid();
  public string Email { get; set; } = "";
  public string DisplayName { get; set; } = "";
  public string PasswordHash { get; set; } = "";
  public bool EmailVerified { get; set; }
  public bool IsAdmin { get; set; }
  public bool Enabled { get; set; } = true;
  public int SecurityVersion { get; set; }
  public int FailedSignIns { get; set; }
  public DateTime? LockedUntilUtc { get; set; }
  public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
  public DateTime? LastSignInUtc { get; set; }
  public DateTime? TermsAcceptedUtc { get; set; }
}

public sealed class UserPasskey
{
  public string CredentialId { get; set; } = "";
  public Guid UserId { get; set; }
  public AppUser User { get; set; } = null!;
  public string PublicKey { get; set; } = "";
  public string UserHandle { get; set; } = "";
  public uint SignCount { get; set; }
  public string Name { get; set; } = "";
}

public sealed class OneTimeToken
{
  public string Hash { get; set; } = "";
  public string Purpose { get; set; } = "";
  public Guid? UserId { get; set; }
  public string Payload { get; set; } = "";
  public DateTime ExpiresUtc { get; set; }
}

public sealed class ApplicationSettings
{
  public int Id { get; set; } = 1;
  public string SiteName { get; set; } = "Faith UI";
  public string Theme { get; set; } = "Forest";
  public bool AllowRegistration { get; set; } = true;
  public string ContactEmail { get; set; } = "hello@example.com";
}

public sealed class EmailTemplate
{
  public string Key { get; set; } = "";
  public string Subject { get; set; } = "";
  public string Body { get; set; } = "";
}

public sealed class EmailMessage
{
  public Guid Id { get; set; } = Guid.NewGuid();
  public string Recipient { get; set; } = "";
  public string Subject { get; set; } = "";
  public string Html { get; set; } = "";
  public string Status { get; set; } = "Pending";
  public int Attempts { get; set; }
  public string? LastError { get; set; }
  public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
  public DateTime NextAttemptUtc { get; set; } = DateTime.UtcNow;
}

public sealed class ContactMessage
{
  public Guid Id { get; set; } = Guid.NewGuid();
  public string Name { get; set; } = "";
  public string Email { get; set; } = "";
  public string Message { get; set; } = "";
  public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class MonitoringEntry
{
  public string Id { get; set; } = "";
  public string Kind { get; set; } = "";
  public string Application { get; set; } = "";
  public DateTime TimestampUtc { get; set; }
  public DateTime StartedUtc { get; set; }
  public string Json { get; set; } = "";
}
