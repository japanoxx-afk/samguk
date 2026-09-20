# Distributed map

`samhan-glory.skm` is the exact user-provided `(N4) 삼한의 영광.skm`.
The user requested uploading this map and downloading it through the launcher.

- Installed filename: `(N4) 삼한의 영광.skm`
- Destination: selected original game executable directory / `Mission`
- Size: 223917 bytes; P32M, 128×128
- SHA256: `a5e1a9e6ea5e7ec0a4de0647a05c79eb78c68981883da8d28c6f90f08ec1c272`

The launcher validates this hash and never overwrites a different same-name map.
Game executables, editor binaries and other game assets are not included here.

## Adding maps for v1.9.1+ launchers

Upload each approved `.skm` into this directory and add a tab-separated row to
`catalog.tsv`: source filename, installed filename, lowercase SHA256, byte size.
Publish file and catalog together. The launcher fetches the catalog on every
startup; no new launcher release is needed for additional maps. Files not listed
in the catalog are not downloaded. Keep source filenames simple, and use a new
installed filename for revised maps: different existing files are never replaced.
