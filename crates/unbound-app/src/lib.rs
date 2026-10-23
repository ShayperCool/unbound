//! The windowed player: a Bevy app that drives the game's frames on the main thread and renders
//! the managed engine's render protocol with ports of Unity's shaders.
//!
//! ```text
//! main thread (Bevy Update)            render world (Bevy render app)
//! ─────────────────────────            ──────────────────────────────
//! FrameDriver::frame(input) ──calls──▶ QueuedBackend ──extract──▶ UnityRenderer (prepare, encode)
//! ```
pub mod backend;
pub mod input;
pub mod materials;
pub mod render;
pub mod shaderlab;

use anyhow::Result;
use backend::QueuedBackend;
use bevy::{
    app::AppExit,
    input::{
        ButtonState,
        keyboard::KeyboardInput,
        mouse::{MouseScrollUnit, MouseWheel},
    },
    prelude::*,
    render::{
        camera::CameraRenderGraph,
        view::window::screenshot::{Screenshot, ScreenshotCaptured},
    },
    window::{ExitCondition, PresentMode, PrimaryWindow, WindowCloseRequested, WindowResolution},
};
use std::{
    path::PathBuf,
    sync::{
        Arc,
        atomic::{AtomicBool, AtomicUsize, Ordering},
    },
    time::Instant,
};
pub use unbound_runtime::{FrameInput, KeyEvent};

/// What produces frames: the managed game, or a synthetic producer (tests, demos). Called on
/// the main thread only.
pub trait FrameDriver: 'static {
    /// Runs one frame; false when the game asked to quit.
    fn frame(&mut self, input: &FrameInput) -> bool;
    fn shutdown(&mut self);
}

impl FrameDriver for unbound_runtime::Game {
    fn frame(&mut self, input: &FrameInput) -> bool {
        // Edits made since the last frame are patched in between frames, as in the Editor; one
        // that cannot be patched ends the session for a restart.
        self.apply_changes();
        if unbound_runtime::restart_requested() {
            return false;
        }
        unbound_runtime::Game::frame(self, input)
    }

    fn shutdown(&mut self) {
        unbound_runtime::Game::shutdown(self);
    }
}

#[derive(Debug, Clone)]
pub struct AppOptions {
    pub title: String,
    /// Window size in physical pixels.
    pub width: u32,
    pub height: u32,
    /// Write the image of the last frame here (PNG) and exit.
    pub screenshot: Option<PathBuf>,
    /// Run this many frames, then exit (after the screenshot, if any).
    pub frames: Option<u32>,
    /// Write the image after these frames (PNG) and keep running.
    pub snapshots: Vec<(u32, PathBuf)>,
    /// Where the project's `.shader` files are (its `Assets` and package roots).
    pub shader_roots: Vec<PathBuf>,
    /// How project shaders' own programs compile (none: the generic family draws shaders
    /// without a port).
    pub shader_compiler: Option<unbound_shaders::Settings>,
}

impl Default for AppOptions {
    fn default() -> Self {
        AppOptions {
            title: "Unbound".into(),
            width: 540,
            height: 960,
            screenshot: None,
            frames: None,
            snapshots: Vec::new(),
            shader_roots: Vec::new(),
            shader_compiler: None,
        }
    }
}

struct Driver {
    inner: Box<dyn FrameDriver>,
    last: Option<Instant>,
    frames: u32,
    stopped: bool,
    keys: input::KeySet,
    text: String,
    key_events: Vec<KeyEvent>,
    mouse: Vec2,
    quit_requested: bool,
}

impl Driver {
    fn stop(&mut self) {
        if !self.stopped {
            self.stopped = true;
            self.inner.shutdown();
        }
    }
}

impl Drop for Driver {
    fn drop(&mut self) {
        self.stop();
    }
}

#[derive(Resource)]
struct RunSettings {
    screenshot: Option<PathBuf>,
    frames: Option<u32>,
    screenshot_requested: bool,
    /// Set by the screenshot observer once the file is written.
    saved: Arc<AtomicBool>,
    snapshots: Vec<(u32, PathBuf)>,
    /// Snapshots requested but not yet written; the run waits for them before exiting.
    pending: Arc<AtomicUsize>,
}

