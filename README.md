# GatewayForge

An API gateway (YARP) in front of two services, secured with Keycloak. It shows two ways to trust a downstream service:

| Route | Pattern | The service... |
|---|---|---|
| `/api/secure/*` | Token relay | validates the JWT and role itself |
| `/api/internal/*` | Edge offload | trusts `X-User-Id`, but only with the gateway's `X-Gateway-Key` |

```
Client ──JWT──▶ Gateway ──▶ Secure service
                   │    └──▶ Internal service
                   └──▶ Keycloak ──▶ Postgres
```

## Run

```bash
cp .env.example .env      # fill in the values
docker-compose up -d --build
```

- Gateway: <http://localhost:5100>
- Keycloak: <http://localhost:8080>

```bash
TOKEN=$(curl -s -d client_id=gateway-client -d username=testuser -d password=123456 \
  -d grant_type=password http://localhost:8080/realms/gateway-forge/protocol/openid-connect/token \
  | python3 -c 'import sys,json;print(json.load(sys.stdin)["access_token"])')

curl -H "Authorization: Bearer $TOKEN" http://localhost:5100/api/secure/secure-data
curl -H "Authorization: Bearer $TOKEN" http://localhost:5100/api/internal/internal-data
```

`testuser` / `123456` is a dev-only user seeded from `keycloak/import/gateway-forge-realm.json`.

## What it enforces

- JWT signature, issuer, audience and expiry are validated.
- Realm roles `secure-user` and `internal-user` gate each route.
- The gateway strips any client-supplied identity headers and sets its own.
- Services and the database sit on internal-only Docker networks.
- Per-user rate limiting and correlation ids on every request.

## Test

```bash
dotnet test GatewayForge.Tests
```
