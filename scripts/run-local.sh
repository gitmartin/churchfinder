#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

# Keep the local demo separate from the database configured in .env.
# Export DATABASE_URL before running this script to use a different database.
export DATABASE_URL="${DATABASE_URL:-sqlite:///./churchfinder-map.db}"
export EMBED_ALLOWED_ORIGINS="${EMBED_ALLOWED_ORIGINS:-[\"http://localhost:5242\",\"http://127.0.0.1:5242\"]}"
uv sync --locked
npm ci --prefix frontend --no-audit --no-fund
npm run build --prefix frontend
uv run alembic upgrade head
uv run churchfinder-import data/seed-toronto-10.json --format research

backend_pid=""
frontend_pid=""
cleanup() {
  if [[ -n "$frontend_pid" ]]; then kill "$frontend_pid" 2>/dev/null || true; fi
  if [[ -n "$backend_pid" ]]; then kill "$backend_pid" 2>/dev/null || true; fi
}
trap cleanup EXIT
trap 'exit 0' INT TERM
.venv/bin/uvicorn churchfinder.main:create_app --factory --host 127.0.0.1 --port 8000 &
backend_pid=$!
node frontend/node_modules/vite/bin/vite.js frontend --host 127.0.0.1 --port 5173 --strictPort &
frontend_pid=$!
while kill -0 "$backend_pid" 2>/dev/null && kill -0 "$frontend_pid" 2>/dev/null; do sleep 1; done
exit 1
