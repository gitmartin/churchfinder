from typing import Annotated
from uuid import UUID

from fastapi import APIRouter, HTTPException, Query
from sqlalchemy import func, or_, select
from sqlalchemy.orm import selectinload

from churchfinder.database import DatabaseSession
from churchfinder.models import Church, ServiceTime
from churchfinder.schemas import (
    BoundingBox,
    ChurchDetail,
    ChurchPage,
    ChurchSummary,
    ErrorResponse,
    FilterOptions,
)

router = APIRouter(prefix="/api/v1", tags=["Church discovery"])


@router.get("/churches", response_model=ChurchPage)
def list_churches(
    session: DatabaseSession,
    q: Annotated[
        str | None, Query(max_length=200, description="Name, city, or street substring")
    ] = None,
    city: Annotated[str | None, Query(max_length=100)] = None,
    denomination: Annotated[str | None, Query(max_length=100)] = None,
    language: Annotated[str | None, Query(max_length=35)] = None,
    bbox: Annotated[
        BoundingBox | None,
        Query(description="Inclusive west,south,east,north bounds; no antimeridian crossing"),
    ] = None,
    limit: Annotated[int, Query(ge=1, le=200)] = 50,
    offset: Annotated[int, Query(ge=0)] = 0,
) -> ChurchPage:
    """Combine all filters with AND; total counts all matches before pagination."""
    conditions = []
    if q and q.strip():
        # Treat SQL wildcard characters as literal search input.
        conditions.append(
            or_(
                Church.name.icontains(q.strip(), autoescape=True),
                Church.city.icontains(q.strip(), autoescape=True),
                Church.address_line1.icontains(q.strip(), autoescape=True),
            )
        )
    if city and city.strip():
        conditions.append(func.lower(Church.city) == city.strip().lower())
    if denomination and denomination.strip():
        conditions.append(Church.denomination == denomination.strip())
    if language and language.strip():
        conditions.append(
            Church.service_times.any(ServiceTime.language == language.strip().lower())
        )
    if bbox:
        west, south, east, north = [float(part) for part in bbox.split(",")]
        conditions.extend(
            [Church.longitude.between(west, east), Church.latitude.between(south, north)]
        )

    total = session.scalar(select(func.count()).select_from(Church).where(*conditions)) or 0
    churches = session.scalars(
        select(Church)
        .where(*conditions)
        .order_by(Church.name, Church.id)
        .limit(limit)
        .offset(offset)
    )
    return ChurchPage(
        items=[ChurchSummary.model_validate(church) for church in churches],
        total=total,
        limit=limit,
        offset=offset,
    )


@router.get(
    "/churches/{church_id}",
    response_model=ChurchDetail,
    responses={404: {"model": ErrorResponse, "description": "Church not found"}},
)
def get_church(church_id: UUID, session: DatabaseSession) -> Church:
    church = session.scalar(
        select(Church).where(Church.id == church_id).options(selectinload(Church.service_times))
    )
    if church is None:
        raise HTTPException(status_code=404, detail="Church not found")
    return church


@router.get("/filters", response_model=FilterOptions)
def get_filters(session: DatabaseSession) -> FilterOptions:
    """Return global, sorted filter options; null values are omitted."""

    def values(column) -> list[str]:
        return list(
            session.scalars(select(column).where(column.is_not(None)).distinct().order_by(column))
        )

    return FilterOptions(
        denominations=values(Church.denomination),
        cities=values(Church.city),
        languages=values(ServiceTime.language),
    )
