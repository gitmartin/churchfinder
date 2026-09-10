"""Create churches and recurring weekly service times."""

import sqlalchemy as sa
from alembic import op

revision = "0001"
down_revision = None
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.create_table(
        "churches",
        sa.Column("id", sa.Uuid(), nullable=False),
        sa.Column("source", sa.String(100), nullable=False),
        sa.Column("source_id", sa.String(255), nullable=False),
        sa.Column("name", sa.String(255), nullable=False),
        sa.Column("denomination", sa.String(100)),
        sa.Column("description", sa.String()),
        sa.Column("address_line1", sa.String()),
        sa.Column("city", sa.String(100)),
        sa.Column("region", sa.String(100)),
        sa.Column("postal_code", sa.String(20)),
        sa.Column("country_code", sa.String(2)),
        sa.Column("latitude", sa.Double()),
        sa.Column("longitude", sa.Double()),
        sa.Column("timezone", sa.String(100)),
        sa.Column("website_url", sa.String()),
        sa.Column("phone", sa.String(50)),
        sa.Column("email", sa.String(254)),
        sa.Column("source_url", sa.String()),
        sa.Column("last_verified_at", sa.DateTime(timezone=True)),
        sa.Column(
            "created_at", sa.DateTime(timezone=True), server_default=sa.func.now(), nullable=False
        ),
        sa.Column(
            "updated_at", sa.DateTime(timezone=True), server_default=sa.func.now(), nullable=False
        ),
        sa.PrimaryKeyConstraint("id"),
        sa.UniqueConstraint("source", "source_id", name="uq_churches_source_source_id"),
        sa.CheckConstraint("length(trim(name)) > 0", name="ck_churches_name"),
        sa.CheckConstraint("length(trim(source)) > 0", name="ck_churches_source"),
        sa.CheckConstraint("length(trim(source_id)) > 0", name="ck_churches_source_id"),
        sa.CheckConstraint(
            "(latitude IS NULL AND longitude IS NULL) OR "
            "(latitude IS NOT NULL AND longitude IS NOT NULL)",
            name="ck_churches_coordinate_pair",
        ),
        sa.CheckConstraint("latitude BETWEEN -90 AND 90", name="ck_churches_latitude"),
        sa.CheckConstraint("longitude BETWEEN -180 AND 180", name="ck_churches_longitude"),
    )
    op.create_index("ix_churches_city", "churches", ["city"])
    op.create_index("ix_churches_denomination", "churches", ["denomination"])
    op.create_table(
        "service_times",
        sa.Column("id", sa.Uuid(), nullable=False),
        sa.Column("church_id", sa.Uuid(), nullable=False),
        sa.Column("day_of_week", sa.Integer(), nullable=False),
        sa.Column("start_time", sa.Time(), nullable=False),
        sa.Column("language", sa.String(35)),
        sa.Column("label", sa.String(100)),
        sa.Column("notes", sa.String()),
        sa.PrimaryKeyConstraint("id"),
        sa.ForeignKeyConstraint(["church_id"], ["churches.id"], ondelete="CASCADE"),
        sa.CheckConstraint("day_of_week BETWEEN 0 AND 6", name="ck_service_times_day_of_week"),
    )
    op.create_index("ix_service_times_church_id", "service_times", ["church_id"])


def downgrade() -> None:
    op.drop_table("service_times")
    op.drop_table("churches")
