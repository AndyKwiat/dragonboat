//! Wandering "people": tiny two-block figures that roam the terrain,
//! follow ground height, avoid water, and stay inside the world.

use bevy::asset::RenderAssetUsages;
use bevy::camera::visibility::NoFrustumCulling;
use bevy::mesh::{Indices, PrimitiveTopology};
use bevy::prelude::*;

use crate::config::*;
use crate::world::{splitmix64, WorldGrid};

pub struct MoversPlugin;

impl Plugin for MoversPlugin {
    fn build(&self, app: &mut App) {
        app.add_systems(Startup, spawn_movers)
            .add_systems(Update, move_movers);
    }
}

#[derive(Component)]
pub struct Mover {
    heading: f32,
    speed: f32,
    /// Seconds until the next random heading change.
    retarget_in: f32,
    /// Per-entity deterministic RNG state (no allocation per frame).
    rng: u64,
}

const BODY_HEIGHT: f32 = 0.9;

fn rand01(rng: &mut u64) -> f32 {
    (splitmix64(rng) >> 40) as f32 / (1u64 << 24) as f32
}

// Linear-RGB equivalent of sRGB (0.87, 0.72, 0.58).
const SKIN_COLOR: [f32; 3] = [0.7305, 0.4735, 0.2960];

/// Append an axis-aligned box (24 verts, per-face normals, one color) to the
/// mesh buffers.
#[allow(clippy::too_many_arguments)]
fn push_box(
    positions: &mut Vec<[f32; 3]>,
    normals: &mut Vec<[f32; 3]>,
    colors: &mut Vec<[f32; 4]>,
    indices: &mut Vec<u32>,
    center: Vec3,
    half: Vec3,
    color: [f32; 3],
) {
    let (c, h) = (center, half);
    // (normal, four corners CCW seen from outside)
    let faces: [([f32; 3], [Vec3; 4]); 6] = [
        (
            [0.0, 1.0, 0.0],
            [
                Vec3::new(-h.x, h.y, -h.z),
                Vec3::new(-h.x, h.y, h.z),
                Vec3::new(h.x, h.y, h.z),
                Vec3::new(h.x, h.y, -h.z),
            ],
        ),
        (
            [0.0, -1.0, 0.0],
            [
                Vec3::new(-h.x, -h.y, h.z),
                Vec3::new(-h.x, -h.y, -h.z),
                Vec3::new(h.x, -h.y, -h.z),
                Vec3::new(h.x, -h.y, h.z),
            ],
        ),
        (
            [1.0, 0.0, 0.0],
            [
                Vec3::new(h.x, h.y, h.z),
                Vec3::new(h.x, -h.y, h.z),
                Vec3::new(h.x, -h.y, -h.z),
                Vec3::new(h.x, h.y, -h.z),
            ],
        ),
        (
            [-1.0, 0.0, 0.0],
            [
                Vec3::new(-h.x, h.y, -h.z),
                Vec3::new(-h.x, -h.y, -h.z),
                Vec3::new(-h.x, -h.y, h.z),
                Vec3::new(-h.x, h.y, h.z),
            ],
        ),
        (
            [0.0, 0.0, 1.0],
            [
                Vec3::new(-h.x, h.y, h.z),
                Vec3::new(-h.x, -h.y, h.z),
                Vec3::new(h.x, -h.y, h.z),
                Vec3::new(h.x, h.y, h.z),
            ],
        ),
        (
            [0.0, 0.0, -1.0],
            [
                Vec3::new(h.x, h.y, -h.z),
                Vec3::new(h.x, -h.y, -h.z),
                Vec3::new(-h.x, -h.y, -h.z),
                Vec3::new(-h.x, h.y, -h.z),
            ],
        ),
    ];
    for (normal, corners) in faces {
        let base = positions.len() as u32;
        for corner in corners {
            positions.push((c + corner).to_array());
        }
        for _ in 0..4 {
            normals.push(normal);
            colors.push([color[0], color[1], color[2], 1.0]);
        }
        indices.extend_from_slice(&[base, base + 1, base + 2, base, base + 2, base + 3]);
    }
}

/// Body prism + head cube merged into one vertex-colored mesh, origin at the
/// feet (same overall shape as the old two-entity person).
fn person_mesh(body_color: [f32; 3]) -> Mesh {
    let mut positions = Vec::new();
    let mut normals = Vec::new();
    let mut colors = Vec::new();
    let mut indices = Vec::new();
    push_box(
        &mut positions,
        &mut normals,
        &mut colors,
        &mut indices,
        Vec3::new(0.0, BODY_HEIGHT * 0.5, 0.0),
        Vec3::new(0.25, BODY_HEIGHT * 0.5, 0.175),
        body_color,
    );
    push_box(
        &mut positions,
        &mut normals,
        &mut colors,
        &mut indices,
        Vec3::new(0.0, BODY_HEIGHT + 0.2, 0.0),
        Vec3::new(0.2, 0.2, 0.2),
        SKIN_COLOR,
    );
    Mesh::new(
        PrimitiveTopology::TriangleList,
        RenderAssetUsages::RENDER_WORLD,
    )
    .with_inserted_attribute(Mesh::ATTRIBUTE_POSITION, positions)
    .with_inserted_attribute(Mesh::ATTRIBUTE_NORMAL, normals)
    .with_inserted_attribute(Mesh::ATTRIBUTE_COLOR, colors)
    .with_inserted_indices(Indices::U32(indices))
}

