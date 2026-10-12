# Load test results

Edge offload (`/api/internal`, gateway validates once) compared with re-authentication (`/api/secure`, gateway and service both validate).
Both routes pass through the same gateway authentication, role check and rate limiter.

## Environment

- One MacBook, 14 cores, 24 GB. Load generator, Docker VM, gateway, services and Keycloak all share it.
- Stack started with `docker-compose.benchmark.yml` (rate limit raised, request logging off) unless noted.
- Absolute req/s are specific to this machine. The ratios are what generalise.

## 1. Does the gain scale with total request volume?

128 concurrent connections, fixed number of requests per path, two passes each, 0 errors.

| Requests per path | Secure req/s | Internal req/s | Offload gain | p50 secure / internal | p99 secure / internal |
|---|---|---|---|---|---|
| 100,000 | 28,334 – 30,907 | 35,614 – 35,834 | 1.16x – 1.26x (avg 1.21x) | 4.21 / 3.32 – 3.97 / 3.36 ms | 8.19 – 10.38 / 7.43 – 7.83 ms |
| 500,000 | 29,247 – 30,725 | 35,958 – 36,990 | 1.20x – 1.23x (avg 1.22x) | 3.98 / 3.27 – 4.08 / 3.34 ms | 8.43 – 10.48 / 7.18 – 7.79 ms |
| 1,000,000 | 28,025 – 29,355 | 34,993 – 36,081 | 1.23x – 1.25x (avg 1.24x) | 3.99 / 3.33 – 4.28 / 3.43 ms | 10.30 – 11.12 / 7.53 – 7.94 ms |

Averages of the two passes:

| Requests per path | Time secure | Time internal | Time saved | Median latency saved | p99 latency saved |
|---|---|---|---|---|---|
| 100,000 | 3.4 s | 2.8 s | 0.6 s | 0.75 ms | 1.7 ms |
| 500,000 | 16.7 s | 13.7 s | 3.0 s | 0.73 ms | 2.0 ms |
| 1,000,000 | 34.9 s | 28.1 s | 6.7 s | 0.76 ms | 3.0 ms |

Reading: the throughput gain is roughly constant (about 1.2x) regardless of volume. Time saved grows linearly because the per-request saving is constant.
The p99 saving widens with sustained volume (1.7 → 2.0 → 3.0 ms): the re-authenticating path's tail drifts up over longer runs while the offload path's stays flat. With two passes this is suggestive, not conclusive.

## 2. Does the gain scale with concurrency?

Fixed 8 seconds per path at each level, 0 errors.

| Connections | Secure req/s | Internal req/s | Offload gain | Secure p99 | Internal p99 |
|---|---|---|---|---|---|
| 8 | 10,828 | 11,550 | 1.07x | 1.4 ms | 1.4 ms |
| 32 | 21,102 | 24,521 | 1.16x | 3.2 ms | 3.0 ms |
| 128 | 32,110 | 38,279 | 1.19x | 7.9 ms | 6.8 ms |
| 512 | 35,198 | 43,304 | 1.23x | 31.9 ms | 24.7 ms |
| 2048 | 34,131 | 46,884 | 1.37x | 114.3 ms | 83.7 ms |

Reading: the gain grows with concurrency. At low concurrency latency is dominated by the round trip. Once the machine is CPU-bound the re-authenticating path plateaus (about 35k req/s) while edge offload keeps climbing.

## 3. Single user, interleaved rounds

64 connections, 6 interleaved rounds of 10 s per path, two runs.

| Run | Secure | Internal | Gain |
|---|---|---|---|
| A | 26,267 req/s, p50 2.30 ms | 30,585 req/s, p50 1.95 ms | 1.16x |
| B | 25,876 req/s, p50 2.33 ms | 30,398 req/s, p50 1.96 ms | 1.17x |

## 4. 100 distinct users

Each user has their own token. 15 s per path.

| Run | Secure | Internal | Gain |
|---|---|---|---|
| 1 | 25,286 req/s, p50 3.79 ms | 30,060 req/s, p50 3.18 ms | 1.19x |
| 2 | 24,727 req/s, p50 3.86 ms | 29,898 req/s, p50 3.18 ms | 1.21x |

Per-user request counts varied by about 2% (no user starved).

## 5. Realistic crowd with one abusive user

50 paced users (about 1 request per 1.2 s, 50/50 across the two routes) and 1 user with no pauses, default 60/min per-user limit.

| Route (normal users) | Requests | 2xx | 429 | p50 | p95 |
|---|---|---|---|---|---|
| Internal (edge offload) | 639 | 639 | 0 | 0.86 ms | 1.56 ms |
| Secure (re-authenticates) | 636 | 636 | 0 | 1.01 ms | 2.00 ms |

The abusive user sent 82,032 requests: 58 allowed, 81,974 rejected with 429 (99.93%). No normal user was throttled.

## 6. Flood at one user

64 connections, 10 s, default limit: 365,358 requests, 60 allowed (200), 365,298 rejected (429) at about 1.6 ms each.

## Summary

| Question | Answer |
|---|---|
| How much faster is edge offload? | About 1.2x throughput, 0.4 – 0.8 ms less median latency per request |
| Does the gain grow with total volume? | No, it stays about 1.2x. Absolute time saved grows linearly. |
| Does the gain grow with concurrency? | Yes, 1.07x at 8 connections up to 1.37x at 2048 |
| Does the tail benefit more than the median? | Yes, the p99 saving is larger and widens under sustained load |
