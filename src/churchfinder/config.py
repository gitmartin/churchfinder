from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_file=".env", env_file_encoding="utf-8", extra="ignore")

    database_url: str = "postgresql+psycopg://churchfinder:churchfinder@localhost:5432/churchfinder"
    cors_origins: list[str] = ["http://localhost:5173", "http://localhost:3000"]
