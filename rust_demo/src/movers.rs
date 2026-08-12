//! Wandering "people": tiny two-block figures that roam the terrain,
//! follow ground height, avoid water, and stay inside the world.

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

fn spawn_movers(
    mut commands: Commands,
    world: Res<WorldGrid>,
    mut meshes: ResMut<Assets<Mesh>>,
    mut materials: ResMut<Assets<StandardMaterial>>,
) {
    // Shared meshes: rectangular body + cube head.
    let body_mesh = meshes.add(Cuboid::new(0.5, BODY_HEIGHT, 0.35));
    let head_mesh = meshes.add(Cuboid::new(0.4, 0.4, 0.4));

    // Small reusable palette of "clothing" colors.
    let palette = [
        Color::srgb(0.85, 0.25, 0.20),
        Color::srgb(0.20, 0.45, 0.85),
        Color::srgb(0.90, 0.75, 0.20),
        Color::srgb(0.85, 0.85, 0.88),
        Color::srgb(0.30, 0.25, 0.35),
    ];
    let body_mats: Vec<_> = palette
        .into_iter()
        .map(|c| {
            materials.add(StandardMaterial {
                base_color: c,
                perceptual_roughness: 0.9,
                ..default()
            })
        })
        .collect();
    let head_mat = materials.add(StandardMaterial {
        base_color: Color::srgb(0.87, 0.72, 0.58),
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

        commands
            .spawn((
                Mover {
                    heading,
                    speed,
                    retarget_in: 1.0 + rand01(&mut rng) * 4.0,
                    rng: entity_rng,
                },
                Transform::from_xyz(x, y, z).with_rotation(Quat::from_rotation_y(-heading)),
                Visibility::default(),
            ))
            .with_children(|p| {
                p.spawn((
                    Mesh3d(body_mesh.clone()),
                    MeshMaterial3d(body_mats[spawned % body_mats.len()].clone()),
                    Transform::from_xyz(0.0, BODY_HEIGHT * 0.5, 0.0),
                ));
                p.spawn((
                    Mesh3d(head_mesh.clone()),
                    MeshMaterial3d(head_mat.clone()),
                    Transform::from_xyz(0.0, BODY_HEIGHT + 0.2, 0.0),
                ));
            });
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
