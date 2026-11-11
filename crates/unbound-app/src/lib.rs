//! The windowed player: a Bevy app that drives the game's frames on the main thread and executes
//! the managed engine's rendering commands with Unity's shaders compiled from their sources.
//!
//! ```text
//! main thread (Bevy PreUpdate)                         render world (Bevy render app)
//! ────────────────────────────                         ──────────────────────────────
//! FrameDriver::frame ─calls─▶ GpuBackend ─▶ UnityRenderer   Presenter: shows the latest frame
//!                              (encodes and queues on the GPU at once)
//! ```
pub mod backend;
pub mod input;
pub mod materials;
pub mod remote;
pub mod render;
pub mod shaderlab;

mod host_display;

use anyhow::Result;
use backend::GpuBackend;
use bevy::{
    app::AppExit,
    input::{
        ButtonState,
        mouse::{AccumulatedMouseMotion, MouseScrollUnit, MouseWheel},
    },
    prelude::*,
    render::{
        camera::CameraRenderGraph,
        view::window::screenshot::{Screenshot, ScreenshotCaptured},
    },
    window::{
        CursorGrabMode, CursorIcon, CursorOptions, CustomCursor, CustomCursorImage, ExitCondition,
        PresentMode, PrimaryWindow, SystemCursorIcon, WindowCloseRequested, WindowEvent,
        WindowResolution,
    },
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
    /// How shader programs compile.
    pub shader_compiler: Option<unbound_shaders::Settings>,
    /// Remote play: the session the window's frames go to and whose viewers' input joins the
    /// window's.
    pub remote: Option<Arc<unbound_remote::Session>>,
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
            remote: None,
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
    remote: remote::RemoteInput,
    /// The game's cursor request last applied (`unbound_runtime::cursor`).
    cursor_seen: u64,
    /// The game's cursor, and whether Escape gave the pointer back (as the Editor does; a click
    /// in the window takes it again).
    cursor: Option<unbound_runtime::cursor::CursorState>,
    cursor_released: bool,
    /// Whether a warp outside the window was reported (once).
    warp_outside_warned: bool,
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
    remote: Option<Arc<unbound_remote::Session>>,
    /// A frame for the remote session being captured.
    remote_capture: remote::CaptureState,
}

/// Starts the graphics device: makes its GPU device, which the window's renderer then takes;
/// false when its backend has no GPU that can draw it.
pub fn start_device(device: unbound_compat::GraphicsDevice) -> bool {
    render::device::start(device)
}

/// Offers the graphics device's own plugin interfaces (`IUnityGraphicsVulkan` on Vulkan), before
/// any plugin loads; their device handles are filled once the renderer is up. On OpenGL Core
/// the Vulkan interfaces are answered too, as the Editor answers them, and refuse every call.
pub fn provide_plugin_interfaces(device: unbound_compat::GraphicsDevice) {
    #[cfg(target_os = "linux")]
    match device {
        unbound_compat::GraphicsDevice::Vulkan => {
            render::executor::provide_vulkan_plugin_interfaces()
        }
        unbound_compat::GraphicsDevice::OpenGLCore => {
            render::executor::provide_vulkan_plugin_interfaces_elsewhere()
        }
        unbound_compat::GraphicsDevice::Metal => {}
    }
    let _ = device;
}

