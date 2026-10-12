# Load test results

Edge offload (`/api/internal`, the gateway validates the JWT once) compared with re-authentication (`/api/secure`, the gateway and the service both validate).
Both routes pass through the same gateway authentication, role check and rate limiter, and both services do the same trivial work.

## Headline

| Question | Answer |
|---|---|
| How much faster is edge offload? | 1.3x to 1.65x throughput from 8 to 8,192 connections, with a lower p50 and p99 at every level up to 16,384 (p99 4% to 42% lower) |
| Does the gain grow with concurrency? | Yes: 1.33x at 8 connections, 1.65x at 2,048, then noise once both paths are overloaded |
| Does it grow with total request volume? | No, it stays flat (see the earlier runs below) |
| How many concurrent connections with 0 errors? | 16,384 in every run, 32,768 in 3 of 3 runs, 50,000 in 1 of 3 runs |
| What stops it? | Memory of the 7.7 GB Docker VM: the gateway was OOM-killed at 60,000 connections |

## Pushing the limits: 8 to 60,000 concurrent connections

### Method

- The load generator runs **inside Docker** on the same network as the gateway (`docker-compose.benchmark.yml`, service `loadtest`), so there is no Docker Desktop port forwarding and no macOS 16,384-port limit.
- Every connection is busy (closed loop): `N` concurrent connections each send a request as soon as the previous one completes. 10 s measured per path per level, with a ramp-up that grows with the level.
- The rate limiter is raised and request logging is off for the run. Server GC is enabled in the generator.
- One MacBook, 14 cores, 24 GB, Docker VM 7.7 GB. The generator, gateway, both services and Keycloak all share it.

### Results (mean of two passes with identical settings)

| Connections | Secure req/s | Internal req/s | Gain | p50 secure / internal | p99 secure / internal | p99 faster | Errors (pass A, pass B) |
|---|---|---|---|---|---|---|---|
| 8 | 11,455 | 15,188 | 1.33x | 0.6 / 0.5 ms | 2 / 1 ms | 25% | 0, 0 |
| 32 | 19,212 | 26,798 | 1.39x | 1.5 / 1.1 ms | 5 / 4 ms | 17% | 0, 0 |
| 128 | 22,421 | 30,954 | 1.38x | 4.8 / 3.5 ms | 22 / 21 ms | 4% | 0, 0 |
| 512 | 22,544 | 33,298 | 1.48x | 19.8 / 12.9 ms | 60 / 46 ms | 24% | 0, 0 |
| 2,048 | 20,498 | 33,844 | 1.65x | 97.7 / 57.8 ms | 206 / 120 ms | 42% | 0, 0 |
| 8,192 | 18,092 | 29,074 | 1.61x | 425 / 274 ms | 753 / 474 ms | 37% | 0, 0 |
| 16,384 | 14,558 | 21,562 | 1.48x | 983 / 731 ms | 1,857 / 1,378 ms | 26% | 0, 0 |
| 32,768 | 6,306 | 6,575 | not meaningful | 4,151 / 4,964 ms | 6,133 / 7,217 ms | n/a | 0, 0 |
| 50,000 | 1,774 | 4,152 | not meaningful | 17,013 / 9,874 ms | 18,737 / 12,063 ms | n/a | 0, 25 |

A third, earlier pass used the generator's default (workstation) GC and agrees: gains of 1.29x, 1.32x, 1.33x, 1.19x, 1.35x, 1.58x and 1.17x from 8 to 16,384 connections, 0 errors up to 32,768 and 23 errors at 50,000.

### How many connections the gateway really held

Counted from inside the gateway container (`/proc/net/tcp`, state ESTABLISHED on port 8080), sampled every 5 to 6 seconds:

| Pass | Peak established connections |
|---|---|
| Workstation GC pass | 36,262 |
| Server GC pass A (reached the 60,000 ramp) | 56,100 |
| Server GC pass B | 45,507 |

### The limit, and why

| Level | Outcome |
|---|---|
| Up to 16,384 | 0 errors in every run |
| 32,768 | 0 errors in 3 of 3 runs, but throughput has collapsed to about 6,000 req/s and p50 is 4 to 5 s |
| 50,000 | 0 errors in 1 of 3 runs; otherwise 23 to 25 `Connection reset by peer` out of hundreds of thousands of requests |
| 60,000 | Gateway container OOM-killed (exit 137): 48,931 resets and 686 refused connections seen by the client |

What bounds it, from the sampled container metrics and logs:

