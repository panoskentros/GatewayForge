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

## Load simulation

`GatewayForge.LoadTest` measures how much faster edge offload (`/api/internal`) is than re-authenticating at the service (`/api/secure`). Both go through the same gateway authentication.

```bash
docker-compose -f docker-compose.yml -f docker-compose.benchmark.yml up -d --build
dotnet run -c Release --project GatewayForge.LoadTest -- --mode compare --rounds 6 --duration 10
dotnet run -c Release --project GatewayForge.LoadTest -- --mode users --users 100
dotnet run -c Release --project GatewayForge.LoadTest -- --mode volume --requests 500000 --concurrency 128
dotnet run -c Release --project GatewayForge.LoadTest -- --mode sweep --levels 8,32,128,512,2048 --duration 8

docker-compose up -d
dotnet run -c Release --project GatewayForge.LoadTest -- --mode noisy --users 50 --duration 30
dotnet run -c Release --project GatewayForge.LoadTest -- --mode flood --duration 10
```

`users` and `noisy` create throwaway Keycloak users (`loadtest-*`, using the admin credentials in `.env`) and delete them afterwards.

Options: `--concurrency 64 --users 50 --rounds 1 --requests 500000 --levels 16,64,256,1024 --duration 15 --warmup 3 --think MS --gateway URL --keycloak URL --user NAME --password PASS --admin-user NAME --admin-password PASS`.
