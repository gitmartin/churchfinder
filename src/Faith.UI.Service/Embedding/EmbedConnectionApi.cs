using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;

namespace Faith.UI.Service.Embedding;

public static class EmbedConnectionApi
{
  public static void AddEmbedConnections(this IServiceCollection services, IConfiguration configuration)
  {
    string[] origins = configuration.GetSection("Embed:AllowedOrigins").Get<string[]>() ?? [];
    services.AddCors(options => options.AddDefaultPolicy(policy => policy
      .WithOrigins(origins)
      .WithMethods("GET", "POST", "DELETE")
      .WithHeaders("Content-Type", "Authorization")));
    services.AddSingleton<EmbedConnectionRegistry>();
    services.AddHostedService(provider => provider.GetRequiredService<EmbedConnectionRegistry>());
  }

  public static void MapEmbedConnectionApi(this WebApplication app)
  {
    string[] allowedOrigins = app.Configuration.GetSection("Embed:AllowedOrigins").Get<string[]>() ?? [];
    var api = app.MapGroup("/api/embed/connections");
    api.AddEndpointFilter((context, next) =>
    {
      context.HttpContext.Response.Headers.CacheControl = "no-store";
      return next(context);
    });
    api.MapPost("", (RegisterRequest request, HttpContext context, EmbedConnectionRegistry registry) =>
    {
      string origin = context.Request.Headers.Origin.ToString();
      if (origin != OwnOrigin(context) && !allowedOrigins.Contains(origin, StringComparer.Ordinal))
        return Results.StatusCode(StatusCodes.Status403Forbidden);
      if (request.Component is not ("a" or "b" or "c")) return Results.BadRequest();
      var connection = registry.Register(origin, request.Component);
      return connection is null
        ? Results.StatusCode(StatusCodes.Status429TooManyRequests)
        : Results.Ok(connection);
    });
    api.MapGet("/{connectionId}/events", StreamAsync);
    api.MapPost("/{connectionId}/resize", (string connectionId, ResizeRequest request, HttpContext context, EmbedConnectionRegistry registry) =>
    {
      if (!double.IsFinite(request.Height) || request.Height <= 0 || request.Height > 10000000)
        return Results.BadRequest();
      return registry.Publish(connectionId, BearerToken(context), request.Height)
        ? Results.NoContent() : Results.Unauthorized();
    });
    api.MapDelete("/{connectionId}", (string connectionId, HttpContext context, EmbedConnectionRegistry registry) =>
      registry.Delete(connectionId, BearerToken(context), RequestOrigin(context))
        ? Results.NoContent() : Results.Unauthorized());
  }

  private static async Task StreamAsync(string connectionId, string token, HttpContext context, EmbedConnectionRegistry registry)
  {
    var reader = registry.OpenStream(connectionId, token, RequestOrigin(context));
    if (reader is null)
    {
      context.Response.StatusCode = StatusCodes.Status401Unauthorized;
      return;
    }
    var aborted = context.RequestAborted;
    context.Response.ContentType = "text/event-stream";
    context.Response.Headers["X-Accel-Buffering"] = "no";
    context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
    try
    {
      await context.Response.WriteAsync("event: connected\ndata: {}\n\n", aborted);
      await context.Response.Body.FlushAsync(aborted);
      while (!aborted.IsCancellationRequested)
      {
        using var heartbeat = CancellationTokenSource.CreateLinkedTokenSource(aborted);
        heartbeat.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
          if (!await reader.WaitToReadAsync(heartbeat.Token)) break;
          while (reader.TryRead(out double height))
            await context.Response.WriteAsync("event: resize\ndata: " + JsonSerializer.Serialize(new { height }) + "\n\n", aborted);
        }
        catch (OperationCanceledException) when (!aborted.IsCancellationRequested)
        {
          if (!registry.TouchStream(connectionId, reader)) break;
          await context.Response.WriteAsync(": keep-alive\n\n", aborted);
        }
        await context.Response.Body.FlushAsync(aborted);
      }
    }
    catch (OperationCanceledException) when (aborted.IsCancellationRequested) { }
    finally { registry.CloseStream(connectionId, reader); }
  }

  private static string OwnOrigin(HttpContext context) => $"{context.Request.Scheme}://{context.Request.Host}";
  private static string RequestOrigin(HttpContext context) => context.Request.Headers.Origin.Count > 0
    ? context.Request.Headers.Origin.ToString() : OwnOrigin(context);
  private static string BearerToken(HttpContext context)
  {
    string header = context.Request.Headers.Authorization.ToString();
    return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header[7..] : "";
  }

  public sealed record RegisterRequest(string Component);
  public sealed record ResizeRequest(double Height);
}