/// Runs the windowed app until the game quits or the window closes. `backend` must be the
/// backend the driver's frames submit to.
pub fn run(
    driver: Box<dyn FrameDriver>,
    backend: Arc<QueuedBackend>,
    options: AppOptions,
) -> Result<()> {
    let mut app = App::new();
    app.add_plugins(
        DefaultPlugins
            .set(WindowPlugin {
                primary_window: Some(Window {
                    title: options.title.clone(),
                    resolution: WindowResolution::new(options.width.max(1), options.height.max(1)),
                    resizable: true,
                    present_mode: PresentMode::AutoVsync,
                    ..default()
                }),
                exit_condition: ExitCondition::DontExit,
                close_when_requested: false,
                ..default()
            })
            .build(),
    );
    app.add_plugins(render::UnityRenderPlugin {
        backend,
        shader_roots: options.shader_roots.clone(),
        shader_compiler: options.shader_compiler.clone(),
    });
    app.insert_non_send(Driver {
        inner: driver,
        last: None,
        frames: 0,
        stopped: false,
        keys: input::KeySet::default(),
        text: String::new(),
        key_events: Vec::new(),
        mouse: Vec2::ZERO,
        quit_requested: false,
    });
    app.insert_resource(RunSettings {
        screenshot: options.screenshot.clone(),
        frames: options.frames,
        screenshot_requested: false,
        saved: Arc::new(AtomicBool::new(false)),
        snapshots: options.snapshots.clone(),
        pending: Arc::new(AtomicUsize::new(0)),
    });
    app.add_systems(Startup, spawn_view);
    app.add_systems(PreUpdate, drive.after(bevy::input::InputSystems));
    app.add_systems(Update, finish_run);
    match app.run() {
        AppExit::Success => Ok(()),
        AppExit::Error(code) => anyhow::bail!("the app exited with code {code}"),
    }
}

fn spawn_view(mut commands: Commands) {
    commands.spawn((
        Camera2d,
        Camera {
            clear_color: ClearColorConfig::None,
            ..default()
        },
        CameraRenderGraph::new(render::UnityFrameGraph),
        Msaa::Off,
    ));
}

#[allow(clippy::too_many_arguments)]
fn drive(
    mut driver: NonSendMut<Driver>,
    mut settings: ResMut<RunSettings>,
    keys: Res<ButtonInput<KeyCode>>,
    buttons: Res<ButtonInput<MouseButton>>,
    mut wheel: MessageReader<MouseWheel>,
    mut keyboard: MessageReader<KeyboardInput>,
    mut close: MessageReader<WindowCloseRequested>,
    windows: Query<&Window, With<PrimaryWindow>>,
    mut commands: Commands,
    mut exit: MessageWriter<AppExit>,
) {
    if close.read().count() > 0 {
        driver.quit_requested = true;
    }
    if driver.stopped {
        return;
    }
    if let Some(limit) = settings.frames
        && driver.frames >= limit
    {
        // Frozen on the last frame: wait for the screenshot (finish_run exits).
        if settings.screenshot.is_some() && !settings.screenshot_requested {
            settings.screenshot_requested = true;
            let path = settings.screenshot.clone().unwrap();
            let saved = settings.saved.clone();
            capture(&mut commands, path, move || {
                saved.store(true, Ordering::SeqCst)
            });
        }
        return;
    }
    let Ok(window) = windows.single() else {
        return;
    };
    let now = Instant::now();
    let dt = driver.last.map_or(0.0, |last| (now - last).as_secs_f64());
    driver.last = Some(now);

    let mut set = input::KeySet::default();
    for key in keys.get_pressed() {
        set.set(input::unity_key(*key));
    }
    let mut mouse_buttons = 0u32;
    for (bit, button) in [
        MouseButton::Left,
        MouseButton::Right,
        MouseButton::Middle,
        MouseButton::Back,
        MouseButton::Forward,
    ]
    .into_iter()
    .enumerate()
    {
        if buttons.pressed(button) {
            mouse_buttons |= 1 << bit;
            set.set(input::MOUSE0 + bit as u16);
        }
    }
    driver.keys = set;
    let mut scroll = Vec2::ZERO;
    for event in wheel.read() {
        let scale = match event.unit {
            MouseScrollUnit::Line => 1.0,
            // Unity reports wheel notches; a notch is ~16 pixels of smooth scrolling.
            MouseScrollUnit::Pixel => 1.0 / 16.0,
        };
        scroll += Vec2::new(event.x, event.y) * scale;
    }
    driver.text.clear();
    driver.key_events.clear();
    for event in keyboard.read() {
        let pressed = event.state == ButtonState::Pressed;
        let start = driver.text.len() as i32;
        if pressed && let Some(text) = &event.text {
            let text = text.clone();
            driver.text.push_str(&text);
        }
        let text_length = driver.text.len() as i32 - start;
        driver.key_events.push(KeyEvent {
            key: i32::from(input::unity_key(event.key_code)),
            pressed: i32::from(pressed),
            text_start: start,
            text_length,
        });
    }
    if let Some(position) = window.physical_cursor_position() {
        driver.mouse = position;
    }
    let input = FrameInput {
        real_delta_time: dt,
        screen_width: window.physical_width() as i32,
        screen_height: window.physical_height() as i32,
        mouse_x: driver.mouse.x,
        mouse_y: driver.mouse.y,
        scroll_x: scroll.x,
        scroll_y: scroll.y,
        mouse_buttons,
        focused: i32::from(window.focused),
        keys: driver.keys.0.as_ptr(),
        text: driver.text.as_ptr(),
        text_length: driver.text.len() as i32,
        quit_requested: i32::from(driver.quit_requested),
        key_events: driver.key_events.as_ptr(),
        key_event_count: driver.key_events.len() as i32,
    };
    let keep_going = driver.inner.frame(&input);
    driver.frames += 1;
    let frame = driver.frames;
    for (_, path) in settings.snapshots.iter().filter(|(at, _)| *at == frame) {
        let pending = settings.pending.clone();
        pending.fetch_add(1, Ordering::SeqCst);
        capture(&mut commands, path.clone(), move || {
            pending.fetch_sub(1, Ordering::SeqCst);
        });
    }
    if !keep_going || driver.quit_requested {
        driver.stop();
        exit.write(AppExit::Success);
    }
}

