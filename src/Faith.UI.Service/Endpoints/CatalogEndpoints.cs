using Faith.UI.Service.Components;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Faith.UI.Service.Endpoints;

public static class CatalogEndpoints
{
  public static void MapComponentCatalog(this WebApplication app)
  {
    app.MapGet("/", () => new RazorComponentResult<CatalogPage>());
    app.MapGet("/components/{component}", (string component) => component is "a" or "b" or "c"
      ? (IResult)new RazorComponentResult<ComponentPage>(new { ComponentKey = component })
      : Results.NotFound());
  }
}
