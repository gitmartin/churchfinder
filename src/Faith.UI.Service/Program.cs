using Faith.UI.Logic;
using Faith.UI.Service.Endpoints;
using Faith.UI.Service.Embedding;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorComponents();
builder.Services.AddEmbedConnections(builder.Configuration);
string storage = Path.GetFullPath(builder.Configuration["Storage:Root"] ?? "App_Data", builder.Environment.ContentRootPath);
builder.Services.AddSingleton(new SampleTextStore(Path.Combine(storage, "faith-ui.db")));

var app = builder.Build();
await app.Services.GetRequiredService<SampleTextStore>().InitializeAsync();
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UseHttpsRedirection();
app.Use(async (context, next) =>
{
  context.Response.Headers.XContentTypeOptions = "nosniff";
  context.Response.Headers["Referrer-Policy"] = "no-referrer";
  context.Response.Headers["X-Frame-Options"] = "DENY";
  await next();
});
app.UseStaticFiles();
app.UseCors();
app.UseFaithEmbeds();
app.MapEmbedConnectionApi();
app.MapComponentCatalog();
app.MapGet("/health", async (SampleTextStore store) =>
{
  await store.GetLoremAsync();
  return Results.Ok(new { status = "ready" });
});
app.Run();
