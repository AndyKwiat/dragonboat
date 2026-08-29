/** Marker layer: labelled circles placed on the map. A marker is a point
 *  (map px, same coordinate space as the tile/height arrays), a radius
 *  (map px) and a mandatory short tag. Markers are not rasterized; they
 *  are a plain list kept alongside the two pixel layers. */

export interface Marker {
  x: number;   // map px, center
  y: number;   // map px, center
  r: number;   // map px, radius > 0
  tag: string; // 1..MAX_TAG_LENGTH chars (trimmed), no newlines
}

export const DEFAULT_MARKER_RADIUS = 10;
export const MAX_TAG_LENGTH = 100;
export const MAX_MARKERS = 1_000_000;

/** Trim + validate a tag. Returns null if it is unusable (empty / too long). */
export function normalizeTag(raw: string): string | null {
  const tag = raw.replace(/\s+/g, " ").trim(); // collapse whitespace: tags are one short line
  if (tag.length === 0 || tag.length > MAX_TAG_LENGTH) return null;
  return tag;
}

/** Build a marker, throwing on invalid input. */
export function makeMarker(x: number, y: number, r: number, rawTag: string): Marker {
  const tag = normalizeTag(rawTag);
  if (tag === null) throw new Error(`invalid marker tag (1..${MAX_TAG_LENGTH} chars)`);
  if (!(r > 0) || !Number.isFinite(r)) throw new Error(`invalid marker radius ${r}`);
  if (!Number.isFinite(x) || !Number.isFinite(y)) throw new Error("invalid marker position");
  return { x, y, r, tag };
}

/** Index of the marker whose disc contains (x, y), topmost (last placed)
 *  first; `minHit` inflates tiny discs so they stay clickable when zoomed
 *  out. -1 if none. */
export function hitMarker(markers: readonly Marker[], x: number, y: number, minHit = 0): number {
  for (let i = markers.length - 1; i >= 0; i--) {
    const m = markers[i];
    const rr = Math.max(m.r, minHit);
    const dx = x - m.x, dy = y - m.y;
    if (dx * dx + dy * dy <= rr * rr) return i;
  }
  return -1;
}

/** Deep copy (markers are plain objects; this makes history snapshots safe). */
export function cloneMarkers(markers: readonly Marker[]): Marker[] {
  return markers.map((m) => ({ ...m }));
}

/** Scale marker positions/radii when the map resolution changes. */
export function resampleMarkers(markers: readonly Marker[], from: number, to: number): Marker[] {
  const k = to / from;
  return markers.map((m) => ({ ...m, x: m.x * k, y: m.y * k, r: m.r * k }));
}
