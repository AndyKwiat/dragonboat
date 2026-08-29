# `.terrain` file format

Authoritative spec for the binary map files written by the terrain editor.
If you are implementing a reader or writer in another codebase (game engine,
tool, exporter), this file is the contract — the reference implementation is
`src/core/fileFormat.ts`.

- **Byte order:** little-endian, everywhere.
- **Container:** the payload described below is usually **gzipped** on disk.
  Sniff the gzip magic (`0x1f 0x8b`) on the first two bytes and inflate if
  present; both raw and gzipped files must be accepted. Writers may emit
  either (the editor gzips).
- **Extension:** `.terrain`.
- **Versioning:** the 4-byte magic *is* the version. `TGN1` = version 1,
  `TGN2` = version 2 (current). A reader must switch on it and reject
  anything else.

## Versions

| magic | version | contents |
|---|---|---|
| `TGN1` | 1 | header + heights + tiles |
| `TGN2` | 2 | version 1, plus a marker section appended at the end |

Version 2 is a strict superset: bytes `0 … 16 + 5·size² − 1` are byte-for-byte
identical in meaning to version 1. Upgrading a reader means (a) accepting the
new magic and (b) parsing the appended marker section; a version-1 file loads
as a version-2 document with zero markers.

## Layout

```
offset                      type            field
0                           char[4]         magic: "TGN1" or "TGN2"
4                           u32             size — pixels per side, power of two (1 … 65536)
8                           u32             flags — reserved, must be 0
12                          f32             worldSize — world units the map spans
16                          f32[size*size]  heights, row-major, world units
16 + 4*size*size            u8[size*size]   tile ids, row-major
--- version 2 only, immediately after the tiles ---
16 + 5*size*size            u32             markerCount
then, markerCount times (variable length):
  +0                        f32             x — center, map pixels
  +4                        f32             y — center, map pixels
  +8                        f32             r — radius, map pixels (> 0)
  +12                       u16             tagLen — length of the tag in BYTES
  +14                       u8[tagLen]      tag — UTF-8, no BOM, not NUL-terminated
```

Version 1 files end exactly at `16 + 5·size²`. Version 2 files end exactly
after the last marker's tag; trailing bytes are an error in both versions.

### Fields

- **`size`** — the map is always square and a power of two. Both pixel arrays
  are `size²` entries, row-major (`index = y * size + x`), origin top-left.
- **`worldSize`** — how many world units the whole map spans on each axis, so
  one map pixel is `worldSize / size` world units. The editor writes 1024.
- **`heights`** — world units, sea level = 0. The editor clamps painted
  heights to `[1, 100]`, but a reader should not assume that range.
- **`tiles`** — one byte per pixel:

  | id | tile |
  |---|---|
  | 0 | water |
  | 1 | sand |
  | 2 | grass |
  | 3 | forest |
  | 4 | rock |

  Unknown ids should be treated as water rather than rejected.

### Markers (version 2)

A marker is a labelled circle on the map: a center, a radius, and a mandatory
tag. Markers are *not* rasterized into the pixel layers; they are an
independent list, stored in placement order (later entries paint on top and
win hit-tests first).

- `x`, `y`, `r` are in **map pixels**, the same coordinate space as the pixel
  arrays, so world coordinates are `x * worldSize / size` etc. Resolution
  changes in the editor scale all three.
- `r` must be greater than 0. The editor's default for a new marker is `10`.
- `tag` is required and non-empty: 1 … 100 **characters** after trimming, with
  whitespace runs collapsed to single spaces. `tagLen` counts **UTF-8 bytes**,
  so it can exceed 100 for non-ASCII tags (a 100-character emoji tag is up to
  400 bytes). Readers should validate the character count, not the byte count.
- Tags are arbitrary text, not identifiers: they are not unique, not sorted,
  and carry no schema. Treat them as untrusted display strings.
- The editor caps the list at 1,000,000 markers.

## Reader checklist

1. Inflate if the bytes start with `1f 8b`.
2. Read the 4-byte magic → version. Reject anything but `TGN1` / `TGN2`.
3. Validate `size`: power of two, 1 … 65536.
4. For `TGN1`, require exactly `16 + 5·size²` bytes; for `TGN2`, require at
   least `16 + 5·size² + 4`.
5. Read heights and tiles.
6. `TGN2`: read `markerCount`, then that many markers, bounds-checking every
   read against the buffer length; reject invalid UTF-8, `r <= 0` and any
   trailing bytes.

## Writer checklist

- Always write the current magic (`TGN2`); there is no reason to emit `TGN1`.
- Write `flags = 0`.
- Validate tags before writing (non-empty, ≤ 100 characters).
- Gzip unless you specifically need a raw file — an 8192² map is 320 MB raw
  and compresses hugely, since most maps are flat/repetitive.

## Adding a version 3

Keep the pattern: bump the magic to `TGN3`, append the new section *after*
the existing ones, and leave every earlier byte's meaning untouched. Readers
then only need `version >= N` checks per section, and old files keep loading
without conversion.
