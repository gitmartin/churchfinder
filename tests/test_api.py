def test_search_combines_filters_without_duplicate_churches(client):
    params = {
        "q": "HARBOUR",
        "city": "toronto",
        "denomination": "Non-denominational",
        "language": "en",
    }
    response = client.get("/api/v1/churches", params=params)
    assert response.status_code == 200
    page = response.json()
    # Harbour has two English services, but should appear only once.
    assert page["total"] == 1
    assert [item["name"] for item in page["items"]] == ["Harbour Community Church (Demo)"]

    params["city"] = "Mississauga"
    empty_page = client.get("/api/v1/churches", params=params).json()
    assert empty_page["items"] == []
    assert empty_page["total"] == 0


def test_pagination_preserves_the_matching_total(client):
    response = client.get("/api/v1/churches", params={"city": "Toronto", "limit": 1, "offset": 1})
    assert response.status_code == 200
    page = response.json()
    assert page["total"] == 2
    assert page["limit"] == 1
    assert page["offset"] == 1
    assert [item["name"] for item in page["items"]] == ["Hope Gathering (Demo)"]


def test_map_bounds_exclude_outside_and_unmapped_churches(client):
    response = client.get("/api/v1/churches", params={"bbox": "-79.5,43.6,-79.3,43.8"})
    assert response.status_code == 200
    page = response.json()
    assert page["total"] == 1
    assert [item["name"] for item in page["items"]] == ["Harbour Community Church (Demo)"]
    assert page["items"][0]["latitude"] == 43.6532
    assert page["items"][0]["longitude"] == -79.3832


def test_invalid_map_bounds_return_a_validation_error(client):
    response = client.get("/api/v1/churches", params={"bbox": "invalid"})
    assert response.status_code == 422
    assert response.json()["detail"][0]["loc"] == ["query", "bbox"]


def test_church_details_include_contacts_and_local_service_times(client):
    church_id = client.get("/api/v1/churches", params={"q": "Harbour"}).json()["items"][0]["id"]
    response = client.get(f"/api/v1/churches/{church_id}")
    assert response.status_code == 200
    detail = response.json()
    assert detail["id"] == church_id
    assert detail["timezone"] == "America/Toronto"
    assert detail["email"] == "harbour@example.org"
    assert detail["website_url"] == "https://example.org/harbour"
    assert len(detail["service_times"]) == 3
    assert detail["service_times"][1]["day_of_week"] == 6
    assert detail["service_times"][1]["start_time"] == "09:00:00"
