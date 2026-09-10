import argparse
import json
import sys
from pathlib import Path

from pydantic import ValidationError
from sqlalchemy import Engine, select
from sqlalchemy.exc import SQLAlchemyError
from sqlalchemy.orm import Session, selectinload

from churchfinder.config import Settings
from churchfinder.database import build_engine
from churchfinder.models import Church, ServiceTime, utc_now
from churchfinder.schemas import ChurchImportBatch


def import_batch(engine: Engine, batch: ChurchImportBatch) -> dict[str, int]:
    """Apply complete record snapshots atomically, matching churches by source identity."""
    created = updated = 0
    with Session(engine) as session, session.begin():
        for item in batch.items:
            church = session.scalar(
                select(Church)
                .where(Church.source == item.source, Church.source_id == item.source_id)
                .options(selectinload(Church.service_times))
            )
            fields = item.model_dump(exclude={"service_times"})
            for key in ("website_url", "source_url"):
                if fields[key] is not None:
                    fields[key] = str(fields[key])
            if church is None:
                church = Church(**fields)
                session.add(church)
                created += 1
            else:
                for key, value in fields.items():
                    setattr(church, key, value)
                updated += 1

            existing = {
                (s.day_of_week, s.start_time, s.language, s.label): s for s in church.service_times
            }
            services = []
            for service in item.service_times:
                key = (service.day_of_week, service.start_time, service.language, service.label)
                record = existing.get(key)
                if record is None:
                    record = ServiceTime(**service.model_dump())
                else:
                    record.notes = service.notes
                services.append(record)
            church.service_times = services
            church.updated_at = utc_now()
    return {"created": created, "updated": updated, "total": len(batch.items)}


def main() -> int:
    parser = argparse.ArgumentParser(description="Import a JSON church dataset into the database")
    parser.add_argument("path", type=Path, nargs="?", help="JSON file containing an items array")
    parser.add_argument("--validate-only", action="store_true", help="Validate without a database")
    parser.add_argument("--schema", action="store_true", help="Print the JSON Schema and exit")
    args = parser.parse_args()
    if args.schema:
        print(json.dumps(ChurchImportBatch.model_json_schema(), indent=2))
        return 0
    if args.path is None:
        parser.error("path is required unless --schema is used")
    try:
        batch = ChurchImportBatch.model_validate_json(args.path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, ValidationError) as exc:
        print(f"Import failed: {exc}", file=sys.stderr)
        return 1
    if args.validate_only:
        print(json.dumps({"valid": True, "total": len(batch.items)}))
        return 0

    engine = build_engine(Settings().database_url)
    try:
        result = import_batch(engine, batch)
    except SQLAlchemyError:
        print(
            "Import failed; no changes committed. Check DATABASE_URL, database constraints, "
            "and that 'alembic upgrade head' has run.",
            file=sys.stderr,
        )
        return 1
    finally:
        engine.dispose()
    print(json.dumps(result))
    return 0


if __name__ == "__main__":
    sys.exit(main())
