//! Terrain rendering: chunked voxel surface meshes with per-vertex colors,
//! plus cheap static decorations (trees, rocks, houses).

use bevy::asset::RenderAssetUsages;
use bevy::mesh::{Indices, PrimitiveTopology};
use bevy::prelude::*;

use crate::config::*;
use crate::world::{splitmix64, TerrainType, WorldGrid};

pub struct TerrainPlugin;

impl Plugin for TerrainPlugin {
    fn build(&self, app: &mut App) {
        app.add_systems(Startup, (spawn_terrain, spawn_decorations));
    }
}

// ---------------------------------------------------------------------------
// Colors
// ---------------------------------------------------------------------------

/// Restrained per-cell color. `variant` (0..=255) nudges brightness so
/// neighboring voxels read as distinct cells; height adds a subtle gradient.
fn cell_color(terrain_type: TerrainType, height: u8, variant: u8) -> [f32; 4] {
    let v = variant as f32 / 255.0;
    let h = height as f32 / TERRAIN_MAX_HEIGHT as f32;
    let (r, g, b) = match terrain_type {
        TerrainType::Water => (0.16, 0.32, 0.55),
        TerrainType::Sand => (0.76, 0.70, 0.50),
        TerrainType::Grass => (0.26, 0.46, 0.21),
        TerrainType::DryGrass => (0.50, 0.48, 0.24),
        TerrainType::Earth => (0.45, 0.36, 0.24),
        TerrainType::Rock => (0.52, 0.52, 0.50),
    };
    // ±8% brightness per voxel + up to +12% toward higher ground.
    let shade = 0.92 + v * 0.16 + h * 0.12;
    [r * shade, g * shade, b * shade, 1.0]
}

/// Side faces are slightly darkened for cheap edge definition.
fn darken(c: [f32; 4], f: f32) -> [f32; 4] {
    [c[0] * f, c[1] * f, c[2] * f, 1.0]
}

// ---------------------------------------------------------------------------
// Chunk meshing
// ---------------------------------------------------------------------------

struct MeshData {
    positions: Vec<[f32; 3]>,
    normals: Vec<[f32; 3]>,
    colors: Vec<[f32; 4]>,
    indices: Vec<u32>,
}

impl MeshData {
    fn new() -> Self {
        Self {
            positions: Vec::new(),
            normals: Vec::new(),
            colors: Vec::new(),
            indices: Vec::new(),
        }
    }

    fn quad(&mut self, verts: [[f32; 3]; 4], normal: [f32; 3], color: [f32; 4]) {
        let base = self.positions.len() as u32;
        self.positions.extend_from_slice(&verts);
        self.normals.extend_from_slice(&[normal; 4]);
        self.colors.extend_from_slice(&[color; 4]);
        self.indices
            .extend_from_slice(&[base, base + 1, base + 2, base, base + 2, base + 3]);
    }

    fn into_mesh(self) -> Mesh {
        Mesh::new(
            PrimitiveTopology::TriangleList,
            RenderAssetUsages::RENDER_WORLD,
        )
        .with_inserted_attribute(Mesh::ATTRIBUTE_POSITION, self.positions)
        .with_inserted_attribute(Mesh::ATTRIBUTE_NORMAL, self.normals)
        .with_inserted_attribute(Mesh::ATTRIBUTE_COLOR, self.colors)
        .with_inserted_indices(Indices::U32(self.indices))
    }
}

/// Neighbor height for side-face culling; out-of-bounds counts as height 0 so
/// the world edge gets solid walls.
fn neighbor_height(world: &WorldGrid, x: i64, z: i64) -> u8 {
    if x < 0 || z < 0 || x >= WORLD_WIDTH as i64 || z >= WORLD_DEPTH as i64 {
        0
    } else {
        world.cell(x as usize, z as usize).height
    }
}

