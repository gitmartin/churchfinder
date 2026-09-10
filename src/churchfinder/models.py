from datetime import UTC, datetime, time
from uuid import UUID, uuid4

from sqlalchemy import CheckConstraint, DateTime, Double, ForeignKey, String, UniqueConstraint, func
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column, relationship
from sqlalchemy.types import TypeDecorator


def utc_now() -> datetime:
    return datetime.now(UTC)


class UTCDateTime(TypeDecorator):
    """Retain UTC offsets on reads, including SQLite's timezone-naive storage."""

    impl = DateTime(timezone=True)
    cache_ok = True

    def process_bind_param(self, value, dialect):
        if value is None:
            return None
        if value.tzinfo is None:
            raise ValueError("Timestamps must include a timezone")
        return value.astimezone(UTC)

    def process_result_value(self, value, dialect):
        if value is None:
            return None
        return value.replace(tzinfo=UTC) if value.tzinfo is None else value.astimezone(UTC)


class Base(DeclarativeBase):
    pass


class Church(Base):
    __tablename__ = "churches"
    __table_args__ = (
        UniqueConstraint("source", "source_id", name="uq_churches_source_source_id"),
        CheckConstraint("length(trim(name)) > 0", name="ck_churches_name"),
        CheckConstraint("length(trim(source)) > 0", name="ck_churches_source"),
        CheckConstraint("length(trim(source_id)) > 0", name="ck_churches_source_id"),
        CheckConstraint(
            "(latitude IS NULL AND longitude IS NULL) OR "
            "(latitude IS NOT NULL AND longitude IS NOT NULL)",
            name="ck_churches_coordinate_pair",
        ),
        CheckConstraint("latitude BETWEEN -90 AND 90", name="ck_churches_latitude"),
        CheckConstraint("longitude BETWEEN -180 AND 180", name="ck_churches_longitude"),
    )

    id: Mapped[UUID] = mapped_column(primary_key=True, default=uuid4)
    source: Mapped[str] = mapped_column(String(100))
    source_id: Mapped[str] = mapped_column(String(255))
    name: Mapped[str] = mapped_column(String(255))
    denomination: Mapped[str | None] = mapped_column(String(100), index=True)
    description: Mapped[str | None]
    address_line1: Mapped[str | None]
    city: Mapped[str | None] = mapped_column(String(100), index=True)
    region: Mapped[str | None] = mapped_column(String(100))
    postal_code: Mapped[str | None] = mapped_column(String(20))
    country_code: Mapped[str | None] = mapped_column(String(2))
    latitude: Mapped[float | None] = mapped_column(Double)
    longitude: Mapped[float | None] = mapped_column(Double)
    timezone: Mapped[str | None] = mapped_column(String(100))
    website_url: Mapped[str | None]
    phone: Mapped[str | None] = mapped_column(String(50))
    email: Mapped[str | None] = mapped_column(String(254))
    source_url: Mapped[str | None]
    last_verified_at: Mapped[datetime | None] = mapped_column(UTCDateTime)
    created_at: Mapped[datetime] = mapped_column(
        UTCDateTime, default=utc_now, server_default=func.now()
    )
    updated_at: Mapped[datetime] = mapped_column(
        UTCDateTime, default=utc_now, onupdate=utc_now, server_default=func.now()
    )
    service_times: Mapped[list["ServiceTime"]] = relationship(
        back_populates="church",
        cascade="all, delete-orphan",
        passive_deletes=True,
        order_by="(ServiceTime.day_of_week, ServiceTime.start_time, ServiceTime.id)",
    )


class ServiceTime(Base):
    __tablename__ = "service_times"
    __table_args__ = (
        CheckConstraint("day_of_week BETWEEN 0 AND 6", name="ck_service_times_day_of_week"),
    )

    id: Mapped[UUID] = mapped_column(primary_key=True, default=uuid4)
    church_id: Mapped[UUID] = mapped_column(
        ForeignKey("churches.id", ondelete="CASCADE"), index=True
    )
    day_of_week: Mapped[int]
    start_time: Mapped[time]
    language: Mapped[str | None] = mapped_column(String(35))
    label: Mapped[str | None] = mapped_column(String(100))
    notes: Mapped[str | None]
    church: Mapped[Church] = relationship(back_populates="service_times")
