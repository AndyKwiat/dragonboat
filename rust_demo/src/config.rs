//! Central tuning parameters for the demo.
//!
//! Another agent should be able to reshape the demo from this file alone.
//! See docs/TUNING.md for what each knob does.

/// World size in voxels (X and Z). One voxel == 1.0 Bevy world unit.
pub const WORLD_WIDTH: usize = 256;
pub const WORLD_DEPTH: usize = 256;

/// Deterministic seed for terrain generation and mover placement.
/// Change it to get a different map.
pub const WORLD_SEED: u64 = 12345;

/// Approximate number of ground voxels the camera should frame vertically.
/// Smaller = camera appears closer, larger = farther away.
pub const TARGET_VISIBLE_VOXELS: f32 = 200.0;

/// Terrain height range, in voxels (0..=TERRAIN_MAX_HEIGHT).
pub const TERRAIN_MAX_HEIGHT: u32 = 8;

/// Cells at or below this height become water.
pub const WATER_LEVEL: u32 = 1;

/// Chunk size for terrain meshing. 256/16 = 16x16 = 256 chunks.
pub const TERRAIN_CHUNK_SIZE: usize = 16;

/// Number of wandering entities. Try 100 / 500 / 1000 / 2000 / 5000.
pub const MOVER_COUNT: usize = 15000;
pub const MOVER_MIN_SPEED: f32 = 2.0;
pub const MOVER_MAX_SPEED: f32 = 6.0;

/// Static decoration counts (trees, rocks, houses).
pub const TREE_COUNT: usize = 220;
pub const ROCK_COUNT: usize = 60;
pub const HOUSE_COUNT: usize = 180;

/// Fixed isometric-style camera orientation.
pub const CAMERA_AZIMUTH_DEGREES: f32 = 45.0;
pub const CAMERA_ELEVATION_DEGREES: f32 = 38.0;
