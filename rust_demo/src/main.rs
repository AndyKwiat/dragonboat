//! Rust + WASM + WebGPU voxel demo.
//!
//! `main.rs` only composes plugins; each subsystem lives in its own module.
//! Tuning constants live in `config.rs`.

mod camera;
mod config;
mod diagnostics;
mod movers;
mod terrain;
mod world;

use bevy::prelude::*;

fn main() {
    App::new()
        .add_plugins(DefaultPlugins.set(WindowPlugin {
            primary_window: Some(Window {
                title: "Rust + WASM + WebGPU Voxel Demo".into(),
                // On the web, attach to the page's canvas and track its size.
                canvas: Some("#bevy-canvas".into()),
                fit_canvas_to_parent: true,
                prevent_default_event_handling: false,
                ..default()
            }),
            ..default()
        }))
        .add_plugins(world::WorldPlugin)
        .add_plugins(terrain::TerrainPlugin)
        .add_plugins(movers::MoversPlugin)
        .add_plugins(camera::CameraPlugin)
        .add_plugins(diagnostics::OverlayPlugin)
        .run();
}
