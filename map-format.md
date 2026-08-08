# Dragon Boat Map Format (.dmap)

A simple binary format for exporting the 2D island map as terrain indices.

## File Layout

| Offset | Size    | Type       | Description              |
|--------|---------|------------|--------------------------|
| 0      | 4 bytes | ASCII      | Magic bytes: `DMAP`      |
| 4      | 4 bytes | uint32 LE  | Width in pixels           |
| 8      | 4 bytes | uint32 LE  | Height in pixels          |
| 12     | W * H   | uint8[]    | Terrain data, one byte per pixel |

**Total file size:** 12 + (width * height) bytes. For a 1000x1000 map this is 1,000,012 bytes (~977 KB).

## Terrain Data

Pixels are stored row-major, top-to-bottom, left-to-right. Each byte is a terrain index:

| Index | Terrain    | Visual Color    | Description                                    |
|-------|------------|-----------------|------------------------------------------------|
| 0     | Ocean      | `#1a6dd4` blue  | Open water surrounding the island              |
| 1     | Beach      | `#dec882` sand  | Narrow sandy border around the island coastline |
| 2     | Grassland  | `#4a9e4a` green | Open land, more common near the coast          |
| 3     | Forest     | `#266626` dark green | Dense forest, more common near mountains  |
| 4     | Mountain   | `#888888` grey  | Rocky mountain region in the island center     |
| 5     | River      | `#1a6dd4` blue  | Rivers, tributaries, and lakes                 |
| 6     | Canal      | `#135ba1` dark blue | Straight man-made waterways connecting rivers |

## Reading Example (JavaScript)

```js
const buf = await fetch('map.dmap').then(r => r.arrayBuffer());
const view = new DataView(buf);

// Verify magic
const magic = String.fromCharCode(
  view.getUint8(0), view.getUint8(1), view.getUint8(2), view.getUint8(3)
);
console.assert(magic === 'DMAP');

const width = view.getUint32(4, true);
const height = view.getUint32(8, true);
const terrain = new Uint8Array(buf, 12);

// Access pixel at (x, y):
function getTerrain(x, y) {
  return terrain[y * width + x];
}
```

## Reading Example (Python)

```python
import struct

with open('map.dmap', 'rb') as f:
    magic = f.read(4)
    assert magic == b'DMAP'
    width, height = struct.unpack('<II', f.read(8))
    terrain = list(f.read(width * height))

# Access pixel at (x, y):
def get_terrain(x, y):
    return terrain[y * width + x]
```

## Notes

- Forest vs grassland distribution is probabilistic: pixels closer to the mountain center are more likely to be forest. The exact distribution may vary between exports.
- Rivers and lakes share the same index (5) since they represent the same type of natural water body.
- Canals (6) are distinguished from rivers because they are man-made and always perfectly straight.
