//! Small corner overlay: FPS, frame time, and demo facts.

use bevy::diagnostic::{DiagnosticsStore, FrameTimeDiagnosticsPlugin};
use bevy::prelude::*;

use crate::config::*;

pub struct OverlayPlugin;

impl Plugin for OverlayPlugin {
    fn build(&self, app: &mut App) {
        app.add_plugins(FrameTimeDiagnosticsPlugin::default())
            .add_systems(Startup, spawn_overlay)
            .add_systems(Update, (update_overlay, log_fps));
    }
}

#[derive(Component)]
struct OverlayText;

fn spawn_overlay(mut commands: Commands) {
    commands.spawn((
        OverlayText,
        Text::new(overlay_string(None, None)),
        TextFont {
            font_size: FontSize::Px(13.0),
            ..default()
        },
        TextColor(Color::srgba(0.95, 0.95, 0.95, 0.9)),
        Node {
            position_type: PositionType::Absolute,
            top: Val::Px(8.0),
            left: Val::Px(10.0),
            ..default()
        },
    ));
}

fn overlay_string(fps: Option<f64>, frame_ms: Option<f64>) -> String {
    let fps = fps.map_or("--".to_string(), |v| format!("{v:.0}"));
    let frame = frame_ms.map_or("--".to_string(), |v| format!("{v:.1}"));
    format!(
        "Rust -> WASM -> WebGPU\n\
         FPS: {fps}\n\
         Frame: {frame} ms\n\
         Movers: {MOVER_COUNT}\n\
         World: {WORLD_WIDTH} x {WORLD_DEPTH}\n\
         Visible scale: ~{TARGET_VISIBLE_VOXELS:.0} voxels"
    )
}

/// Log FPS to the console every few seconds so headless/browser runs can be
/// measured from the terminal (wasm-server-runner forwards console output).
fn log_fps(time: Res<Time>, diagnostics: Res<DiagnosticsStore>, mut next_log: Local<f32>) {
    if time.elapsed_secs() < *next_log {
        return;
    }
    *next_log = time.elapsed_secs() + 5.0;
    if let Some(fps) = diagnostics
        .get(&FrameTimeDiagnosticsPlugin::FPS)
        .and_then(|d| d.smoothed())
    {
        info!("fps: {fps:.0} (movers: {MOVER_COUNT})");
    }
}

fn update_overlay(
    diagnostics: Res<DiagnosticsStore>,
    mut query: Query<&mut Text, With<OverlayText>>,
) {
    let fps = diagnostics
        .get(&FrameTimeDiagnosticsPlugin::FPS)
        .and_then(|d| d.smoothed());
    let frame_ms = diagnostics
        .get(&FrameTimeDiagnosticsPlugin::FRAME_TIME)
        .and_then(|d| d.smoothed());
    for mut text in &mut query {
        **text = overlay_string(fps, frame_ms);
    }
}
