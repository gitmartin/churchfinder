using System.Net;
using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Faith.UI.Logic.Data;
using Faith.UI.Logic.Models;

namespace Faith.UI.Logic.Communications;

public sealed class EmailService(IDbContextFactory<AppDbContext> factory)
{
  public static string Render(string template, IReadOnlyDictionary<string, string> values)
  {
    foreach (var pair in values) template = template.Replace("{{" + pair.Key + "}}", WebUtility.HtmlEncode(pair.Value), StringComparison.Ordinal);
    return template;
  }

  public async Task QueueAsync(string templateKey, string recipient, string name, string url = "")
  {
    await using var db = await factory.CreateDbContextAsync();
    var template = await db.EmailTemplates.SingleAsync(x => x.Key == templateKey);
    var site = await db.Settings.SingleAsync();
    var values = new Dictionary<string, string> { ["name"] = name, ["site"] = site.SiteName, ["url"] = url };
    db.Emails.Add(new() { Recipient = recipient, Subject = Render(template.Subject, values).Replace('\r', ' ').Replace('\n', ' '), Html = Render(template.Body, values) });
    await db.SaveChangesAsync();
  }

  public static IEnumerable<EmailTemplate> Defaults()
  {
    yield return Template("verify", "Confirm your email for {{site}}", "Confirm your email", "Finish creating your account.", "Confirm email");
    yield return Template("signin", "New sign-in to {{site}}", "You signed in", "A new session was opened for your account. If this was not you, reset your password.", "Reset password");
    yield return Template("reset", "Reset your {{site}} password", "Reset your password", "Use this single-use link within 30 minutes. Ignore this email if you did not request it.", "Choose a password");
    yield return Template("invite", "You are invited to {{site}}", "You're invited", "Create your account using this invitation within 48 hours.", "Accept invitation");
    yield return Template("welcome", "Welcome to {{site}}", "Make yourself at home", "Your account is ready. Sign in to manage your profile and passkeys.", "Open dashboard");
  }

  private static EmailTemplate Template(string key, string subject, string title, string text, string action) => new()
  {
    Key = key, Subject = subject,
    Body = "<!doctype html><html><body style=\"background:#f4f5f2;font-family:Arial,sans-serif;padding:32px;color:#17251d\"><div style=\"max-width:560px;margin:auto;background:white;padding:40px;border-radius:16px\"><p>{{site}}</p><h1>" + title + "</h1><p>Hello {{name}},</p><p>" + text + "</p><p><a style=\"display:inline-block;background:#245e44;color:white;padding:14px 22px;border-radius:8px\" href=\"{{url}}\">" + action + "</a></p><p style=\"color:#647168;font-size:12px\">Sent by {{site}}. Never share account links.</p></div></body></html>"
  };
}

public sealed class EmailWorker(IDbContextFactory<AppDbContext> factory, IConfiguration config, ILogger<EmailWorker> logger) : BackgroundService
{
  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
    try
    {
      while (await timer.WaitForNextTickAsync(stoppingToken)) await DeliverBatchAsync(stoppingToken);
    }
    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
  }

  private async Task DeliverBatchAsync(CancellationToken token)
  {
    await using var db = await factory.CreateDbContextAsync(token);
    var batch = await db.Emails.Where(x => x.Status == "Pending" && x.NextAttemptUtc <= DateTime.UtcNow).OrderBy(x => x.CreatedUtc).Take(10).ToListAsync(token);
    foreach (var email in batch)
    {
      try
      {
        using var client = new SmtpClient();
        bool pickup = config["Email:Mode"] == "Pickup";
        if (pickup)
        {
          var path = Path.GetFullPath(Path.Combine(config["Storage:Root"]!, "mail"));
          Directory.CreateDirectory(path);
          client.DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory;
          client.PickupDirectoryLocation = path;
        }
        else
        {
          client.Host = config["Email:Host"]!;
          client.Port = config.GetValue("Email:Port", 587);
          client.EnableSsl = true;
          client.Timeout = 15000;
          if (!string.IsNullOrEmpty(config["Email:Username"])) client.Credentials = new NetworkCredential(config["Email:Username"], config["Email:Password"]);
        }
        using var message = new MailMessage(config["Email:From"]!, email.Recipient, email.Subject, email.Html) { IsBodyHtml = true };
        await client.SendMailAsync(message, token);
        email.Status = pickup ? "Local outbox" : "Sent";
        email.LastError = null;
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        email.Attempts++;
        email.Status = email.Attempts >= 5 ? "Failed" : "Pending";
        email.NextAttemptUtc = DateTime.UtcNow.AddMinutes(Math.Pow(2, email.Attempts));
        email.LastError = ex.GetType().Name;
        logger.LogWarning("Email delivery failed for message {Id}; attempt {Attempt}.", email.Id, email.Attempts);
      }
    }
    await db.SaveChangesAsync(token);
  }
}