/// Runs the windowed app until the game quits or the window closes. `backend` must be the
/// backend the driver's frames submit to.
pub fn run(
    driver: Box<dyn FrameDriver>,
    backend: Arc<GpuBackend>,
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
                    // Any size the run asks for (`--size`, `Screen.SetResolution`): Bevy's default
                    // minimum of 180x120 goes into the X11 size hints, and a smaller window is
                    // then made that size.
                    resize_constraints: bevy::window::WindowResizeConstraints {
                        min_width: 1.0,
                        min_height: 1.0,
                        ..default()
                    },
                    present_mode: PresentMode::AutoVsync,
                    // The OpenGL backend's window surfaces take its EGL config's visual, 32-bit
                    // with alpha: winit makes a window of that visual when it may be transparent
                    // (the present writes opaque alpha).
                    transparent: render::device::backends(unbound_runtime::graphics_device::get())
                        == wgpu::Backends::GL,
                    ..default()
                }),
                exit_condition: ExitCondition::DontExit,
                close_when_requested: false,
                ..default()
            })
            .set(bevy::render::RenderPlugin {
                // The GPU device started for the run's graphics device.
                render_creation: bevy::render::settings::RenderCreation::Manual(Box::new(
                    render::device::take_started(unbound_runtime::graphics_device::get()),
                )),
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
        remote: remote::RemoteInput::default(),
        cursor_seen: 0,
        cursor: None,
        cursor_released: false,
        warp_outside_warned: false,
    });
    app.insert_resource(RunSettings {
        screenshot: options.screenshot.clone(),
        frames: options.frames,
        screenshot_requested: false,
        saved: Arc::new(AtomicBool::new(false)),
        snapshots: options.snapshots.clone(),
        pending: Arc::new(AtomicUsize::new(0)),
        remote: options.remote.clone(),
        remote_capture: Default::default(),
    });
    app.add_systems(Startup, spawn_view);
    app.add_systems(PreUpdate, drive.after(bevy::input::InputSystems));
    app.add_systems(PreUpdate, publish_display.before(drive));
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

/// The window's mouse buttons in `FrameInput::mouse_buttons` bit order (Unity's `Mouse0` on).
const MOUSE_BUTTONS: [MouseButton; 5] = [
    MouseButton::Left,
    MouseButton::Right,
    MouseButton::Middle,
    MouseButton::Back,
    MouseButton::Forward,
];

