import pytest
from alembic import command
from alembic.autogenerate import compare_metadata
from alembic.migration import MigrationContext
from conftest import migration_config
from sqlalchemy import func, inspect, select
from sqlalchemy.exc import IntegrityError
from sqlalchemy.orm import Session

from churchfinder.models import Base, Church, ServiceTime


def test_migration_matches_models_and_can_be_reversed(engine):
    with engine.begin() as connection:
        assert compare_metadata(MigrationContext.configure(connection), Base.metadata) == []
        config = migration_config(connection)
        command.downgrade(config, "base")
        assert "churches" not in inspect(connection).get_table_names()
        command.upgrade(config, "head")
        assert {"churches", "service_times"}.issubset(inspect(connection).get_table_names())


@pytest.mark.parametrize(
    "fields",
    [
        {"name": " "},
        {"latitude": 0},
        {"longitude": 0},
        {"latitude": 91, "longitude": 0},
        {"latitude": 0, "longitude": -181},
        {"source": ""},
        {"source_id": ""},
    ],
)
def test_database_constraints(engine, fields):
    with Session(engine) as session, pytest.raises(IntegrityError):
        session.add(Church(**{"source": "test", "source_id": "test", "name": "Test", **fields}))
        session.commit()


def test_duplicate_source_identity_fails_and_deletion_cascades(seeded_engine):
    with Session(seeded_engine) as session, pytest.raises(IntegrityError):
        session.add(Church(source="demo", source_id="harbour-toronto", name="Duplicate"))
        session.commit()
    with Session(seeded_engine) as session, session.begin():
        church = session.scalar(select(Church).where(Church.source_id == "harbour-toronto"))
        session.delete(church)
    with Session(seeded_engine) as session:
        assert session.scalar(select(func.count()).select_from(ServiceTime)) == 1
