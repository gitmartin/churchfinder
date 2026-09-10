from fastapi.testclient import TestClient

from churchfinder.config import Settings
from churchfinder.main import create_app


def test_frontend_config_and_embed_policy(engine, tmp_path):
    (tmp_path / "index.html").write_text("<!doctype html><title>ChurchFinder</title>")
    settings = Settings(
        _env_file=None,
        google_maps_api="test-browser-key",
        frontend_dist=tmp_path,
        embed_allowed_origins=["https://host.example"],
    )
    with TestClient(create_app(settings, engine)) as client:
        config = client.get("/api/v1/config")
        assert config.json() == {
            "google_maps_api_key": "test-browser-key",
            "google_maps_map_id": "DEMO_MAP_ID",
        }
        assert config.headers["cache-control"] == "no-store"
        response = client.get("/embed/churches")
        assert response.status_code == 200
        assert response.headers["content-security-policy"] == (
            "frame-ancestors 'self' https://host.example;"
        )
        assert client.get("/.env").status_code == 404
