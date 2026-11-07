# HEIC decoder fixture

`color-blocks.heic` is a synthetic 96 × 64 HEVC-encoded image with four colour
blocks, authored for DriveAlt with `generate.py` using Pillow and pillow-heif.
It contains no student material or third-party photograph.

The production Dockerfile converts it to JPEG as the unprivileged app user.
The build fails if HEIC's decoder is missing, even when `heif-convert` itself
is installed. The fixture is mounted only during the check and is not shipped.
GitHub PR tests build the same `converters` stage used by production.

Ubuntu Noble splits HEIC decoding into `libheif-plugin-libde265`, which
`--no-install-recommends` does not install with `libheif-examples`:
https://packages.ubuntu.com/noble/libheif-plugin-libde265

Regeneration is optional: install Pillow and pillow-heif in a temporary Python
environment, then run `python tests/fixtures/heic/generate.py`. Exact bytes can
vary with encoder versions; the build checks decoding rather than an encoder hash.
