from fastapi import FastAPI, HTTPException
from fastapi.responses import FileResponse, JSONResponse
from fastapi.staticfiles import StaticFiles

from churchfinder.config import Settings


def mount_frontend(app: FastAPI, settings: Settings) -> None:
    """Serve only the compiled frontend, never the repository or environment files."""
    dist = settings.frontend_dist
    frame_policy = "frame-ancestors 'self' " + " ".join(settings.embed_allowed_origins) + ";"

    @app.get("/api/v1/config", tags=["Frontend"])
    def frontend_config() -> JSONResponse:
        # This browser key is intentionally public. Never serialize all Settings here.
        return JSONResponse(
            {
                "google_maps_api_key": settings.google_maps_api,
                "google_maps_map_id": settings.google_maps_map_id,
            },
            headers={"Cache-Control": "no-store"},
        )

    def public_file(name: str) -> FileResponse:
        path = dist / name
        if not path.is_file():
            raise HTTPException(
                503, "Frontend not built. Run npm ci and npm run build in frontend/."
            )
        return FileResponse(
            path,
            headers={"Content-Security-Policy": frame_policy, "Cache-Control": "no-cache"},
        )

    @app.get("/", include_in_schema=False)
    @app.get("/embed/churches", include_in_schema=False)
    def church_map() -> FileResponse:
        return public_file("index.html")

    @app.get("/churchfinder.js", include_in_schema=False)
    def embed_loader() -> FileResponse:
        return public_file("churchfinder.js")

    @app.get("/embed-demo.html", include_in_schema=False)
    def embed_demo() -> FileResponse:
        return public_file("embed-demo.html")

    @app.get("/favicon.svg", include_in_schema=False)
    def favicon() -> FileResponse:
        return public_file("favicon.svg")

    # check_dir=False allows API-only startup before a frontend build exists.
    app.mount("/assets", StaticFiles(directory=dist / "assets", check_dir=False), name="assets")
