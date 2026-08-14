// Crew maths. The crew is just a head count, but it drives three things:
// rations eaten, wages paid (both in src/upkeep.ts) and the boat's top speed.
//
// More hands means a faster boat: speedAtMin at the minimum crew, speedAtMax
// at the maximum, straight-line in between.
//
// Dependency-free (limits are passed in, from CONFIG.crew) so
// scripts/test-time.js can run this module directly.

export interface CrewLimits {
  min: number;
  max: number;
  speedAtMin: number;
  speedAtMax: number;
}

export function clampCrew(count: number, limits: CrewLimits): number {
  return Math.min(Math.max(Math.round(count), limits.min), limits.max);
}

/** Multiplier on the boat's top speed for a given crew size. */
export function crewSpeedMultiplier(crew: number, limits: CrewLimits): number {
  const span = limits.max - limits.min;
  const t = span <= 0 ? 0 : (clampCrew(crew, limits) - limits.min) / span;
  return limits.speedAtMin + (limits.speedAtMax - limits.speedAtMin) * t;
}