fn finish_run(
    mut driver: NonSendMut<Driver>,
    settings: Res<RunSettings>,
    mut exit: MessageWriter<AppExit>,
) {
    let Some(limit) = settings.frames else {
        return;
    };
    let done = if settings.screenshot.is_some() {
        settings.saved.load(Ordering::SeqCst)
    } else {
        driver.frames >= limit
    } && settings.pending.load(Ordering::SeqCst) == 0;
    if done {
        driver.stop();
        exit.write(AppExit::Success);
    }
}

/// Captures the window's next presented image to a PNG file, then calls `written`.
fn capture(commands: &mut Commands, path: PathBuf, written: impl Fn() + Send + Sync + 'static) {
    commands.spawn(Screenshot::primary_window()).observe(
        move |captured: On<ScreenshotCaptured>| {
            match save_png(&captured.image, &path) {
                Ok(()) => tracing::info!("screenshot written to {}", path.display()),
                Err(e) => tracing::error!("writing the screenshot: {e:#}"),
            }
            written();
        },
    );
}

/// Writes a captured window image (RGBA or BGRA, 8 bits) as PNG.
fn save_png(image: &Image, path: &std::path::Path) -> Result<()> {
    use bevy::render::render_resource::TextureFormat;
    let width = image.width();
    let height = image.height();
    let data = image
        .data
        .as_ref()
        .ok_or_else(|| anyhow::anyhow!("the screenshot has no data"))?;
    let format = image.texture_descriptor.format;
    let mut rgba = Vec::with_capacity((width * height * 4) as usize);
    match format {
        TextureFormat::Bgra8Unorm | TextureFormat::Bgra8UnormSrgb => {
            for px in data.as_chunks::<4>().0 {
                rgba.extend_from_slice(&[px[2], px[1], px[0], 255]);
            }
        }
        TextureFormat::Rgba8Unorm | TextureFormat::Rgba8UnormSrgb => {
            for px in data.as_chunks::<4>().0 {
                rgba.extend_from_slice(&[px[0], px[1], px[2], 255]);
            }
        }
        other => anyhow::bail!("unsupported screenshot format {other:?}"),
    }
    if let Some(parent) = path.parent()
        && !parent.as_os_str().is_empty()
    {
        std::fs::create_dir_all(parent)?;
    }
    let buffer = image::RgbaImage::from_raw(width, height, rgba)
        .ok_or_else(|| anyhow::anyhow!("the screenshot's size does not match its data"))?;
    buffer.save_with_format(path, image::ImageFormat::Png)?;
    Ok(())
}
