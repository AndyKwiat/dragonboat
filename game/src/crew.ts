// Crew maths. The crew is just a head count, but it drives four things:
// rations eaten, wages paid (both in src/upkeep.ts), the boat's top speed and
// how much cargo the hold can take.
//
// More hands means a faster boat, up to a point: speedAtMin at the minimum
// crew rising straight to speedAtMax at speedFullCrew, flat above that — extra
// hands past there are for the cargo they carry, not the pace they set.
//
// Dependency-free (limits are passed in, from CONFIG.crew) so
// scripts/test-time.js can run this module directly.

export interface CrewLimits {
  min: number;
  max: number;
  speedAtMin: number;
  speedAtMax: number;
  /** Crew size at which speed tops out; bigger crews sail no faster. */
  speedFullCrew: number;
  cargoPerCrew: number;
}

export function clampCrew(count: number, limits: CrewLimits): number {
  return Math.min(Math.max(Math.round(count), limits.min), limits.max);
}

/** Total hold space, in units, for a given crew size. */
export function cargoCapacity(crew: number, limits: CrewLimits): number {
  return clampCrew(crew, limits) * limits.cargoPerCrew;
}

/**
 * Whether the crew may change by `delta` while carrying `cargoUnits`. False
 * at the limits (nothing would change) and false when putting hands ashore
 * would leave less hold space than the cargo already aboard.
 */
export function crewChangeAllowed(
  crew: number,
  delta: number,
  cargoUnits: number,
  limits: CrewLimits,
): boolean {
  const next = clampCrew(crew + delta, limits);
  return next !== clampCrew(crew, limits) && cargoCapacity(next, limits) >= cargoUnits;
}

/** Multiplier on the boat's top speed for a given crew size. */
export function crewSpeedMultiplier(crew: number, limits: CrewLimits): number {
  const span = Math.max(limits.speedFullCrew, limits.min) - limits.min;
  const over = clampCrew(crew, limits) - limits.min;
  const t = span <= 0 ? 1 : Math.min(over / span, 1); // flat past speedFullCrew
  return limits.speedAtMin + (limits.speedAtMax - limits.speedAtMin) * t;
}
