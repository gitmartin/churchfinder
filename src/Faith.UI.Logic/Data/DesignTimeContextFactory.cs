using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Faith.UI.Logic.Data;

public sealed class DesignTimeContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
  public AppDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<AppDbContext>()
    .UseSqlite(Environment.GetEnvironmentVariable("FAITH_UI_DATABASE_CONNECTION") ?? "Data Source=faith-ui.db").Options);
}
