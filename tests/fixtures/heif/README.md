# External HEIC fixture

Issue #312 validates the advertised HEIC/HEIF contract with an externally produced fixture instead of a runtime encoder round-trip.

## libheif-example.heic

- Upstream repository: https://github.com/strukturag/libheif
- Upstream commit: `5c7b41f3cc097447dd3c700cc9ec7d94fbb59eec`
- Upstream path: `examples/example.heic`
- Upstream Git blob SHA-1: `829384037820e545467a4af49aa6414c2b0f2885`
- Size: 718,114 bytes
- License: MIT for the libheif examples directory (copied as `libheif-examples.COPYING`)
- Purpose: external HEVC/HEIF decode regression fixture.

The pinned NetVips Windows "web" runtime exposes `heifload` but is built without libde265 HEVC decoding. #312 therefore uses this fixture to prove the unsupported HEIC state and prevents Library Core from advertising `.heic` / `.heif` until a separately reviewed decoder/runtime is adopted.
