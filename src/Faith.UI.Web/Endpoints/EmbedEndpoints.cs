using Faith.UI.Embedding;
using Faith.UI.Logic;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Faith.UI.Web.Endpoints;

public static class EmbedEndpoints
{
  public static void UseFaithEmbeds(this WebApplication app)
  {
    string[] allowedOrigins = app.Configuration.GetSection("Embed:AllowedOrigins").Get<string[]>() ?? [];
    foreach (string allowed in allowedOrigins)
    {
      if (!Uri.TryCreate(allowed, UriKind.Absolute, out var uri)
        || uri.Scheme is not ("http" or "https")
        || uri.GetLeftPart(UriPartial.Authority) != allowed)
        throw new InvalidOperationException("Embed:AllowedOrigins must contain exact HTTP(S) origins without paths.");
    }

    // The demonstration is anonymous. Text is loaded from local SQLite;
    // iframe sizing remains in the browser.
    app.Map("/embed", branch => branch.Run(async context =>
    {
      context.Response.Headers.CacheControl = "no-store";
      if (context.Request.Path != "/lorem" && context.Request.Path != "/api/lorem")
      {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
      }
      if (!HttpMethods.IsGet(context.Request.Method))
      {
        context.Response.Headers.Allow = "GET";
        context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
        return;
      }
      if (context.Request.Path == "/api/lorem")
      {
        var store = context.RequestServices.GetRequiredService<SampleTextStore>();
        await context.Response.WriteAsJsonAsync(new { text = await store.GetLoremAsync() });
        return;
      }

      var query = context.Request.Query;
      string parentOrigin = query["parentOrigin"].ToString();
      string ownOrigin = $"{context.Request.Scheme}://{context.Request.Host}";
      if (parentOrigin.Length > 0 && parentOrigin != ownOrigin
        && !allowedOrigins.Contains(parentOrigin, StringComparer.Ordinal))
      {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsync("This website is not allowed to embed the control.");
        return;
      }
      string instanceId = query["instanceId"].ToString();
      if (instanceId.Length > 100 || instanceId.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
      {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
      }
      int minHeight = ReadSize(query, "minHeight", 120, 120, 10000);
      int maxHeight = ReadSize(query, "maxHeight", 10000, minHeight, 10000);
      context.Response.Headers.Remove("X-Frame-Options");
      context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; script-src 'self'; "
        + "style-src 'self'; connect-src 'self'; base-uri 'none'; form-action 'none'; "
        + "frame-ancestors 'self' " + string.Join(' ', allowedOrigins) + ";";
      await new RazorComponentResult<EmbedDocument>(new
      {
        ParentOrigin = parentOrigin,
        InstanceId = instanceId,
        Width = ReadSize(query, "width", 640, 1, 10000),
        InitialHeight = ReadSize(query, "initialHeight", 280, minHeight, maxHeight),
        MinHeight = minHeight,
        MaxHeight = maxHeight
      }).ExecuteAsync(context);
    }));
  }

  private static int ReadSize(IQueryCollection query, string key, int fallback, int min, int max)
    => int.TryParse(query[key], out int value) ? Math.Clamp(value, min, max) : Math.Clamp(fallback, min, max);
}