/// Build one chunk mesh: a top quad per cell + side quads where the neighbor
/// is lower. No bottom faces, no internal faces between equal heights.
fn build_chunk_mesh(world: &WorldGrid, cx: usize, cz: usize) -> Mesh {
    let mut m = MeshData::new();
    let x0 = cx * TERRAIN_CHUNK_SIZE;
    let z0 = cz * TERRAIN_CHUNK_SIZE;

    for z in z0..(z0 + TERRAIN_CHUNK_SIZE).min(WORLD_DEPTH) {
        for x in x0..(x0 + TERRAIN_CHUNK_SIZE).min(WORLD_WIDTH) {
            let cell = world.cell(x, z);
            let h = cell.height as f32;
            let (fx, fz) = (x as f32, z as f32);
            let top = cell_color(cell.terrain_type, cell.height, cell.color_variant);
            let side = darken(top, 0.72);

            // Top face at y = h (counter-clockwise seen from above).
            m.quad(
                [
                    [fx, h, fz],
                    [fx, h, fz + 1.0],
                    [fx + 1.0, h, fz + 1.0],
                    [fx + 1.0, h, fz],
                ],
                [0.0, 1.0, 0.0],
                top,
            );

            // Side faces down to any lower neighbor.
            let sides: [(i64, i64, [f32; 3]); 4] = [
                (-1, 0, [-1.0, 0.0, 0.0]),
                (1, 0, [1.0, 0.0, 0.0]),
                (0, -1, [0.0, 0.0, -1.0]),
                (0, 1, [0.0, 0.0, 1.0]),
            ];
            for (dx, dz, normal) in sides {
                let nh = neighbor_height(world, x as i64 + dx, z as i64 + dz) as f32;
                if nh >= h {
                    continue;
                }
                let (lo, hi) = (nh, h);
                let verts = match (dx, dz) {
                    (-1, 0) => [
                        [fx, hi, fz],
                        [fx, lo, fz],
                        [fx, lo, fz + 1.0],
                        [fx, hi, fz + 1.0],
                    ],
                    (1, 0) => [
                        [fx + 1.0, hi, fz + 1.0],
                        [fx + 1.0, lo, fz + 1.0],
                        [fx + 1.0, lo, fz],
                        [fx + 1.0, hi, fz],
                    ],
                    (0, -1) => [
                        [fx + 1.0, hi, fz],
                        [fx + 1.0, lo, fz],
                        [fx, lo, fz],
                        [fx, hi, fz],
                    ],
                    _ => [
                        [fx, hi, fz + 1.0],
                        [fx, lo, fz + 1.0],
                        [fx + 1.0, lo, fz + 1.0],
                        [fx + 1.0, hi, fz + 1.0],
                    ],
                };
                m.quad(verts, normal, side);
            }
        }
    }
    m.into_mesh()
}

fn spawn_terrain(
    mut commands: Commands,
    world: Res<WorldGrid>,
    mut meshes: ResMut<Assets<Mesh>>,
    mut materials: ResMut<Assets<StandardMaterial>>,
) {
    // One shared material; all color comes from vertex colors.
    let material = materials.add(StandardMaterial {
        base_color: Color::WHITE,
        perceptual_roughness: 1.0,
        reflectance: 0.0,
        ..default()
    });

    let chunks_x = WORLD_WIDTH.div_ceil(TERRAIN_CHUNK_SIZE);
    let chunks_z = WORLD_DEPTH.div_ceil(TERRAIN_CHUNK_SIZE);
    for cz in 0..chunks_z {
        for cx in 0..chunks_x {
            let mesh = build_chunk_mesh(&world, cx, cz);
            commands.spawn((
                Mesh3d(meshes.add(mesh)),
                MeshMaterial3d(material.clone()),
                Transform::IDENTITY,
            ));
        }
    }
}

// ---------------------------------------------------------------------------
// Static decorations: trees, rocks, houses (shared meshes + tiny palette).
// ---------------------------------------------------------------------------

