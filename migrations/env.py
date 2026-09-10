from alembic import context

from churchfinder.config import Settings
from churchfinder.database import build_engine
from churchfinder.models import Base

config = context.config
target_metadata = Base.metadata


def run_migrations(connection):
    context.configure(connection=connection, target_metadata=target_metadata, compare_type=True)
    with context.begin_transaction():
        context.run_migrations()


if context.is_offline_mode():
    context.configure(
        url=Settings().database_url,
        target_metadata=target_metadata,
        literal_binds=True,
        dialect_opts={"paramstyle": "named"},
    )
    with context.begin_transaction():
        context.run_migrations()
elif (connection := config.attributes.get("connection")) is not None:
    run_migrations(connection)
else:
    engine = build_engine(Settings().database_url)
    try:
        with engine.connect() as connection:
            run_migrations(connection)
    finally:
        engine.dispose()
