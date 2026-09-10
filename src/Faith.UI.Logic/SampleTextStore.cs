using Microsoft.Data.Sqlite;

namespace Faith.UI.Logic;

public sealed class SampleTextStore
{
  private readonly string databasePath;
  private readonly string connectionString;

  static SampleTextStore() => SQLitePCL.Batteries_V2.Init();

  public SampleTextStore(string databasePath)
  {
    this.databasePath = Path.GetFullPath(databasePath);
    connectionString = new SqliteConnectionStringBuilder
    {
      DataSource = this.databasePath,
      Pooling = false
    }.ToString();
  }

  public async Task InitializeAsync()
  {
    Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
    await using var connection = new SqliteConnection(connectionString);
    await connection.OpenAsync();
    await using var command = connection.CreateCommand();
    command.CommandText = """
      CREATE TABLE IF NOT EXISTS SampleResponses (
        id INTEGER PRIMARY KEY,
        text TEXT NOT NULL
      );
      INSERT OR IGNORE INTO SampleResponses (id, text) VALUES (1, $text);
      """;
    command.Parameters.AddWithValue("$text",
      "Lorem ipsum dolor sit amet, consectetur adipiscing elit. "
      + "Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. "
      + "Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris "
      + "nisi ut aliquip ex ea commodo consequat. Duis aute irure dolor in "
      + "reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur.");
    await command.ExecuteNonQueryAsync();
  }

  public async Task<string> GetLoremAsync()
  {
    await using var connection = new SqliteConnection(connectionString);
    await connection.OpenAsync();
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT text FROM SampleResponses WHERE id = 1;";
    return await command.ExecuteScalarAsync() as string
      ?? throw new InvalidOperationException("The sample response has not been initialized.");
  }
}
