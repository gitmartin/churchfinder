import json
from pathlib import Path

from sqlalchemy import func, select
from sqlalchemy.orm import Session

from churchfinder.importer import import_batch, main, read_batch
from churchfinder.models import Church, ServiceTime
from churchfinder.schemas import ChurchImportBatch


def test_toronto_research_import_keeps_coordinates_and_services(engine):
    path = Path(__file__).resolve().parents[1] / "data" / "seed-toronto-10.json"
    batch = read_batch(path.read_text(encoding="utf-8"), "research")
    assert import_batch(engine, batch)["created"] == 10
    with Session(engine) as session:
        assert len(list(session.scalars(select(Church).where(Church.latitude.is_not(None))))) == 9
        church = session.scalar(select(Church).where(Church.source_id == "tor-grace-toronto-pca"))
        assert church.latitude == 43.662286
        assert church.source == "manual-research"
        assert len(church.service_times) == 2


def test_reimport_preserves_ids_without_creating_duplicates(engine, batch):
    assert import_batch(engine, batch) == {"created": 3, "updated": 0, "total": 3}
    with Session(engine) as session:
        church_ids = set(session.scalars(select(Church.id)))
        service_ids = set(session.scalars(select(ServiceTime.id)))

    assert import_batch(engine, batch) == {"created": 0, "updated": 3, "total": 3}
    with Session(engine) as session:
        assert set(session.scalars(select(Church.id))) == church_ids
        assert set(session.scalars(select(ServiceTime.id))) == service_ids
        assert session.scalar(select(func.count()).select_from(Church)) == 3
        assert session.scalar(select(func.count()).select_from(ServiceTime)) == 4


def test_updated_import_replaces_the_schedule_and_keeps_other_churches(engine, batch):
    import_batch(engine, batch)
    updated = batch.items[0].model_dump(mode="json")
    updated["name"] = "Updated church"
    updated["service_times"] = updated["service_times"][:1]
    updated["service_times"][0]["notes"] = "New notes"
    import_batch(engine, ChurchImportBatch.model_validate({"items": [updated]}))

    with Session(engine) as session:
        church = session.scalar(select(Church).where(Church.source_id == "harbour-toronto"))
        assert church.name == "Updated church"
        assert len(church.service_times) == 1
        assert church.service_times[0].notes == "New notes"
        assert session.scalar(select(func.count()).select_from(Church)) == 3
        assert session.scalar(select(func.count()).select_from(ServiceTime)) == 2


def test_invalid_batch_is_rejected_without_importing_any_records(
    engine, batch, tmp_path, monkeypatch
):
    data = batch.model_dump(mode="json")
    data["items"][-1].update(latitude=91, longitude=0)
    path = tmp_path / "invalid_churches.json"
    path.write_text(json.dumps(data), encoding="utf-8")
    monkeypatch.setenv("DATABASE_URL", engine.url.render_as_string(hide_password=False))
    monkeypatch.setattr("sys.argv", ["churchfinder-import", str(path)])

    assert main() == 1
    with Session(engine) as session:
        assert session.scalar(select(func.count()).select_from(Church)) == 0
        assert session.scalar(select(func.count()).select_from(ServiceTime)) == 0