fn spawn_decorations(
    mut commands: Commands,
    world: Res<WorldGrid>,
    mut meshes: ResMut<Assets<Mesh>>,
    mut materials: ResMut<Assets<StandardMaterial>>,
) {
    let trunk_mesh = meshes.add(Cuboid::new(0.35, 1.0, 0.35));
    let canopy_mesh = meshes.add(Cuboid::new(1.8, 1.6, 1.8));
    let rock_mesh = meshes.add(Cuboid::new(1.0, 0.7, 1.2));
    let house_mesh = meshes.add(Cuboid::new(3.0, 2.5, 4.0));
    let roof_mesh = meshes.add(Cuboid::new(3.4, 0.8, 4.4));

    let trunk_mat = materials.add(StandardMaterial {
        base_color: Color::srgb(0.35, 0.24, 0.14),
        perceptual_roughness: 1.0,
        ..default()
    });
    let canopy_mats = [
        materials.add(StandardMaterial {
            base_color: Color::srgb(0.16, 0.38, 0.16),
            perceptual_roughness: 1.0,
            ..default()
        }),
        materials.add(StandardMaterial {
            base_color: Color::srgb(0.22, 0.45, 0.18),
            perceptual_roughness: 1.0,
            ..default()
        }),
        materials.add(StandardMaterial {
            base_color: Color::srgb(0.30, 0.48, 0.14),
            perceptual_roughness: 1.0,
            ..default()
        }),
    ];
    let rock_mat = materials.add(StandardMaterial {
        base_color: Color::srgb(0.45, 0.45, 0.44),
        perceptual_roughness: 1.0,
        ..default()
    });
    let wall_mat = materials.add(StandardMaterial {
        base_color: Color::srgb(0.75, 0.68, 0.55),
        perceptual_roughness: 1.0,
        ..default()
    });
    let roof_mat = materials.add(StandardMaterial {
        base_color: Color::srgb(0.60, 0.25, 0.18),
        perceptual_roughness: 1.0,
        ..default()
    });

    let mut rng = WORLD_SEED ^ 0xDEC0_0001;

    // Trees: clustered on grass via rejection sampling.
    let mut placed = 0;
    let mut attempts = 0;
    while placed < TREE_COUNT && attempts < TREE_COUNT * 30 {
        attempts += 1;
        let x = (splitmix64(&mut rng) % WORLD_WIDTH as u64) as f32 + 0.5;
        let z = (splitmix64(&mut rng) % WORLD_DEPTH as u64) as f32 + 0.5;
        let cell = world.cell(x as usize, z as usize);
        if cell.terrain_type != TerrainType::Grass {
            continue;
        }
        // Cluster: keep only where a coarse deterministic roll passes.
        if (cell.color_variant as u32 * 7 + (x as u32 / 16) * 13 + (z as u32 / 16) * 29) % 5 > 1 {
            continue;
        }
        let y = cell.height as f32;
        let canopy = canopy_mats[(splitmix64(&mut rng) % 3) as usize].clone();
        commands
            .spawn((Transform::from_xyz(x, y, z), Visibility::default()))
            .with_children(|p| {
                p.spawn((
                    Mesh3d(trunk_mesh.clone()),
                    MeshMaterial3d(trunk_mat.clone()),
                    Transform::from_xyz(0.0, 0.5, 0.0),
                ));
                p.spawn((
                    Mesh3d(canopy_mesh.clone()),
                    MeshMaterial3d(canopy),
                    Transform::from_xyz(0.0, 1.8, 0.0),
                ));
            });
        placed += 1;
    }

    // Rocks: on rock/earth/dry terrain.
    let mut placed = 0;
    let mut attempts = 0;
    while placed < ROCK_COUNT && attempts < ROCK_COUNT * 30 {
        attempts += 1;
        let x = (splitmix64(&mut rng) % WORLD_WIDTH as u64) as f32 + 0.5;
        let z = (splitmix64(&mut rng) % WORLD_DEPTH as u64) as f32 + 0.5;
        let cell = world.cell(x as usize, z as usize);
        if !matches!(
            cell.terrain_type,
            TerrainType::Rock | TerrainType::Earth | TerrainType::DryGrass
        ) {
            continue;
        }
        commands.spawn((
            Mesh3d(rock_mesh.clone()),
            MeshMaterial3d(rock_mat.clone()),
            Transform::from_xyz(x, cell.height as f32 + 0.35, z),
        ));
        placed += 1;
    }

    // Houses: flat grass only.
    let mut placed = 0;
    let mut attempts = 0;
    while placed < HOUSE_COUNT && attempts < HOUSE_COUNT * 60 {
        attempts += 1;
        let x = (splitmix64(&mut rng) % (WORLD_WIDTH as u64 - 8)) as f32 + 4.0;
        let z = (splitmix64(&mut rng) % (WORLD_DEPTH as u64 - 8)) as f32 + 4.0;
        let cell = world.cell(x as usize, z as usize);
        if cell.terrain_type != TerrainType::Grass {
            continue;
        }
        let y = cell.height as f32;
        commands
            .spawn((Transform::from_xyz(x, y, z), Visibility::default()))
            .with_children(|p| {
                p.spawn((
                    Mesh3d(house_mesh.clone()),
                    MeshMaterial3d(wall_mat.clone()),
                    Transform::from_xyz(0.0, 1.25, 0.0),
                ));
                p.spawn((
                    Mesh3d(roof_mesh.clone()),
                    MeshMaterial3d(roof_mat.clone()),
                    Transform::from_xyz(0.0, 2.9, 0.0),
                ));
            });
        placed += 1;
    }
}
