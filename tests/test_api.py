from uuid import UUID, uuid4

import pytest
from sqlalchemy.orm import Session

from churchfinder.models import Church


def test_list_returns_page_contract(client):
    response = client.get("/api/v1/churches")
    assert response.status_code == 200
    page = response.json()
    assert {k: page[k] for k in ("total", "limit", "offset")} == {
        "total": 3,
        "limit": 50,
        "offset": 0,
    }
    assert [item["name"] for item in page["items"]] == [
        "Cedar Fellowship (Demo)",
        "Harbour Community Church (Demo)",
        "Hope Gathering (Demo)",
    ]
    assert set(page["items"][0]) == {
        "id",
        "name",
        "denomination",
        "address_line1",
        "city",
        "region",
        "postal_code",
        "country_code",
        "latitude",
        "longitude",
    }
    assert page["items"][2]["latitude"] is None


@pytest.mark.parametrize(
    ("params", "expected"),
    [
        ({"q": "HARBOUR"}, ["Harbour Community Church (Demo)"]),
        ({"q": "ronto"}, ["Harbour Community Church (Demo)", "Hope Gathering (Demo)"]),
        ({"q": "sample avenue"}, ["Cedar Fellowship (Demo)"]),
        ({"city": " tOrOnTo "}, ["Harbour Community Church (Demo)", "Hope Gathering (Demo)"]),
        ({"denomination": "Baptist"}, ["Cedar Fellowship (Demo)"]),
        ({"denomination": "baptist"}, []),
        ({"language": "FR"}, ["Harbour Community Church (Demo)"]),
        (
            {
                "q": "church",
                "city": "Toronto",
                "denomination": "Non-denominational",
                "language": "en",
            },
            ["Harbour Community Church (Demo)"],
        ),
        ({"city": "Toronto", "denomination": "Baptist"}, []),
        ({"q": "does-not-exist"}, []),
        ({"q": "%"}, []),
        ({"q": "_"}, []),
        ({"q": "' OR 1=1 --"}, []),
    ],
)
def test_search_and_filters(client, params, expected):
    response = client.get("/api/v1/churches", params=params)
    assert response.status_code == 200
    assert [item["name"] for item in response.json()["items"]] == expected
    assert response.json()["total"] == len(expected)


def test_language_filter_does_not_duplicate_churches(client):
    page = client.get("/api/v1/churches", params={"language": "en"}).json()
    assert page["total"] == 2
    assert len(page["items"]) == 2
    assert len({item["id"] for item in page["items"]}) == 2


def test_pagination_counts_all_filtered_matches(client):
    page = client.get(
        "/api/v1/churches", params={"city": "Toronto", "limit": 1, "offset": 1}
    ).json()
    assert page["total"] == 2
    assert page["limit"] == 1
    assert page["offset"] == 1
    assert page["items"][0]["name"] == "Hope Gathering (Demo)"
    empty_page = client.get("/api/v1/churches", params={"offset": 999}).json()
    assert empty_page["items"] == []
    assert empty_page["total"] == 3


def test_equal_names_are_ordered_by_id(client, seeded_engine):
    with Session(seeded_engine) as session, session.begin():
        for number in (2, 1):
            session.add(
                Church(id=UUID(int=number), source="test", source_id=str(number), name="Identical")
            )
    first = client.get("/api/v1/churches", params={"q": "Identical", "limit": 1}).json()
    second = client.get(
        "/api/v1/churches", params={"q": "Identical", "limit": 1, "offset": 1}
    ).json()
    assert first["items"][0]["id"] == str(UUID(int=1))
    assert second["items"][0]["id"] == str(UUID(int=2))


def test_bounds_filter_before_pagination_and_exclude_unmapped(client):
    page = client.get(
        "/api/v1/churches", params={"bbox": "-79.5,43.6,-79.3,43.8", "limit": 1}
    ).json()
    assert page["total"] == 1
    assert page["items"][0]["name"] == "Harbour Community Church (Demo)"
    world = client.get("/api/v1/churches", params={"bbox": "-180,-90,180,90", "limit": 1}).json()
    assert world["total"] == 2
    assert len(world["items"]) == 1
    combined = client.get(
        "/api/v1/churches", params={"bbox": "-79.5,43.6,-79.3,43.8", "denomination": "Baptist"}
    ).json()
    assert combined["total"] == 0


