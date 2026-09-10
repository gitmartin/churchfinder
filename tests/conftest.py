import os
from pathlib import Path
from uuid import uuid4

import pytest
from alembic import command
from alembic.config import Config
from fastapi.testclient import TestClient
from sqlalchemy.engine import make_url
from sqlalchemy.schema import CreateSchema, DropSchema

from churchfinder.config import Settings
from churchfinder.database import build_engine
from churchfinder.importer import import_batch
from churchfinder.main import create_app
from churchfinder.schemas import ChurchImportBatch

ROOT = Path(__file__).resolve().parents[1]


@pytest.fixture
def engine(tmp_path):
    """Use migrations on SQLite, or an isolated schema in a supplied PostgreSQL database."""
    postgres_url = os.getenv("TEST_DATABASE_URL")
    admin_engine = None
    if postgres_url:
        url = make_url(postgres_url)
        if url.get_backend_name() != "postgresql":
            raise ValueError("TEST_DATABASE_URL must be PostgreSQL; omit it for temporary SQLite")
        schema = f"test_{uuid4().hex}"
        admin_engine = build_engine(postgres_url)
        with admin_engine.begin() as connection:
            connection.execute(CreateSchema(schema))
        scoped_url = url.update_query_dict({"options": f"-csearch_path={schema}"})
        database_engine = build_engine(scoped_url.render_as_string(hide_password=False))
    else:
        database_engine = build_engine(f"sqlite:///{tmp_path / 'test.db'}")
    try:
        with database_engine.begin() as connection:
            config = Config(str(ROOT / "alembic.ini"))
            config.attributes["connection"] = connection
            command.upgrade(config, "head")
        yield database_engine
    finally:
        database_engine.dispose()
        if admin_engine is not None:
            with admin_engine.begin() as connection:
                connection.execute(DropSchema(schema, cascade=True))
            admin_engine.dispose()


@pytest.fixture
def batch():
    return ChurchImportBatch.model_validate_json((ROOT / "data/sample_churches.json").read_text())


@pytest.fixture
def client(engine, batch):
    import_batch(engine, batch)
    settings = Settings(_env_file=None, cors_origins=["http://localhost:5173"])
    with TestClient(create_app(settings, engine=engine)) as test_client:
        yield test_client
