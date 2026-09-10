from pathlib import Path
from urllib.parse import urlsplit

from pydantic import field_validator
from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_file=".env", env_file_encoding="utf-8", extra="ignore")

    database_url: str = "postgresql+psycopg://churchfinder:churchfinder@localhost:5432/churchfinder"
    cors_origins: list[str] = ["http://localhost:5173", "http://localhost:3000"]
    google_maps_api: str = ""
    google_maps_map_id: str = "DEMO_MAP_ID"
    frontend_dist: Path = Path(__file__).resolve().parents[2] / "frontend" / "dist"
    embed_allowed_origins: list[str] = []

    @field_validator("embed_allowed_origins")
    @classmethod
    def exact_origins(cls, values: list[str]) -> list[str]:
        for value in values:
            parsed = urlsplit(value)
            if (
                parsed.scheme not in {"http", "https"}
                or not parsed.netloc
                or parsed.username is not None
                or parsed.password is not None
                or value != f"{parsed.scheme}://{parsed.netloc}"
                or any(c.isspace() or c in "*;'\"" for c in value)
            ):
                raise ValueError("EMBED_ALLOWED_ORIGINS must contain exact HTTP(S) origins")
        return values
