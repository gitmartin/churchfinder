import json
import os
import subprocess
import sys

import pytest
from pydantic import ValidationError
from sqlalchemy import func, select
from sqlalchemy.exc import IntegrityError
from sqlalchemy.orm import Session

from churchfinder.importer import import_batch
from churchfinder.models import Church, ServiceTime
from churchfinder.schemas import ChurchImport, ChurchImportBatch


def run_import(engine, *args):
    return subprocess.run(
        [sys.executable, "-m", "churchfinder.importer", *map(str, args)],
        env={**os.environ, "DATABASE_URL": engine.url.render_as_string(hide_password=False)},
        capture_output=True,
        text=True,
        check=False,
    )


def test_reimport_preserves_identity_without_duplicate_services(engine, batch):
    assert import_batch(engine, batch) == {"created": 3, "updated": 0, "total": 3}
    with Session(engine) as session:
        church_ids = set(session.scalars(select(Church.id)))
        service_ids = set(session.scalars(select(ServiceTime.id)))
        created = dict(session.execute(select(Church.id, Church.created_at)).all())
    assert import_batch(engine, batch) == {"created": 0, "updated": 3, "total": 3}
    with Session(engine) as session:
        assert set(session.scalars(select(Church.id))) == church_ids
        assert set(session.scalars(select(ServiceTime.id))) == service_ids
        assert dict(session.execute(select(Church.id, Church.created_at)).all()) == created
        assert session.scalar(select(func.count()).select_from(ServiceTime)) == 4


def test_import_replaces_snapshots_updates_services_and_keeps_other_churches(engine, batch):
    import_batch(engine, batch)
    updated = batch.items[0].model_dump(mode="json")
    updated["name"] = "Updated church"
    updated["service_times"] = updated["service_times"][:1]
    updated["service_times"][0]["notes"] = "New notes"
    del updated["website_url"]
    import_batch(engine, ChurchImportBatch.model_validate({"items": [updated]}))
    with Session(engine) as session:
        church = session.scalar(select(Church).where(Church.source_id == "harbour-toronto"))
        assert church.name == "Updated church"
        assert church.website_url is None
        assert len(church.service_times) == 1
        assert church.service_times[0].notes == "New notes"
        assert session.scalar(select(func.count()).select_from(Church)) == 3
        assert session.scalar(select(func.count()).select_from(ServiceTime)) == 2


def test_clearing_services_removes_previous_schedule(engine, batch):
    import_batch(engine, batch)
    item = batch.items[0].model_dump(mode="json")
    item["service_times"] = []
    import_batch(engine, ChurchImportBatch.model_validate({"items": [item]}))
    with Session(engine) as session:
        assert session.scalar(select(func.count()).select_from(ServiceTime)) == 1


@pytest.mark.parametrize(
    "fields",
    [
        {"name": "  "},
        {"source_id": ""},
        {"source": ""},
        {"latitude": 20},
        {"latitude": 91, "longitude": 0},
        {"latitude": 0, "longitude": -181},
        {"latitude": float("nan"), "longitude": 0},
        {"latitude": 0, "longitude": float("inf")},
        {"timezone": "Not/AZone"},
        {"timezone": "../UTC"},
        {"last_verified_at": "2026-01-01T10:00:00"},
        {"website_url": "javascript:alert(1)"},
        {"email": "not-an-email"},
        {"country_code": "CAN"},
        {"unexpected": True},
        {"service_times": [{"day_of_week": 6, "start_time": "10:00"}]},
        {
            "timezone": "America/Toronto",
            "service_times": [{"day_of_week": 7, "start_time": "10:00"}],
        },
        {
            "timezone": "America/Toronto",
            "service_times": [{"day_of_week": 6, "start_time": "25:00"}],
        },
        {
            "timezone": "America/Toronto",
            "service_times": [{"day_of_week": 6, "start_time": "10:00Z"}],
        },
    ],
)
def test_invalid_collector_records_are_rejected(fields):
    with pytest.raises(ValidationError):
        ChurchImport.model_validate(
            {"source": "test", "source_id": "one", "name": "Test", **fields}
        )


def test_normalization_and_timezone_aware_verification(engine):
    batch = ChurchImportBatch.model_validate(
        {
            "items": [
                {
                    "source": " test ",
                    "source_id": "one",
                    "name": " Test ",
                    "denomination": " ",
                    "country_code": "ca",
                    "timezone": "America/Toronto",
                    "last_verified_at": "2026-01-01T10:00:00-05:00",
                    "service_times": [
                        {"day_of_week": 6, "start_time": "10:00", "language": "EN-ca"}
                    ],
                }
            ]
        }
    )
    import_batch(engine, batch)
    with Session(engine) as session:
        church = session.scalar(select(Church))
        assert church.name == "Test"
        assert church.denomination is None
        assert church.country_code == "CA"
        assert church.service_times[0].language == "en-ca"
        assert church.last_verified_at.isoformat() == "2026-01-01T15:00:00+00:00"


def test_duplicate_source_keys_and_services_are_rejected(batch):
    data = batch.model_dump(mode="json")
    data["items"].append(data["items"][0])
    with pytest.raises(ValidationError, match="Duplicate.*source"):
        ChurchImportBatch.model_validate(data)
    item = batch.items[0].model_dump(mode="json")
    item["service_times"].append(item["service_times"][0])
    with pytest.raises(ValidationError, match="Duplicate service"):
        ChurchImport.model_validate(item)


def test_cli_validates_before_writing_and_supports_dry_run(engine, batch, tmp_path):
    path = tmp_path / "churches.json"
    path.write_text(batch.model_dump_json())
    result = run_import(engine, path, "--validate-only")
    assert result.returncode == 0, result.stderr
    assert json.loads(result.stdout) == {"valid": True, "total": 3}
    with Session(engine) as session:
        assert session.scalar(select(func.count()).select_from(Church)) == 0
    invalid = batch.model_dump(mode="json")
    invalid["items"][-1]["name"] = ""
    path.write_text(json.dumps(invalid))
    result = run_import(engine, path)
    assert result.returncode == 1
    with Session(engine) as session:
        assert session.scalar(select(func.count()).select_from(Church)) == 0
    path.write_text(batch.model_dump_json())
    result = run_import(engine, path)
    assert result.returncode == 0, result.stderr
    assert json.loads(result.stdout)["created"] == 3


def test_cli_reports_invalid_json_and_missing_files(engine, tmp_path):
    path = tmp_path / "invalid.json"
    path.write_text("not json")
    assert run_import(engine, path).returncode == 1
    assert run_import(engine, tmp_path / "missing.json").returncode == 1
    schema = run_import(engine, "--schema")
    assert schema.returncode == 0
    assert "items" in json.loads(schema.stdout)["properties"]


def test_database_failure_rolls_back_whole_import(engine, batch):
    # Simulate a bad caller bypassing validation; database checks must still make the batch atomic.
    invalid = batch.model_copy(deep=True)
    invalid.items[-1].latitude = 91
    invalid.items[-1].longitude = 0
    with pytest.raises(IntegrityError):
        import_batch(engine, invalid)
    with Session(engine) as session:
        assert session.scalar(select(func.count()).select_from(Church)) == 0
        assert session.scalar(select(func.count()).select_from(ServiceTime)) == 0
