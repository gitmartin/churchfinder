from contextlib import asynccontextmanager

from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware
from sqlalchemy import Engine

from churchfinder.api import router
from churchfinder.config import Settings
from churchfinder.database import build_engine
from churchfinder.schemas import HealthResponse
from churchfinder.web import mount_frontend


def create_app(settings: Settings | None = None, engine: Engine | None = None) -> FastAPI:
    settings = settings or Settings()
    owns_engine = engine is None
    database_engine = engine if engine is not None else build_engine(settings.database_url)

    @asynccontextmanager
    async def lifespan(app: FastAPI):
        yield
        if owns_engine:
            database_engine.dispose()

    app = FastAPI(
        title="Church Finder API",
        version="0.1.0",
        description="Search church locations and weekly services for a directory and map.",
        lifespan=lifespan,
    )
    app.state.engine = database_engine
    app.add_middleware(
        CORSMiddleware,
        allow_origins=settings.cors_origins,
        allow_credentials=False,
        allow_methods=["GET"],
        allow_headers=["Accept", "Content-Type"],
    )
    app.include_router(router)
    mount_frontend(app, settings)

    @app.get("/health", response_model=HealthResponse, tags=["Health"])
    def health() -> HealthResponse:
        """Application liveness only; does not query the database."""
        return HealthResponse(status="ok")

    return app