def test_bounds_include_edges(client):
    page = client.get(
        "/api/v1/churches", params={"bbox": "-79.3832,43.6532,-79.3832,43.6532"}
    ).json()
    assert page["total"] == 1


@pytest.mark.parametrize(
    "bbox",
    [
        "",
        "1,2,3",
        "1,2,3,4,5",
        "a,b,c,d",
        "nan,0,1,1",
        "-inf,0,1,1",
        "0,0,inf,1",
        "-181,0,0,1",
        "0,-91,1,1",
        "0,0,181,1",
        "0,0,1,91",
        "170,-10,-170,10",
        "0,5,1,4",
    ],
)
def test_invalid_bounds_return_422(client, bbox):
    response = client.get("/api/v1/churches", params={"bbox": bbox})
    assert response.status_code == 422
    assert response.json()["detail"][0]["loc"] == ["query", "bbox"]


@pytest.mark.parametrize(
    "params",
    [
        {"limit": 0},
        {"limit": 201},
        {"limit": "abc"},
        {"limit": 1.5},
        {"offset": -1},
        {"offset": "abc"},
        {"q": "x" * 201},
    ],
)
def test_invalid_pagination_and_query(client, params):
    assert client.get("/api/v1/churches", params=params).status_code == 422


def test_detail_has_local_services_contacts_and_utc_timestamps(client):
    church_id = client.get("/api/v1/churches", params={"q": "Harbour"}).json()["items"][0]["id"]
    response = client.get(f"/api/v1/churches/{church_id}")
    assert response.status_code == 200
    detail = response.json()
    assert detail["source"] == "demo"
    assert detail["timezone"] == "America/Toronto"
    assert detail["email"] == "harbour@example.org"
    assert detail["website_url"] == "https://example.org/harbour"
    assert [service["day_of_week"] for service in detail["service_times"]] == [2, 6, 6]
    assert detail["service_times"][1]["start_time"] == "09:00:00"
    assert detail["created_at"].endswith("Z")
    assert detail["updated_at"].endswith("Z")
    assert detail["last_verified_at"] is None


def test_missing_optional_details(client):
    church_id = client.get("/api/v1/churches", params={"q": "Hope"}).json()["items"][0]["id"]
    detail = client.get(f"/api/v1/churches/{church_id}").json()
    assert detail["service_times"] == []
    assert detail["website_url"] is None
    assert detail["timezone"] is None
    assert detail["latitude"] is None


def test_unknown_and_invalid_ids(client):
    response = client.get(f"/api/v1/churches/{uuid4()}")
    assert response.status_code == 404
    assert response.json() == {"detail": "Church not found"}
    assert client.get("/api/v1/churches/not-a-uuid").status_code == 422


def test_global_filter_options(client):
    assert client.get("/api/v1/filters").json() == {
        "denominations": ["Baptist", "Non-denominational"],
        "cities": ["Mississauga", "Toronto"],
        "languages": ["en", "fr"],
    }


def test_health_and_openapi(client):
    assert client.get("/health").json() == {"status": "ok"}
    assert client.get("/docs").status_code == 200
    spec = client.get("/openapi.json").json()
    assert set(spec["paths"]) == {
        "/api/v1/churches",
        "/api/v1/churches/{church_id}",
        "/api/v1/filters",
        "/health",
    }
    assert "404" in spec["paths"]["/api/v1/churches/{church_id}"]["get"]["responses"]
    assert client.post("/api/v1/churches", json={}).status_code == 405


def test_cors_for_allowed_and_unknown_origins(client):
    response = client.options(
        "/api/v1/churches",
        headers={"Origin": "http://localhost:5173", "Access-Control-Request-Method": "GET"},
    )
    assert response.status_code == 200
    assert response.headers["access-control-allow-origin"] == "http://localhost:5173"
    response = client.get("/api/v1/churches", headers={"Origin": "https://unapproved.example"})
    assert "access-control-allow-origin" not in response.headers
