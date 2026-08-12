//! World grid: deterministic terrain data + height lookup used by terrain
//! meshing, mover terrain-following, and decoration placement.

use bevy::prelude::*;

use crate::config::*;

#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum TerrainType {
    Water,
    Sand,
    Grass,
    DryGrass,
    Earth,
    Rock,
}

#[derive(Clone, Copy)]
pub struct TerrainCell {
    /// Ground height in voxels. Water cells store the *water surface* height.
    pub height: u8,
    pub terrain_type: TerrainType,
    /// Small deterministic per-cell value (0..=255) used for color variation.
    pub color_variant: u8,
}

/// Flat 2D grid of terrain cells, indexed `z * WORLD_WIDTH + x`.
#[derive(Resource)]
pub struct WorldGrid {
    pub cells: Vec<TerrainCell>,
}

impl WorldGrid {
    pub fn cell(&self, x: usize, z: usize) -> TerrainCell {
        self.cells[z * WORLD_WIDTH + x]
    }

    /// Ground height at a world position, clamped to bounds.
    pub fn height_at(&self, x: f32, z: f32) -> f32 {
        let xi = (x.floor().max(0.0) as usize).min(WORLD_WIDTH - 1);
        let zi = (z.floor().max(0.0) as usize).min(WORLD_DEPTH - 1);
        self.cell(xi, zi).height as f32
    }

    pub fn is_water(&self, x: f32, z: f32) -> bool {
        let xi = (x.floor().max(0.0) as usize).min(WORLD_WIDTH - 1);
        let zi = (z.floor().max(0.0) as usize).min(WORLD_DEPTH - 1);
        self.cell(xi, zi).terrain_type == TerrainType::Water
    }
}

pub struct WorldPlugin;

impl Plugin for WorldPlugin {
    fn build(&self, app: &mut App) {
        app.insert_resource(generate_world(WORLD_SEED));
    }
}

// ---------------------------------------------------------------------------
// Deterministic generation: SplitMix64 hashing + 2D value noise.
// ---------------------------------------------------------------------------

/// SplitMix64 — tiny deterministic hash/PRNG step.
pub fn splitmix64(state: &mut u64) -> u64 {
    *state = state.wrapping_add(0x9E37_79B9_7F4A_7C15);
    let mut z = *state;
    z = (z ^ (z >> 30)).wrapping_mul(0xBF58_476D_1CE4_E5B9);
    z = (z ^ (z >> 27)).wrapping_mul(0x94D0_49BB_1331_11EB);
    z ^ (z >> 31)
}

/// Deterministic hash of integer grid coordinates -> [0, 1).
fn hash01(seed: u64, x: i64, z: i64) -> f32 {
    let mut s = seed
        ^ (x as u64).wrapping_mul(0x517C_C1B7_2722_0A95)
        ^ (z as u64).wrapping_mul(0x2545_F491_4F6C_DD1D);
    (splitmix64(&mut s) >> 40) as f32 / (1u64 << 24) as f32
}

fn smoothstep(t: f32) -> f32 {
    t * t * (3.0 - 2.0 * t)
}

/// Bilinear value noise at arbitrary scale, output [0, 1).
fn value_noise(seed: u64, x: f32, z: f32) -> f32 {
    let x0 = x.floor();
    let z0 = z.floor();
    let tx = smoothstep(x - x0);
    let tz = smoothstep(z - z0);
    let (x0, z0) = (x0 as i64, z0 as i64);
    let a = hash01(seed, x0, z0);
    let b = hash01(seed, x0 + 1, z0);
    let c = hash01(seed, x0, z0 + 1);
    let d = hash01(seed, x0 + 1, z0 + 1);
    let top = a + (b - a) * tx;
    let bottom = c + (d - c) * tx;
    top + (bottom - top) * tz
}

/// Layered value noise, output roughly [0, 1).
fn fbm(seed: u64, x: f32, z: f32) -> f32 {
    let mut total = 0.0;
    let mut amplitude = 1.0;
    let mut frequency = 1.0;
    let mut norm = 0.0;
    for octave in 0..3u64 {
        total += value_noise(seed ^ (octave * 0x9E37), x * frequency, z * frequency) * amplitude;
        norm += amplitude;
        amplitude *= 0.5;
        frequency *= 2.0;
    }
    total / norm
}

pub fn generate_world(seed: u64) -> WorldGrid {
    let mut cells = Vec::with_capacity(WORLD_WIDTH * WORLD_DEPTH);
    for z in 0..WORLD_DEPTH {
        for x in 0..WORLD_WIDTH {
            let fx = x as f32;
            let fz = z as f32;

            // Gentle rolling terrain: large-scale noise + a couple of broad hills.
            let base = fbm(seed, fx * 0.02, fz * 0.02);
            let hills = fbm(seed ^ 0xABCD, fx * 0.008, fz * 0.008);
            let mut h = base * 3.5 + hills * 6.0 - 1.2;

            // Carve a shallow lake basin toward one side of the map.
            let lake = ((fx - 70.0) * (fx - 70.0) + (fz - 170.0) * (fz - 170.0)).sqrt();
            if lake < 42.0 {
                h -= (1.0 - lake / 42.0) * 4.0;
            }

            let height = (h.round().max(0.0) as u32).min(TERRAIN_MAX_HEIGHT);
            let variant_roll = hash01(seed ^ 0xC0FFEE, x as i64, z as i64);
            let moisture = fbm(seed ^ 0x5EED, fx * 0.015, fz * 0.015);

            let (terrain_type, height) = if height <= WATER_LEVEL {
                // Water cells render a flat surface at the water level.
                (TerrainType::Water, WATER_LEVEL)
            } else if height == WATER_LEVEL + 1 {
                (TerrainType::Sand, height)
            } else if height >= TERRAIN_MAX_HEIGHT - 1 {
                (TerrainType::Rock, height)
            } else if moisture < 0.35 {
                (TerrainType::DryGrass, height)
            } else if variant_roll > 0.93 {
                (TerrainType::Earth, height)
            } else {
                (TerrainType::Grass, height)
            };

            cells.push(TerrainCell {
                height: height as u8,
                terrain_type,
                color_variant: (variant_roll * 255.0) as u8,
            });
        }
    }
    WorldGrid { cells }
}
