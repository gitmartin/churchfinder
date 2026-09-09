using System.ComponentModel.DataAnnotations;

namespace Faith.UI.Contracts;

public sealed record UserProfile(Guid Id, string Email, string DisplayName, bool IsAdmin, bool Enabled, bool EmailVerified);
public sealed record SiteSettings(string SiteName, string Theme, bool AllowRegistration, string ContactEmail);
public sealed record SessionInfo(UserProfile? User, SiteSettings Settings, bool RequiresRestart = false);
public sealed record ApiReply(bool Success, string Message);
public sealed record PasskeyInfo(string CredentialId, string Name);
public sealed record DashboardSummary(int Users, int ActiveUsers, int PendingEmails, int ContactMessages);
public sealed record MessageSummary(Guid Id, string Recipient, string Subject, string Status, DateTime CreatedUtc, string? LastError);
public sealed record ContactSummary(Guid Id, string Name, string Email, string Message, DateTime CreatedUtc);
public sealed record EmailTemplateInfo(string Key, string Subject, string Body);
public sealed record ProfileUpdate([property: Required, StringLength(100, MinimumLength = 1)] string DisplayName);
public sealed record SignInRequest(string Email, string Password);
public sealed record RegistrationRequest(string Email, string Password, string DisplayName, bool AcceptTerms);
public sealed record CompleteRegistrationRequest(string Token);
public sealed record ResetRequest(string Email);
public sealed record PasswordResetRequest(string Token, string Password);
public sealed record ContactRequest(string Name, string Email, string Message);
public sealed record InvitationRequest(string Email);
public sealed record AcceptInvitationRequest(string Token, string DisplayName, string Password, bool AcceptTerms);
public sealed record UserUpdate(bool Enabled, bool IsAdmin);

public static class Themes
{
  public static readonly string[] All = ["Forest", "Ocean", "Violet", "Ember", "Slate"];
  public static bool IsValid(string value) => All.Contains(value, StringComparer.Ordinal);
}
