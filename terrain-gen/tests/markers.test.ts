import { describe, expect, test } from "bun:test";
import { DEFAULT_MARKER_RADIUS, MAX_TAG_LENGTH, cloneMarkers, hitMarker, makeMarker, normalizeTag, resampleMarkers } from "../src/core/markers";
import type { Marker } from "../src/core/markers";
import { History, markerEntry } from "../src/core/history";

describe("markers", () => {
  test("defaults and tag normalization", () => {
    expect(DEFAULT_MARKER_RADIUS).toBe(10);
    expect(MAX_TAG_LENGTH).toBe(100);
    expect(normalizeTag("  hello\n world ")).toBe("hello world");
    expect(normalizeTag("   ")).toBeNull();
    expect(normalizeTag("a".repeat(101))).toBeNull();
    expect(normalizeTag("a".repeat(100))).toHaveLength(100);
    expect(() => makeMarker(1, 1, 0, "x")).toThrow(/radius/);
    expect(() => makeMarker(1, 1, 5, "")).toThrow(/tag/);
    expect(makeMarker(1, 2, 5, " t ")).toEqual({ x: 1, y: 2, r: 5, tag: "t" });
  });
  test("hit test: topmost first, minHit inflates tiny markers", () => {
    const ms: Marker[] = [{ x: 10, y: 10, r: 5, tag: "a" }, { x: 12, y: 10, r: 5, tag: "b" }, { x: 50, y: 50, r: 0.5, tag: "c" }];
    expect(hitMarker(ms, 11, 10)).toBe(1);
    expect(hitMarker(ms, 6, 10)).toBe(0);
    expect(hitMarker(ms, 30, 30)).toBe(-1);
    expect(hitMarker(ms, 53, 50)).toBe(-1);
    expect(hitMarker(ms, 53, 50, 4)).toBe(2);
  });
  test("resample scales position and radius", () => {
    const out = resampleMarkers([{ x: 100, y: 50, r: 10, tag: "a" }], 1024, 2048);
    expect(out).toEqual([{ x: 200, y: 100, r: 20, tag: "a" }]);
  });
  test("history: marker entries undo/redo in place", () => {
    const markers: Marker[] = [];
    const hist = new History();
    const before = cloneMarkers(markers);
    markers.push({ x: 1, y: 1, r: 10, tag: "one" });
    hist.push(markerEntry(before, markers)!);
    const before2 = cloneMarkers(markers);
    markers[0].tag = "uno";
    hist.push(markerEntry(before2, markers)!);
    expect(markerEntry(markers, markers)).toBeNull();
    const target = { tiles: new Uint8Array(64 * 64), height: new Float32Array(64 * 64), markers };
    expect(hist.undo(target, 64)!.layer).toBe("markers");
    expect(markers[0].tag).toBe("one");
    hist.undo(target, 64);
    expect(markers).toEqual([]);
    hist.redo(target, 64); hist.redo(target, 64);
    expect(markers).toEqual([{ x: 1, y: 1, r: 10, tag: "uno" }]);
  });
});
