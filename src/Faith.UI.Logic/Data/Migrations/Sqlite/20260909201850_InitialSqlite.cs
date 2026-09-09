using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Faith.UI.Logic.Data.Migrations.Sqlite
{
  /// <inheritdoc />
  public partial class InitialSqlite : Migration
  {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.CreateTable(
        name: "Contacts",
        columns: table => new
        {
          Id = table.Column<Guid>(type: "TEXT", nullable: false),
          Name = table.Column<string>(type: "TEXT", nullable: false),
          Email = table.Column<string>(type: "TEXT", nullable: false),
          Message = table.Column<string>(type: "TEXT", nullable: false),
          CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
        },
        constraints: table =>
        {
          table.PrimaryKey("PK_Contacts", x => x.Id);
        });

      migrationBuilder.CreateTable(
        name: "Emails",
        columns: table => new
        {
          Id = table.Column<Guid>(type: "TEXT", nullable: false),
          Recipient = table.Column<string>(type: "TEXT", nullable: false),
          Subject = table.Column<string>(type: "TEXT", nullable: false),
          Html = table.Column<string>(type: "TEXT", nullable: false),
          Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
          Attempts = table.Column<int>(type: "INTEGER", nullable: false),
          LastError = table.Column<string>(type: "TEXT", nullable: true),
          CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
          NextAttemptUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
        },
        constraints: table =>
        {
          table.PrimaryKey("PK_Emails", x => x.Id);
        });

      migrationBuilder.CreateTable(
        name: "EmailTemplates",
        columns: table => new
        {
          Key = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
          Subject = table.Column<string>(type: "TEXT", nullable: false),
          Body = table.Column<string>(type: "TEXT", nullable: false)
        },
        constraints: table =>
        {
          table.PrimaryKey("PK_EmailTemplates", x => x.Key);
        });

      migrationBuilder.CreateTable(
        name: "Monitoring",
        columns: table => new
        {
          Id = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
          Kind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
          Application = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
          TimestampUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
          StartedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
          Json = table.Column<string>(type: "TEXT", nullable: false)
        },
        constraints: table =>
        {
          table.PrimaryKey("PK_Monitoring", x => x.Id);
        });

      migrationBuilder.CreateTable(
        name: "Settings",
        columns: table => new
        {
          Id = table.Column<int>(type: "INTEGER", nullable: false),
          SiteName = table.Column<string>(type: "TEXT", nullable: false),
          Theme = table.Column<string>(type: "TEXT", nullable: false),
          AllowRegistration = table.Column<bool>(type: "INTEGER", nullable: false),
          ContactEmail = table.Column<string>(type: "TEXT", nullable: false)
        },
        constraints: table =>
        {
          table.PrimaryKey("PK_Settings", x => x.Id);
        });

      migrationBuilder.CreateTable(
        name: "Tokens",
        columns: table => new
        {
          Hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
          Purpose = table.Column<string>(type: "TEXT", nullable: false),
          UserId = table.Column<Guid>(type: "TEXT", nullable: true),
          Payload = table.Column<string>(type: "TEXT", nullable: false),
          ExpiresUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
        },
        constraints: table =>
        {
          table.PrimaryKey("PK_Tokens", x => x.Hash);
        });

      migrationBuilder.CreateTable(
        name: "Users",
        columns: table => new
        {
          Id = table.Column<Guid>(type: "TEXT", nullable: false),
          Email = table.Column<string>(type: "TEXT", maxLength: 254, nullable: false),
          DisplayName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
          PasswordHash = table.Column<string>(type: "TEXT", nullable: false),
          EmailVerified = table.Column<bool>(type: "INTEGER", nullable: false),
          IsAdmin = table.Column<bool>(type: "INTEGER", nullable: false),
          Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
          SecurityVersion = table.Column<int>(type: "INTEGER", nullable: false),
          FailedSignIns = table.Column<int>(type: "INTEGER", nullable: false),
          LockedUntilUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
          CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
          LastSignInUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
          TermsAcceptedUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
        },
        constraints: table =>
        {
          table.PrimaryKey("PK_Users", x => x.Id);
        });

      migrationBuilder.CreateTable(
        name: "Passkeys",
        columns: table => new
        {
          CredentialId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
          UserId = table.Column<Guid>(type: "TEXT", nullable: false),
          PublicKey = table.Column<string>(type: "TEXT", nullable: false),
          UserHandle = table.Column<string>(type: "TEXT", nullable: false),
          SignCount = table.Column<uint>(type: "INTEGER", nullable: false),
          Name = table.Column<string>(type: "TEXT", nullable: false)
        },
        constraints: table =>
        {
          table.PrimaryKey("PK_Passkeys", x => x.CredentialId);
          table.ForeignKey(
            name: "FK_Passkeys_Users_UserId",
            column: x => x.UserId,
            principalTable: "Users",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);
        });

      migrationBuilder.CreateIndex(
        name: "IX_Emails_Status_NextAttemptUtc",
        table: "Emails",
        columns: new[] { "Status", "NextAttemptUtc" });

      migrationBuilder.CreateIndex(
        name: "IX_Monitoring_Kind_Application_TimestampUtc",
        table: "Monitoring",
        columns: new[] { "Kind", "Application", "TimestampUtc" });

      migrationBuilder.CreateIndex(
        name: "IX_Passkeys_UserId",
        table: "Passkeys",
        column: "UserId");

      migrationBuilder.CreateIndex(
        name: "IX_Tokens_ExpiresUtc",
        table: "Tokens",
        column: "ExpiresUtc");

      migrationBuilder.CreateIndex(
        name: "IX_Users_Email",
        table: "Users",
        column: "Email",
        unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.DropTable(
        name: "Contacts");

      migrationBuilder.DropTable(
        name: "Emails");

      migrationBuilder.DropTable(
        name: "EmailTemplates");

      migrationBuilder.DropTable(
        name: "Monitoring");

      migrationBuilder.DropTable(
        name: "Passkeys");

      migrationBuilder.DropTable(
        name: "Settings");

      migrationBuilder.DropTable(
        name: "Tokens");

      migrationBuilder.DropTable(
        name: "Users");
    }
  }
}
