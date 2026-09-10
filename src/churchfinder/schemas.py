import math
from datetime import datetime, time
from typing import Annotated, Self
from uuid import UUID
from zoneinfo import ZoneInfo, ZoneInfoNotFoundError

from pydantic import (
    AfterValidator,
    AwareDatetime,
    BaseModel,
    ConfigDict,
    EmailStr,
    Field,
    HttpUrl,
    StringConstraints,
    field_validator,
    model_validator,
)

NonBlank = Annotated[str, StringConstraints(strip_whitespace=True, min_length=1)]
Language = Annotated[
    str,
    StringConstraints(
        strip_whitespace=True,
        to_lower=True,
        max_length=35,
        pattern=r"^[a-z]{2,3}(-[a-z0-9]{2,8})*$",
    ),
]


class ImportModel(BaseModel):
    model_config = ConfigDict(extra="forbid", str_strip_whitespace=True, allow_inf_nan=False)

    @field_validator("*", mode="before")
    @classmethod
    def empty_strings_to_none(cls, value):
        return None if isinstance(value, str) and not value.strip() else value


class ServiceTimeImport(ImportModel):
    day_of_week: int = Field(ge=0, le=6, strict=True)
    start_time: time
    language: Language | None = None
    label: str | None = Field(default=None, max_length=100)
    notes: str | None = None

    @field_validator("language", mode="before")
    @classmethod
    def normalize_language(cls, value):
        return value.strip().lower() if isinstance(value, str) else value

    @field_validator("start_time")
    @classmethod
    def local_time_only(cls, value: time) -> time:
        if value.tzinfo is not None:
            raise ValueError("Use a local time without an offset and set the church timezone")
        return value


class ChurchImport(ImportModel):
    source: NonBlank = Field(max_length=100)
    source_id: NonBlank = Field(max_length=255)
    name: NonBlank = Field(max_length=255)
    denomination: str | None = Field(default=None, max_length=100)
    description: str | None = None
    address_line1: str | None = None
    city: str | None = Field(default=None, max_length=100)
    region: str | None = Field(default=None, max_length=100)
    postal_code: str | None = Field(default=None, max_length=20)
    country_code: str | None = Field(default=None, pattern=r"^[A-Z]{2}$")
    latitude: float | None = Field(default=None, ge=-90, le=90)
    longitude: float | None = Field(default=None, ge=-180, le=180)
    timezone: str | None = Field(default=None, max_length=100)
    website_url: HttpUrl | None = None
    phone: str | None = Field(default=None, max_length=50)
    email: EmailStr | None = Field(default=None, max_length=254)
    source_url: HttpUrl | None = None
    last_verified_at: AwareDatetime | None = None
    service_times: list[ServiceTimeImport] = Field(default_factory=list)

    @field_validator("country_code", mode="before")
    @classmethod
    def normalize_country(cls, value):
        return value.strip().upper() if isinstance(value, str) else value

    @field_validator("timezone")
    @classmethod
    def valid_timezone(cls, value: str | None) -> str | None:
        if value is not None:
            try:
                ZoneInfo(value)
            except (ZoneInfoNotFoundError, ValueError) as exc:
                raise ValueError("Use an IANA timezone, such as America/Toronto") from exc
        return value

    @model_validator(mode="after")
    def validate_location_and_services(self) -> Self:
        if (self.latitude is None) != (self.longitude is None):
            raise ValueError("latitude and longitude must both be present or both be null")
        if self.service_times and self.timezone is None:
            raise ValueError("timezone is required when service_times are supplied")
        keys = [(s.day_of_week, s.start_time, s.language, s.label) for s in self.service_times]
        if len(keys) != len(set(keys)):
            raise ValueError("Duplicate service times in one church record")
        return self


class ChurchImportBatch(ImportModel):
    items: list[ChurchImport]

    @model_validator(mode="after")
    def unique_source_ids(self) -> Self:
        keys = [(item.source, item.source_id) for item in self.items]
        if len(keys) != len(set(keys)):
            raise ValueError("Duplicate (source, source_id) in import batch")
        return self


class ReadModel(BaseModel):
    model_config = ConfigDict(from_attributes=True)


class ChurchSummary(ReadModel):
    id: UUID
    name: str
    denomination: str | None
    address_line1: str | None
    city: str | None
    region: str | None
    postal_code: str | None
    country_code: str | None
    latitude: float | None
    longitude: float | None


class ServiceTimeRead(ReadModel):
    id: UUID
    day_of_week: int
    start_time: time
    language: str | None
    label: str | None
    notes: str | None


class ChurchDetail(ChurchSummary):
    source: str
    source_id: str
    description: str | None
    timezone: str | None
    website_url: str | None
    phone: str | None
    email: str | None
    source_url: str | None
    last_verified_at: datetime | None
    created_at: datetime
    updated_at: datetime
    service_times: list[ServiceTimeRead]


class ChurchPage(BaseModel):
    items: list[ChurchSummary]
    total: int
    limit: int
    offset: int


class FilterOptions(BaseModel):
    denominations: list[str]
    cities: list[str]
    languages: list[str]


class ErrorResponse(BaseModel):
    detail: str


class HealthResponse(BaseModel):
    status: str


def validate_bbox(value: str) -> str:
    try:
        west, south, east, north = [float(part) for part in value.split(",")]
    except ValueError as exc:
        raise ValueError("bbox must contain four numbers: west,south,east,north") from exc
    if not all(math.isfinite(v) for v in (west, south, east, north)):
        raise ValueError("bbox coordinates must be finite")
    if not (-180 <= west <= east <= 180 and -90 <= south <= north <= 90):
        raise ValueError(
            "bbox requires -180 <= west <= east <= 180 and -90 <= south <= north <= 90; "
            "antimeridian-crossing bounds are not supported"
        )
    return value


BoundingBox = Annotated[str, AfterValidator(validate_bbox)]
