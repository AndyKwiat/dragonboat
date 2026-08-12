//! Fixed orthographic isometric-style camera plus lighting.

use bevy::camera::ScalingMode;
use bevy::prelude::*;

use crate::config::*;

pub struct CameraPlugin;

impl Plugin for CameraPlugin {
    fn build(&self, app: &mut App) {
        app.add_systems(Startup, setup_camera_and_light);
    }
}

/// Vertical world-units the orthographic view should span so that roughly
/// `target_visible_voxels` ground cells fit vertically. Looking down at
/// elevation angle `e`, ground depth foreshortens by sin(e).
fn viewport_height_for_visible_voxels(target_visible_voxels: f32, elevation_deg: f32) -> f32 {
    target_visible_voxels * elevation_deg.to_radians().sin()
}

fn setup_camera_and_light(mut commands: Commands) {
    let center = Vec3::new(WORLD_WIDTH as f32 * 0.5, 0.0, WORLD_DEPTH as f32 * 0.5);

    // Position camera on a sphere around the world center.
    let azimuth = CAMERA_AZIMUTH_DEGREES.to_radians();
    let elevation = CAMERA_ELEVATION_DEGREES.to_radians();
    let distance = 400.0;
    let offset = Vec3::new(
        elevation.cos() * azimuth.cos(),
        elevation.sin(),
        elevation.cos() * azimuth.sin(),
    ) * distance;

    commands.spawn((
        Camera3d::default(),
        Projection::Orthographic(OrthographicProjection {
            scaling_mode: ScalingMode::FixedVertical {
                viewport_height: viewport_height_for_visible_voxels(
                    TARGET_VISIBLE_VOXELS,
                    CAMERA_ELEVATION_DEGREES,
                ),
            },
            // Keep the whole world inside the clip range.
            near: -600.0,
            far: 1200.0,
            ..OrthographicProjection::default_3d()
        }),
        Transform::from_translation(center + offset).looking_at(center, Vec3::Y),
        // Ambient fill so shadowed faces stay readable (component on the camera).
        AmbientLight {
            color: Color::srgb(0.85, 0.9, 1.0),
            brightness: 220.0,
            ..default()
        },
    ));

    // Sun: angled so hills and movers cast readable shadows.
    commands.spawn((
        DirectionalLight {
            illuminance: 11000.0,
            shadow_maps_enabled: true,
            ..default()
        },
        Transform::from_translation(Vec3::new(1.0, 2.0, 0.4)).looking_at(Vec3::ZERO, Vec3::Y),
    ));

    // Soft sky-like clear color.
    commands.insert_resource(ClearColor(Color::srgb(0.10, 0.12, 0.16)));
}
