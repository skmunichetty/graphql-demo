<#
.SYNOPSIS
  Gets a DEVELOPMENT access token from the local Keycloak. LOCAL DEVELOPMENT ONLY.

.DESCRIPTION
  Uses the OAuth 2.0 password grant on the "construction-dev-cli" client, which is enabled only in
  the local demo realm so tokens can be obtained from a terminal. Real clients use the authorization
  code flow with PKCE (users) or client credentials (services).

.EXAMPLE
  $token = ./scripts/get-token.ps1 -User alice -Scopes 'estimates:read estimates:write'
#>
param(
    [string]$User = 'alice',
    [string]$Scopes = 'estimates:read estimates:write accounting:export accounting:read',
    [string]$Keycloak = 'http://localhost:8180'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$password = (Get-Content (Join-Path $root '.env') | Where-Object { $_ -match '^DEMO_USER_PASSWORD=' }) -replace '^DEMO_USER_PASSWORD=', ''
if (-not $password) {
    throw 'DEMO_USER_PASSWORD not found. Run ./scripts/init-dev-env.ps1 first.'
}

$response = Invoke-RestMethod -Method Post -Uri "$Keycloak/realms/construction/protocol/openid-connect/token" -Body @{
    grant_type = 'password'
    client_id  = 'construction-dev-cli'
    username   = $User
    password   = $password
    scope      = "openid $Scopes"
}
return $response.access_token