fn spawn_movers(
    mut commands: Commands,
    world: Res<WorldGrid>,
    mut meshes: ResMut<Assets<Mesh>>,
    mut materials: ResMut<Assets<StandardMaterial>>,
) {
    // One merged body+head mesh per palette color (vertex-colored), all
    // sharing a single white material. Same mesh + same material means Bevy's
    // renderer draws each variant as ONE instanced batch — 5 draw calls for
    // every mover on screen instead of per-entity work times 45,000.
    let palette = [
        [0.85, 0.25, 0.20],
        [0.20, 0.45, 0.85],
        [0.90, 0.75, 0.20],
        [0.85, 0.85, 0.88],
        [0.30, 0.25, 0.35],
    ];
    // Vertex colors are linear RGB; convert the sRGB palette values.
    let variant_meshes: Vec<_> = palette
        .into_iter()
        .map(|[r, g, b]| {
            let c = Color::srgb(r, g, b).to_linear();
            meshes.add(person_mesh([c.red, c.green, c.blue]))
        })
        .collect();
    let shared_mat = materials.add(StandardMaterial {
        base_color: Color::WHITE,
        perceptual_roughness: 0.9,
        ..default()
    });

    let mut rng = WORLD_SEED ^ 0x0DDB_A115;
    let mut spawned = 0;
    while spawned < MOVER_COUNT {
        let x = rand01(&mut rng) * (WORLD_WIDTH as f32 - 2.0) + 1.0;
        let z = rand01(&mut rng) * (WORLD_DEPTH as f32 - 2.0) + 1.0;
        if world.is_water(x, z) {
            continue;
        }
        let y = world.height_at(x, z);
        let heading = rand01(&mut rng) * std::f32::consts::TAU;
        let speed = MOVER_MIN_SPEED + rand01(&mut rng) * (MOVER_MAX_SPEED - MOVER_MIN_SPEED);
        let entity_rng = splitmix64(&mut rng);

        commands.spawn((
            Mover {
                heading,
                speed,
                retarget_in: 1.0 + rand01(&mut rng) * 4.0,
                rng: entity_rng,
            },
            Mesh3d(variant_meshes[spawned % variant_meshes.len()].clone()),
            MeshMaterial3d(shared_mat.clone()),
            Transform::from_xyz(x, y, z).with_rotation(Quat::from_rotation_y(-heading)),
            // Movers are tiny and everywhere; skipping per-entity culling is
            // cheaper than testing 15k bounding boxes each frame.
            NoFrustumCulling,
        ));
        spawned += 1;
    }
}

fn move_movers(
    time: Res<Time>,
    world: Res<WorldGrid>,
    mut movers: Query<(&mut Transform, &mut Mover)>,
) {
    let dt = time.delta_secs();
    let margin = 2.0;
    let max_x = WORLD_WIDTH as f32 - margin;
    let max_z = WORLD_DEPTH as f32 - margin;

    for (mut transform, mut mover) in &mut movers {
        // Occasionally pick a new heading.
        mover.retarget_in -= dt;
        if mover.retarget_in <= 0.0 {
            let mut rng = mover.rng;
            mover.heading += (rand01(&mut rng) - 0.5) * std::f32::consts::PI;
            mover.retarget_in = 1.5 + rand01(&mut rng) * 4.0;
            mover.rng = rng;
        }

        let step = Vec2::new(mover.heading.cos(), mover.heading.sin()) * mover.speed * dt;
        let mut next_x = transform.translation.x + step.x;
        let mut next_z = transform.translation.z + step.y;

        // Bounce off world edges.
        let mut bounced = false;
        if next_x < margin || next_x > max_x {
            mover.heading = std::f32::consts::PI - mover.heading;
            bounced = true;
        }
        if next_z < margin || next_z > max_z {
            mover.heading = -mover.heading;
            bounced = true;
        }
        // Turn away from water instead of walking in.
        if !bounced && world.is_water(next_x, next_z) {
            mover.heading += std::f32::consts::PI;
            bounced = true;
        }
        if bounced {
            next_x = transform.translation.x;
            next_z = transform.translation.z;
        }

        transform.translation.x = next_x;
        transform.translation.z = next_z;

        // Follow terrain height (smoothed a little so steps aren't jumpy).
        let target_y = world.height_at(next_x, next_z);
        transform.translation.y += (target_y - transform.translation.y) * (dt * 12.0).min(1.0);

        // Face travel direction.
        transform.rotation = Quat::from_rotation_y(-mover.heading);
    }
}
