# External HEIC fixture

This directory contains a pinned external HEIC fixture used by Lumine v2 Issue #312.

## dsoprea-image4.heic

- Upstream repository: https://github.com/dsoprea/heic-exif-samples
- Upstream commit: `6bf59bf9c822473817dfb897c45acdcdb53ed9a0`
- Upstream path: `image4.heic`
- Upstream Git blob SHA-1: `efd119a0ea5f9c59d225e2f1ba7269bfe1802d0b`
- Size: 41,465 bytes
- License: MIT (copied as `dsoprea-heic-exif-samples.LICENSE.txt`)
- Purpose: prove HEVC/HEIF decode from an externally produced fixture without depending on Lumine/libvips HEVC encoding support.

The upstream README describes these files as HEIC samples containing EXIF metadata. The original photographic source attribution was not retained by the upstream author; this fixture is therefore used only as a decoder regression fixture under the upstream repository's MIT distribution terms.
