#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
if [[ -f .env ]]; then source .env; fi
: "${TEST_POSTGRES_CONNECTION:?Configure TEST_POSTGRES_CONNECTION; el rol necesita CREATEDB}"
dotnet restore InventoryErp.slnx
dotnet build InventoryErp.slnx --no-restore --configuration Release
dotnet test InventoryErp.slnx --no-build --configuration Release --logger trx --results-directory TestResults
