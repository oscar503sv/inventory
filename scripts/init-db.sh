#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
if [[ -f .env ]]; then source .env; fi
: "${PGDATABASE:?Configure PGDATABASE en .env o el entorno}"
: "${PGUSER:?Configure PGUSER en .env o el entorno}"
psql --dbname=postgres --set=ON_ERROR_STOP=1 --set=db="$PGDATABASE" --set=owner="$PGUSER" <<'SQL'
SELECT format('CREATE DATABASE %I OWNER %I', :'db', :'owner')
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = :'db')
\gexec
SQL
dotnet run --project src/Inventory.API --no-launch-profile -- --migrate-only
