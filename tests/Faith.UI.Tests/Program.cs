using Faith.UI.Logic;

string root = Path.GetFullPath(args.FirstOrDefault() ?? Directory.GetCurrentDirectory());
string run = Path.Combine(root, "artifacts", "tests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(run);
string databasePath = Path.Combine(run, "faith-ui.db");
var firstStore = new SampleTextStore(databasePath);
await firstStore.InitializeAsync();
string firstResponse = await firstStore.GetLoremAsync();
var reopenedStore = new SampleTextStore(databasePath);
await reopenedStore.InitializeAsync();
if (!firstResponse.StartsWith("Lorem ipsum", StringComparison.Ordinal)
  || await reopenedStore.GetLoremAsync() != firstResponse)
  throw new InvalidOperationException("SQLite sample response did not persist after reopening.");
Console.WriteLine("PASS SQLite seeds the sample response and reads it after reopening.");
