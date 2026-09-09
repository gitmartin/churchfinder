# Faith.UI validation

Validated on September 9, 2026. Tests are limited to basic functionality.

- Active service and test projects built with zero warnings and zero errors.
- One SQLite check passed: create/seed the database, reopen it, and read the response.
- Six browser checks passed: catalog links, separate-origin loading with connection/sizing parameters, server-provided text, SSE height growth, width-driven reflow, and clear/shrink.
- Desktop and mobile screenshots were inspected for usable layout.
- Test servers and the isolated browser were stopped, with immediate and delayed process cleanup verified. Visual Studio's existing MSBuild workers were preserved.

Browser results: `artifacts/embed-browser-results.json`. Screenshots: `artifacts/catalog.png`, `artifacts/embed-desktop.png`, and `artifacts/embed-mobile.png`.

Builds used the existing package cache. No inherited account/admin suite or deployment tests were run. Connection caching is in-memory and targets one service instance.