#[allow(clippy::too_many_arguments)]
fn drive(
    mut driver: NonSendMut<Driver>,
    mut settings: ResMut<RunSettings>,
    keys: Res<ButtonInput<KeyCode>>,
    buttons: Res<ButtonInput<MouseButton>>,
    mut wheel: MessageReader<MouseWheel>,
    mut window_events: MessageReader<WindowEvent>,
    mut close: MessageReader<WindowCloseRequested>,
    mut windows: Query<&mut Window, With<PrimaryWindow>>,
    motion: Res<AccumulatedMouseMotion>,
    mut cursors: Query<(Entity, &mut CursorOptions), With<PrimaryWindow>>,
    mut images: ResMut<Assets<Image>>,
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
    for (bit, button) in MOUSE_BUTTONS.into_iter().enumerate() {
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
    // The input method is the window's own; a remote session's viewers type elsewhere.
    // inputplatform measures defaults only: no configured input method is available to
    // establish composition parity. Keep legacy composition explicitly unsupported; committed
    // text continues through the normal text events.
    unbound_runtime::ime::attach(false);
    // Key and mouse button changes in the order they happened: several may fall between two
    // frames (a click within a frame is a press and a release).
    for event in window_events.read() {
        match event {
            WindowEvent::KeyboardInput(event) => {
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
            WindowEvent::MouseButtonInput(event) => {
                let Some(bit) = MOUSE_BUTTONS.iter().position(|b| *b == event.button) else {
                    continue;
                };
                let start = driver.text.len() as i32;
                driver.key_events.push(KeyEvent {
                    key: i32::from(input::MOUSE0 + bit as u16),
                    pressed: i32::from(event.state == ButtonState::Pressed),
                    text_start: start,
                    text_length: 0,
                });
            }
            // Text the input method commits joins the frame's text (`Input.inputString`) and comes
            // as a text-only key event; the composition and whether it is enabled go to
            // `Input.compositionString` and `Input.imeIsSelected`.
            WindowEvent::Ime(input) => {
                use unbound_runtime::ime::{self, ImeEvent};
                let event = match input {
                    bevy::window::Ime::Preedit { value, cursor, .. } => ImeEvent::Preedit {
                        value,
                        cursor: *cursor,
                    },
                    bevy::window::Ime::Commit { value, .. } => ImeEvent::Commit(value),
                    bevy::window::Ime::Enabled { .. } => ImeEvent::Enabled,
                    bevy::window::Ime::Disabled { .. } => ImeEvent::Disabled,
                };
                // Committed text also comes as a text-only key event (key 0), as typed text does,
                // so IMGUI/uGUI text fields and the Input System receive it, not only inputString.
                let added = ime::event(event, &mut driver.text);
                if !added.is_empty() {
                    driver.key_events.push(KeyEvent {
                        key: 0,
                        pressed: 1,
                        text_start: added.start as i32,
                        text_length: added.len() as i32,
                    });
                }
            }
            _ => {}
        }
    }
    // Bevy knows the pointer only while it is over the window (it forgets it on leaving, and
    // drops positions outside): the last position inside stays until it returns. Documented-
    // contract gap, not a choice: Unity 6000.0 Input.mousePosition reports the pointer outside
    // the game view (values below 0 or above the screen size) while the cursor is neither locked
    // nor confined. Open in docs/COMPATIBILITY_GAPS.md.
    if let Some(position) = window.physical_cursor_position() {
        driver.mouse = position;
    }
    // The remote viewers' input joins the window's.
    let mut watched = false;
    if let Some(session) = &settings.remote {
        watched = session.watching();
        let events = session.take_input();
        driver.remote.update(events);
        let size = Vec2::new(
            window.physical_width() as f32,
            window.physical_height() as f32,
        );
        if let Some(pointer) = driver.remote.pointer
            && (driver.remote.active || driver.remote.buttons != 0)
        {
            driver.mouse = pointer * size;
        }
        mouse_buttons |= driver.remote.buttons;
        for bit in 0..5 {
            if driver.remote.buttons & (1 << bit) != 0 {
                driver.keys.set(input::MOUSE0 + bit as u16);
            }
        }
        for code in 0..512u16 {
            if driver.remote.keys.contains(code) {
                driver.keys.set(code);
            }
        }
        scroll += driver.remote.scroll;
        let events = std::mem::take(&mut driver.remote.key_events);
        for (key, pressed, text) in &events {
            let start = driver.text.len() as i32;
            if let Some(c) = text {
                driver.text.push(*c);
            }
            let text_length = driver.text.len() as i32 - start;
            driver.key_events.push(KeyEvent {
                key: i32::from(*key),
                pressed: i32::from(*pressed),
                text_start: start,
                text_length,
            });
        }
        driver.remote.key_events = events;
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
        // A game played from a browser has the focus there.
        focused: i32::from(window.focused || watched),
        keys: driver.keys.0.as_ptr(),
        text: driver.text.as_ptr(),
        text_length: driver.text.len() as i32,
        quit_requested: i32::from(driver.quit_requested),
        key_events: driver.key_events.as_ptr(),
        key_event_count: driver.key_events.len() as i32,
        paced: 1,
        raw_motion_x: motion.delta.x,
        raw_motion_y: motion.delta.y,
        raw_motion: 1,
        display_version: unbound_runtime::display::version(),
        ime_version: unbound_runtime::ime::version(),
    };
    let keep_going = driver.inner.frame(&input);
    // The presentation follows the game's vertical sync setting.
    if let Some(vsync) = unbound_runtime::presentation::vsync()
        && let Ok(mut window) = windows.single_mut()
    {
        let mode = if vsync {
            PresentMode::AutoVsync
        } else {
            PresentMode::AutoNoVsync
        };
        if window.present_mode != mode {
            window.present_mode = mode;
        }
    }
    if let Ok((entity, mut options)) = cursors.single_mut() {
        if let Some(mut cursor) = unbound_runtime::cursor::changed(&mut driver.cursor_seen) {
            let warp = cursor.warp.take();
            // A warp alone leaves the applied lock, visibility and image (and a pointer Escape
            // gave back) as they are.
            if warp.is_none() || driver.cursor.as_ref() != Some(&cursor) {
                apply_cursor(&cursor, entity, &mut options, &mut images, &mut commands);
                driver.cursor_released = false;
            }
            driver.cursor = Some(cursor);
            if let Some(warp) = warp
                && let Ok(mut window) = windows.single_mut()
            {
                warp_cursor(&mut window, warp, &mut driver.warp_outside_warned);
            }
        } else if let Some(cursor) = &driver.cursor
            && (cursor.lock != 0 || !cursor.visible)
        {
            // Escape gives the pointer back, a click in the window takes it again (the Editor's
            // game view does so).
            if !driver.cursor_released && keys.just_pressed(KeyCode::Escape) {
                options.grab_mode = CursorGrabMode::None;
                options.visible = true;
                driver.cursor_released = true;
            } else if driver.cursor_released && buttons.just_pressed(MouseButton::Left) {
                options.grab_mode = grab_mode(cursor.lock);
                options.visible = cursor.visible;
                driver.cursor_released = false;
            }
        }
    }
    // The game's IME request (`Input.imeCompositionMode`, `Input.compositionCursorPos` in window
    // pixels from the top left; Bevy places the input method's windows in logical pixels).
    if let Some(request) = unbound_runtime::ime::take_request()
        && settings.remote.is_none()
        && let Ok(mut window) = windows.single_mut()
    {
        let position = Vec2::from(request.position) / window.scale_factor();
        if window.ime_enabled != request.enabled {
            window.ime_enabled = request.enabled;
        }
        if window.ime_position != position {
            window.ime_position = position;
        }
    }
    driver.frames += 1;
    if let Some(session) = &settings.remote {
        remote::capture(&mut commands, session, &settings.remote_capture);
    }
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

/// The game's cursor on the window: Unity's lock modes as grab modes (X11 confines a locked
/// cursor: its raw motion still reaches the game), the visibility, the image and its hotspot.
fn apply_cursor(
    cursor: &unbound_runtime::cursor::CursorState,
    window: Entity,
    options: &mut CursorOptions,
    images: &mut Assets<Image>,
    commands: &mut Commands,
) {
    options.grab_mode = grab_mode(cursor.lock);
    options.visible = cursor.visible;
    let icon = match &cursor.image {
        None => CursorIcon::System(SystemCursorIcon::Default),
        Some(image) => {
            let handle = images.add(Image::new(
                bevy::render::render_resource::Extent3d {
                    width: image.width,
                    height: image.height,
                    depth_or_array_layers: 1,
                },
                bevy::render::render_resource::TextureDimension::D2,
                image.rgba.clone(),
                bevy::render::render_resource::TextureFormat::Rgba8UnormSrgb,
                bevy::asset::RenderAssetUsages::MAIN_WORLD,
            ));
            CursorIcon::Custom(CustomCursor::Image(CustomCursorImage {
                handle,
                hotspot: image.hotspot,
                ..Default::default()
            }))
        }
    };
    commands.entity(window).insert(icon);
}

/// `Mouse.WarpCursorPosition`: Unity's screen coordinates (window pixels from the bottom left)
/// as the window's (from the top left). Bevy moves the system's cursor when the window's cursor
/// position changes, only to a position inside the window; one outside is reported (once) and
/// not applied. The next frame reads the new position as a pointer move.
fn warp_cursor(window: &mut Window, [x, y]: [f64; 2], warned: &mut bool) {
    let width = f64::from(window.physical_width());
    let height = f64::from(window.physical_height());
    let target = bevy::math::DVec2::new(x, height - y);
    if target.x >= 0.0 && target.y >= 0.0 && target.x < width && target.y < height {
        window.set_physical_cursor_position(Some(target));
    } else if !*warned {
        *warned = true;
        tracing::warn!(
            "Mouse.WarpCursorPosition({x}, {y}) lies outside the {width}x{height} window: \
             Unbound moves the cursor only within the window, this warp and later ones outside it are ignored"
        );
    }
}

/// Unity's `CursorLockMode` as a grab mode.
fn grab_mode(lock: i32) -> CursorGrabMode {
    match lock {
        1 => CursorGrabMode::Locked,
        2 => CursorGrabMode::Confined,
        _ => CursorGrabMode::None,
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

/// Makes host display answers available to Awake/OnEnable before the window is created.
/// If no display can be queried, keep the explicit unavailable answer until window discovery.
pub fn initialize_display() {
    match host_display::snapshot(None) {
        Ok(display) => {
            unbound_runtime::display::publish(unbound_runtime::display::Display::Known(display));
            host_display::watch();
        }
        Err(error) => unbound_runtime::display::publish(
            unbound_runtime::display::Display::Unavailable(format!("{error:#}")),
        ),
    }
}

/// Publishes the display the window is on for `Screen.currentResolution` & co
/// (`unbound_runtime::display`): the monitor the window system places the window on, else the
/// primary monitor. These APIs describe the host desktop, including in a remote session.
/// Republished when the window changes monitor or the monitor's description changes.
fn publish_display(
    windows: Query<Option<&bevy::window::OnMonitor>, With<PrimaryWindow>>,
    monitors: Query<(
        Entity,
        Ref<bevy::window::Monitor>,
        bevy::ecs::query::Has<bevy::window::PrimaryMonitor>,
    )>,
    mut published: Local<Option<Option<Entity>>>,
) {
    use unbound_runtime::display::{self, Display, DisplayMode, DisplaySnapshot};
    let on = windows.single().ok().flatten().map(|link| link.0);
    let found = on
        .and_then(|entity| monitors.get(entity).ok())
        .or_else(|| monitors.iter().find(|(_, _, primary)| *primary));
    let key = found.as_ref().map(|(entity, _, _)| *entity);
    let changed = found
        .as_ref()
        .is_some_and(|(_, monitor, _)| monitor.is_changed());
    let host_changed = host_display::changed();
    if *published == Some(key) && !changed && !host_changed {
        return;
    }
    *published = Some(key);
    let Some((entity, monitor, _)) = found else {
        display::publish(Display::Unavailable(
            "the window system reports no monitor".into(),
        ));
        return;
    };
    match host_display::snapshot(monitor.name.as_deref()) {
        Ok(snapshot) => {
            host_display::watch();
            display::publish(Display::Known(snapshot));
            return;
        }
        Err(error) => tracing::warn!("Screen display details unavailable: {error:#}"),
    }
    let details = host_display::Details {
        refresh_numerator: monitor.refresh_rate_millihertz.unwrap_or(0),
        refresh_denominator: if monitor.refresh_rate_millihertz.is_some() {
            1000
        } else {
            1
        },
        ..Default::default()
    };
    display::publish(Display::Known(DisplaySnapshot {
        dpi: details.dpi,
        info_refresh_numerator: details.refresh_numerator,
        info_refresh_denominator: details.refresh_denominator.max(1),
        work_area: details.work_area.unwrap_or([
            0,
            0,
            monitor.physical_width as i32,
            monitor.physical_height as i32,
        ]),
        id: entity.to_bits(),
        name: monitor.name.clone(),
        width: monitor.physical_width,
        height: monitor.physical_height,
        refresh_rate_millihertz: monitor.refresh_rate_millihertz,
        scale_factor: monitor.scale_factor,
        modes: monitor
            .video_modes
            .iter()
            .map(|mode| DisplayMode {
                width: mode.physical_size.x,
                height: mode.physical_size.y,
                refresh_rate_millihertz: mode.refresh_rate_millihertz,
            })
            .collect(),
    }));
}