- **Memory.** The gateway grew to about 3.1 GB and the generator to about 1.3 to 1.9 GB inside a 7.7 GB VM. The kernel OOM killer took the gateway at 60,000.
- **Ephemeral ports towards the backends.** At overload the gateway logs `HttpRequestException: Cannot assign requested address` from YARP while forwarding.
- **The load generator itself.** At 32,768 and above it used 5 to 13 cores (GC and scheduling of tens of thousands of busy tasks) while the gateway was at 6 to 58% CPU at times. Throughput at those levels is partly a client limit.
- **Queueing, not slowness.** With every connection busy, Little's law gives latency of about connections divided by throughput: 50,000 / 4,152 req/s is about 12 s. Past roughly 16,384 connections the service is technically up with zero errors but not usable at that concurrency.

### Reading the gain

- "p99 faster" is the reduction in p99 latency versus re-authenticating: (secure - internal) / secure. It is smallest at 128 connections (4%, both paths are CPU-bound and nearly equal) and largest at 2,048 (42%).

- From inside the network the gain is 1.3x to 1.65x. Through Docker Desktop's port forward (earlier runs, below) it is about 1.2x, because the forwarder adds the same fixed cost to both paths and dilutes the ratio.
- The gain rises with concurrency up to about 2,048 connections because the re-authenticating path saturates its CPU first.
- At 32,768 and above both paths are overloaded, so the ratio is noise and is not reported.

### Things that did not work and were discarded

- A paced variant (each connection pausing 10 s between requests) did not hold the connections: the shared connection pool reuses a few connections for many idle workers, so the gateway only ever held about 7,250. Its results are not reported.
- Per-level numbers vary run to run (for example 16,384 connections gave gains of 1.18x and 1.78x in two passes), which is why the table shows means and the errors are listed per pass.

## Earlier runs: load generator on the host, through Docker Desktop's port forward

These were measured before the generator moved into Docker. Absolute numbers are lower and the ratios about 1.2x for the reason above.

### 1. Does the gain scale with total request volume?

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

### 2. Does the gain scale with concurrency?

Fixed 8 seconds per path at each level, 0 errors.

| Connections | Secure req/s | Internal req/s | Offload gain | Secure p99 | Internal p99 |
|---|---|---|---|---|---|
| 8 | 10,828 | 11,550 | 1.07x | 1.4 ms | 1.4 ms |
| 32 | 21,102 | 24,521 | 1.16x | 3.2 ms | 3.0 ms |
| 128 | 32,110 | 38,279 | 1.19x | 7.9 ms | 6.8 ms |
| 512 | 35,198 | 43,304 | 1.23x | 31.9 ms | 24.7 ms |
| 2048 | 34,131 | 46,884 | 1.37x | 114.3 ms | 83.7 ms |

Reading: the gain grows with concurrency. At low concurrency latency is dominated by the round trip. Once the machine is CPU-bound the re-authenticating path plateaus (about 35k req/s) while edge offload keeps climbing.

### 3. Single user, interleaved rounds

64 connections, 6 interleaved rounds of 10 s per path, two runs.

| Run | Secure | Internal | Gain |
|---|---|---|---|
| A | 26,267 req/s, p50 2.30 ms | 30,585 req/s, p50 1.95 ms | 1.16x |
| B | 25,876 req/s, p50 2.33 ms | 30,398 req/s, p50 1.96 ms | 1.17x |

### 4. 100 distinct users

Each user has their own token. 15 s per path.

| Run | Secure | Internal | Gain |
|---|---|---|---|
| 1 | 25,286 req/s, p50 3.79 ms | 30,060 req/s, p50 3.18 ms | 1.19x |
| 2 | 24,727 req/s, p50 3.86 ms | 29,898 req/s, p50 3.18 ms | 1.21x |

Per-user request counts varied by about 2% (no user starved).

### 5. Realistic crowd with one abusive user

50 paced users (about 1 request per 1.2 s, 50/50 across the two routes) and 1 user with no pauses, default 60/min per-user limit.

| Route (normal users) | Requests | 2xx | 429 | p50 | p95 |
|---|---|---|---|---|---|
| Internal (edge offload) | 639 | 639 | 0 | 0.86 ms | 1.56 ms |
| Secure (re-authenticates) | 636 | 636 | 0 | 1.01 ms | 2.00 ms |

The abusive user sent 82,032 requests: 58 allowed, 81,974 rejected with 429 (99.93%). No normal user was throttled.

### 6. Flood at one user

64 connections, 10 s, default limit: 365,358 requests, 60 allowed (200), 365,298 rejected (429) at about 1.6 ms each.

### Summary of the earlier runs

| Question | Answer |
|---|---|
| How much faster is edge offload? | About 1.2x throughput, 0.4 – 0.8 ms less median latency per request |
| Does the gain grow with total volume? | No, it stays about 1.2x. Absolute time saved grows linearly. |
| Does the gain grow with concurrency? | Yes, 1.07x at 8 connections up to 1.37x at 2048 |
| Does the tail benefit more than the median? | Yes, the p99 saving is larger and widens under sustained load |
