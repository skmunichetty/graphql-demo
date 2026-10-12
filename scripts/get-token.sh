#!/usr/bin/env bash
# Gets a DEVELOPMENT access token from the local Keycloak. LOCAL DEVELOPMENT ONLY.
# Uses the password grant on the demo-only "construction-dev-cli" client. Real clients use the
# authorization code flow with PKCE (users) or client credentials (services).
#
# Usage: TOKEN=$(./scripts/get-token.sh alice "estimates:read estimates:write")
set -euo pipefail

user="${1:-alice}"
scopes="${2:-estimates:read estimates:write accounting:export accounting:read}"
keycloak="${KEYCLOAK_URL:-http://localhost:8180}"
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
password="$(grep '^DEMO_USER_PASSWORD=' "$root/.env" | cut -d= -f2-)"

curl --silent --fail-with-body \
  -d grant_type=password \
  -d client_id=construction-dev-cli \
  -d "username=$user" \
  --data-urlencode "password=$password" \
  --data-urlencode "scope=openid $scopes" \
  "$keycloak/realms/construction/protocol/openid-connect/token" \
  | sed -E 's/.*"access_token":"([^"]+)".*/\1/'
