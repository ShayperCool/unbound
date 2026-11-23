//! A running game: the managed engine's start, frame and shutdown entry points.
use crate::{
    config::GameOptions,
    native_api,
    render_protocol::RenderBackend,
    scripted::{self, ScriptedInput},
    state,
    watch::{Changes, ProjectWatcher},
};
use anyhow::{Result, bail};
use serde_json::Value as Json;
use std::{
    collections::BTreeMap,
    sync::{
        Arc, Mutex,
        atomic::{AtomicBool, Ordering},
    },
    time::Instant,
};

/// Set when an edit cannot be patched into the running game: the session stops and the caller
/// starts the game again with the new code, as the Editor's "Stop Playing and Recompile" does.
static RESTART: AtomicBool = AtomicBool::new(false);

/// Whether the game stopped so that it can be started again with edited code.
pub fn restart_requested() -> bool {
    RESTART.load(Ordering::SeqCst)
}

/// Set with [`RESTART`] when the edit could not be patched in because scripts were compiled with
/// optimizations: the next session compiles them in Debug, so that later edits patch in place.
static RESTART_IN_DEBUG: AtomicBool = AtomicBool::new(false);

/// Whether the restarted game should compile its scripts in Debug (see [`RESTART_IN_DEBUG`]).
pub fn restart_in_debug() -> bool {
    RESTART_IN_DEBUG.load(Ordering::SeqCst)
}

/// Set when scripts ask for a compilation (`CompilationPipeline.RequestScriptCompilation`, new
/// scripting defines for the active build target): the next change check compiles the scripts and
/// reloads them whatever changed, as the Editor does (tests/projects/compileevents).
static COMPILE_REQUESTED: AtomicBool = AtomicBool::new(false);

/// The scripting defines scripts set in this run (`PlayerSettings.SetScriptingDefineSymbols`), by the
/// key ProjectSettings stores them under. They replace the project's for the run's compilations
/// and are handed to a relaunched session ([`DEFINES_VARIABLE`]); the project's files are not
/// written.
static DEFINE_OVERRIDES: Mutex<BTreeMap<String, Vec<String>>> = Mutex::new(BTreeMap::new());

/// The environment variable a relaunched session reads the run's scripting defines from (JSON, by
/// group key).
pub const DEFINES_VARIABLE: &str = "UNBOUND_SCRIPTING_DEFINES";

/// A reload a locked session deferred (`EditorApplication.UnlockReloadAssemblies`): the session
/// stops for its restart (in Debug when the deferred edit was refused for optimized code).
pub(crate) fn request_restart(optimized: bool) {
    if optimized && std::env::var("UNBOUND_CODE_OPTIMIZATION").as_deref() != Ok("release") {
        RESTART_IN_DEBUG.store(true, Ordering::SeqCst);
    }
    RESTART.store(true, Ordering::SeqCst);
}

pub(crate) fn request_compilation() {
    COMPILE_REQUESTED.store(true, Ordering::SeqCst);
}

pub(crate) fn set_scripting_defines(group: &str, defines: &str) {
    let key = unbound_project::settings::defines_key(group).to_string();
    let list = unbound_project::settings::parse_defines(defines);
    DEFINE_OVERRIDES.lock().unwrap().insert(key, list);
}

/// The run's scripting defines for a relaunched session, as [`DEFINES_VARIABLE`] holds them; `None`
/// when scripts set none.
pub fn scripting_define_overrides() -> Option<String> {
    let overrides = DEFINE_OVERRIDES.lock().unwrap();
    (!overrides.is_empty()).then(|| serde_json::to_string(&*overrides).unwrap_or_default())
}

/// Applies the run's scripting defines (those of the session this one relaunched included) to the
/// project's settings.
fn apply_define_overrides(settings: &mut unbound_project::Settings) {
    let mut overrides = DEFINE_OVERRIDES.lock().unwrap();
    if let Ok(inherited) = std::env::var(DEFINES_VARIABLE) {
        match serde_json::from_str::<BTreeMap<String, Vec<String>>>(&inherited) {
            Ok(inherited) => {
                for (key, list) in inherited {
                    overrides.entry(key).or_insert(list);
                }
            }
            Err(error) => tracing::warn!("{DEFINES_VARIABLE}: {error}"),
        }
    }
    for (key, list) in overrides.iter() {
        settings.set_defines(key, list.clone());
    }
}
use unbound_host::ManagedRuntime;
use unbound_project::Project;

/// Input sampled for one frame; the layout matches `FrameInput` in the managed engine.
#[repr(C)]
#[derive(Clone, Copy)]
pub struct FrameInput {
    pub real_delta_time: f64,
    pub screen_width: i32,
    pub screen_height: i32,
    pub mouse_x: f32,
    pub mouse_y: f32,
    pub scroll_x: f32,
    pub scroll_y: f32,
    pub mouse_buttons: u32,
    pub focused: i32,
    pub keys: *const u8,
    pub text: *const u8,
    pub text_length: i32,
    pub quit_requested: i32,
    /// The frame's key presses (repeats included) and releases, and the mouse buttons' (as
    /// `KeyCode.Mouse0` on), in the order they happened since the last frame.
    pub key_events: *const KeyEvent,
    pub key_event_count: i32,
    /// 1 when frames follow the wall clock in a window: the engine then keeps
    /// `Application.targetFrameRate` (scripted `--fixed-fps` and headless runs go flat out).
    pub paced: i32,
    /// The mouse's raw motion since the last frame (device units, right and down), when
    /// `raw_motion` is 1: what a locked cursor reports.
    pub raw_motion_x: f32,
    pub raw_motion_y: f32,
    pub raw_motion: i32,
    /// Changes only when the host display description changes.
    pub display_version: u64,
    pub ime_version: u64,
    /// Under `--fixed-fps`, the frame's step in seconds (also in the first frame, whose
    /// `real_delta_time` is 0); 0 when frames follow the wall clock. The engine's real-time clock
    /// then follows the fixed frames too.
    pub fixed_step: f64,
    /// 1 when `mouse_x`/`mouse_y` are where the system pointer is (a window's host knows it,
    /// moved or not, also outside the window); 0 when no pointer is known (headless runs).
    pub pointer_known: i32,
}

/// A key press or release; the layout matches `KeyEvent` in the managed engine.
#[repr(C)]
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct KeyEvent {
    /// The Unity `KeyCode` (0 when the key has none).
    pub key: i32,
    /// 1 when pressed (or repeated), 0 when released.
    pub pressed: i32,
    /// The text the press typed: a range of `FrameInput::text`.
    pub text_start: i32,
    pub text_length: i32,
}

impl KeyEvent {
    /// A host keymap notification in the input event stream. Negative keys are reserved for
    /// input metadata and are ignored by the legacy Input Manager.
    pub const CONFIGURATION_CHANGED: Self = Self {
        key: -1,
        pressed: 0,
        text_start: 0,
        text_length: 0,
    };
}

type StartFn = unsafe extern "system" fn(*const native_api::NativeApi, *const u8, i32) -> i32;
/// The managed frame may rewrite the input (it maps the window's pixels to the Game view's
/// rendering pixels before its readers).
type FrameFn = unsafe extern "system" fn(*mut FrameInput) -> i32;
type ShutdownFn = unsafe extern "system" fn();

pub struct Game {
    pub runtime: ManagedRuntime,
    project: Project,
    options: GameOptions,
    watcher: Option<ProjectWatcher>,
    frame: FrameFn,
    shutdown: ShutdownFn,
    /// The API table must stay where the managed side saw it.
    _api: Box<native_api::NativeApi>,
    pub keys: [u8; 64],
    stopped: bool,
    /// Scripted taps: (frame, x, y) in window pixels from the top left.
    taps: Vec<(u64, f32, f32)>,
    /// Scripted typing: (frame, text).
    typing: Vec<(u64, String)>,
    /// Scripted input changes: (frame, change).
    script: Vec<(u64, ScriptedInput)>,
    /// Where the script last put the pointer: it stays there until the window's (or a remote
    /// viewer's) pointer moves, as a real pointer would be taken over.
    pointer: Option<(f32, f32)>,
    /// The window's pointer when the script last put its own.
    pointer_window: (f32, f32),
    /// The keys and mouse buttons the script holds, as `FrameInput::keys` bits.
    held: [u8; 64],
    /// The focus the script gave the window, once it has (it then decides the focus).
    focus: Option<bool>,
    /// Game time per frame when fixed, instead of the wall clock's.
    fixed_delta: Option<f64>,
    frames: u64,
    /// The exit code the game quit with (`Application.Quit(code)`, `EditorApplication.Exit`).
    exit_code: Option<i32>,
}

/// The key events and text of typing `text`, as a keyboard would send them: each character's key
/// pressed with the text it types, then released.
fn typing_events(text: &str) -> (Vec<KeyEvent>, String) {
    let mut events = Vec::new();
    let mut typed = String::new();
    for c in text.chars() {
        let (key, sent) = match c {
            '\n' => (13, '\r'),
            '\u{8}' => (8, '\u{8}'),
            'a'..='z' | '0'..='9' | ' ' => (c as i32, c),
            'A'..='Z' => (c.to_ascii_lowercase() as i32, c),
            _ => (0, c),
        };
        let start = typed.len() as i32;
        typed.push(sent);
        events.push(KeyEvent {
            key,
            pressed: 1,
            text_start: start,
            text_length: typed.len() as i32 - start,
        });
        events.push(KeyEvent {
            key,
            pressed: 0,
            text_start: 0,
            text_length: 0,
        });
    }
    (events, typed)
}

/// How long a scripted tap holds the button, in frames.
const TAP_FRAMES: u64 = 3;

impl Game {
    /// Compiles and configures the project, then starts it in the managed engine (runtime
    /// initializers and the first scene run here).
    pub fn start(project: &Project, options: &GameOptions) -> Result<Self> {
        Self::start_with_backend(project, options, None)
    }

    /// As [`Game::start`], with the renderer that executes the render protocol installed before
    /// the game starts (the first scene uploads its meshes and textures while it loads).
    /// Without one, frames go to the [`NullBackend`](crate::render_protocol::NullBackend).
    pub fn start_with_backend(
        project: &Project,
        options: &GameOptions,
        backend: Option<Arc<dyn RenderBackend>>,
    ) -> Result<Self> {
        let mut project = Project::open(&project.root)?;
        apply_define_overrides(&mut project.settings);
        let plugins = crate::prepare_plugins(&project);
        let runtime = ManagedRuntime::start()?;
        let runtime_state = state::initialize(&project)?;
        // Play-mode sessions pack the atlases (sprite_atlases); an Edit-mode session can enter
        // Play mode (EditorApplication.EnterPlaymode) with the sprites it loaded unpacked.
        if options.edit_mode
            && let Some(atlases) = &runtime_state.play_mode_atlases
        {
            tracing::warn!("{}", atlases.edit_mode_message());
        }
        let normal_maps_xyz = project
            .settings
            .shader_defines(&options.target, crate::graphics_device::get())
            .contains(&"UNITY_NO_DXT5nm");
        state::set_build_target(
            &options.target,
            normal_maps_xyz,
            project.settings.lighting_encodings(&options.target),
        );
        if let Some(backend) = backend {
            state::set_backend(backend);
        }
        let config = crate::config::prepare(&project, &runtime, options, &plugins)?;
        let (start, frame, shutdown) = unsafe {
            (
                runtime.function::<StartFn>("Unbound.Host.Game, Unbound.Host", "Start")?,
                runtime.function::<FrameFn>("Unbound.Host.Game, Unbound.Host", "Frame")?,
                runtime.function::<ShutdownFn>("Unbound.Host.Game, Unbound.Host", "Shutdown")?,
            )
        };
        let api = Box::new(native_api::api());
        let config = serde_json::to_vec(&config)?;
        let began = Instant::now();
        let status = unsafe { start(&*api, config.as_ptr(), config.len() as i32) };
        if status != 0 {
            bail!("the game failed to start (see the log above)");
        }
        tracing::info!("game started in {:.2?}", began.elapsed());
        Ok(Self {
            runtime,
            options: options.clone(),
            project,
            watcher: None,
            frame,
            shutdown,
            _api: api,
            keys: [0; 64],
            stopped: false,
            taps: Vec::new(),
            typing: Vec::new(),
            script: Vec::new(),
            pointer: None,
            pointer_window: (0.0, 0.0),
            held: [0; 64],
            focus: None,
            fixed_delta: None,
            frames: 0,
            exit_code: None,
        })
    }

    /// Starts watching the project's sources: [`Game::apply_changes`] then picks up edits.
    pub fn watch(&mut self) -> Result<()> {
        let mut roots = vec![self.project.assets.clone()];
        for package in &self.project.packages {
            if matches!(
                package.source,
                unbound_project::PackageSource::Embedded | unbound_project::PackageSource::Local
            ) {
                roots.push(package.root.clone());
            }
        }
        self.watcher = Some(ProjectWatcher::new(&roots)?);
        tracing::info!(
            "watching {} for changes",
            roots
                .iter()
                .map(|r| r.display().to_string())
                .collect::<Vec<_>>()
                .join(", ")
        );
        Ok(())
    }

    /// Applies the edits made since the last call: scripts are recompiled and patched in.
    /// Call between frames.
    /// Also runs a compilation scripts asked for (without watching too).
    pub fn apply_changes(&mut self) {
        let requested = COMPILE_REQUESTED.swap(false, Ordering::SeqCst);
        let changes = match self.watcher.as_mut() {
            Some(watcher) => watcher.poll(),
            None => Changes::default(),
        };
        if changes.is_empty() && !requested {
            return;
        }
        if let Err(error) = self.apply(&changes, requested) {
            tracing::error!("applying changes: {error:#}");
        }
    }

    /// `requested`: scripts asked for a compilation, which reloads them even when nothing changed.
    fn apply(&mut self, changes: &Changes, requested: bool) -> Result<()> {
        if !changes.scripts.is_empty() || changes.graph || requested {
            let started = Instant::now();
            apply_define_overrides(&mut self.project.settings);
            let graph = self
                .project
                .script_graph(self.options.mode, &self.options.target)?;
            // The same assemblies as the initial compile (editor ones in Editor Play mode).
            let editor = self.options.mode == unbound_compat::CompileMode::EditorPlayMode;
            let request = self.project.compile_request(
                &graph,
                self.options.mode,
                editor,
                self.options.code_optimization,
            );
            let response: Json = self.runtime.call(
                "hot-reload",
                &serde_json::json!({ "compile": request, "reload": requested }),
            )?;
            let status = response["status"].as_str().unwrap_or("");
            match status {
                "applied" => {
                    let changes: Vec<&str> = response["changes"]
                        .as_array()
                        .into_iter()
                        .flatten()
                        .filter_map(|c| c.as_str())
                        .collect();
                    tracing::info!(
                        "code patched in {:.0?} ({} member edits): {}",
                        started.elapsed(),
                        changes.len(),
                        changes
                            .iter()
                            .take(6)
                            .copied()
                            .collect::<Vec<_>>()
                            .join(", ")
                    );
                }
                "unchanged" => {
                    tracing::info!("no code change to apply ({:.0?})", started.elapsed())
                }
                "compile-errors" => {
                    for assembly in response["compile"]["assemblies"]
                        .as_array()
                        .into_iter()
                        .flatten()
                    {
                        if assembly["status"] != "failed" {
                            continue;
                        }
                        for error in assembly["errors"].as_array().into_iter().flatten().take(10) {
                            tracing::error!(
                                "{}({},{}): {}: {}",
                                error["file"].as_str().unwrap_or(""),
                                error["line"],
                                error["column"],
                                error["id"].as_str().unwrap_or(""),
                                error["message"].as_str().unwrap_or("")
                            );
                        }
                    }
                    tracing::warn!(
                        "the game keeps running its previous code until the errors are fixed"
                    );
                }
                "restart-required" => {
                    tracing::warn!(
                        "this edit cannot be patched into the running game ({}); restarting it",
                        response["reason"].as_str().unwrap_or("")
                    );
                    // Release code is never patched; unless Release was asked for explicitly, the
                    // restarted session is Debug, so this is the only edit that restarts.
                    if response["optimized"].as_bool() == Some(true)
                        && std::env::var("UNBOUND_CODE_OPTIMIZATION").as_deref() != Ok("release")
                    {
                        tracing::info!(
                            "scripts are compiled in Debug from now on, so that later edits are patched in"
                        );
                        RESTART_IN_DEBUG.store(true, Ordering::SeqCst);
                    }
                    RESTART.store(true, Ordering::SeqCst);
                }
                "reload-deferred" => tracing::info!(
                    "the scripts reload when EditorApplication.UnlockReloadAssemblies is called ({})",
                    response["reason"].as_str().unwrap_or("")
                ),
                other => tracing::warn!("hot reload: {other}"),
            }
        }
        if !changes.content.is_empty() {
            let list: Vec<String> = changes
                .content
                .iter()
                .filter_map(|p| p.strip_prefix(&self.project.root).ok())
                .map(|p| p.display().to_string())
                .collect();
            tracing::info!("content changed: {}", list.join(", "));
        }
        Ok(())
    }

    /// Runs one frame; false when the game asked to quit.
    pub fn frame(&mut self, input: &FrameInput) -> bool {
        let frame = self.frames;
        self.frames += 1;
        let tap = self
            .taps
            .iter()
            .find(|(at, _, _)| frame >= *at && frame <= at + TAP_FRAMES)
            .copied();
        let mut scripted = *input;
        let window_pointer = (input.mouse_x, input.mouse_y);
        if self.pointer.is_some() && window_pointer != self.pointer_window {
            self.pointer = None;
        }
        if let Some(delta) = self.fixed_delta {
            if frame > 0 {
                scripted.real_delta_time = delta;
            }
            scripted.paced = 0;
            scripted.fixed_step = delta;
        }
        // The window's key events and text, then the script's.
        let mut events: Vec<KeyEvent> = if input.key_events.is_null() {
            Vec::new()
        } else {
            unsafe {
                std::slice::from_raw_parts(input.key_events, input.key_event_count.max(0) as usize)
            }
            .to_vec()
        };
        let mut text: Vec<u8> = if input.text.is_null() {
            Vec::new()
        } else {
            unsafe { std::slice::from_raw_parts(input.text, input.text_length.max(0) as usize) }
                .to_vec()
        };
        for (_, change) in self.script.iter().filter(|(at, _)| *at == frame) {
            match *change {
                ScriptedInput::Move(x, y) => {
                    self.pointer = Some((x, y));
                    self.pointer_window = window_pointer;
                }
                ScriptedInput::Scroll(x, y) => {
                    scripted.scroll_x += x;
                    scripted.scroll_y += y;
                }
                ScriptedInput::Motion(dx, dy) => {
                    let (x, y) = self.pointer.unwrap_or(window_pointer);
                    self.pointer = Some((x + dx, y + dy));
                    self.pointer_window = window_pointer;
                }
                ScriptedInput::Focus(focused) => self.focus = Some(focused),
                ScriptedInput::KeyboardConfiguration => {
                    events.push(KeyEvent::CONFIGURATION_CHANGED)
                }
                ScriptedInput::Key { code, down } => {
                    let (byte, bit) = (code as usize / 8, 1u8 << (code % 8));
                    if down {
                        self.held[byte] |= bit;
                    } else {
                        self.held[byte] &= !bit;
                    }
                    let start = text.len() as i32;
                    let shifted = [303u16, 304]
                        .iter()
                        .any(|&shift| self.held[shift as usize / 8] & (1 << (shift % 8)) != 0);
                    if down && let Some(c) = scripted::key_text(code) {
                        // Shift types capitals.
                        let c = if shifted { c.to_ascii_uppercase() } else { c };
                        text.extend_from_slice(c.to_string().as_bytes());
                    }
                    events.push(KeyEvent {
                        key: code as i32,
                        pressed: i32::from(down),
                        text_start: start,
                        text_length: text.len() as i32 - start,
                    });
                }
            }
        }
        if let Some((typed_events, typed)) = self
            .typing
            .iter()
            .find(|(at, _)| *at == frame)
            .map(|(_, text)| typing_events(text))
        {
            let offset = text.len() as i32;
            text.extend_from_slice(typed.as_bytes());
            events.extend(typed_events.into_iter().map(|e| KeyEvent {
                text_start: e.text_start + offset,
                ..e
            }));
        }
        if let Some((at, x, y)) = tap {
            // The release stays where the press was, or the UI would see no click; the pointer
            // stays there after, as a real one would.
            self.pointer = Some((x, y));
            self.pointer_window = window_pointer;
            if frame < at + TAP_FRAMES {
                scripted.mouse_buttons |= 1;
            }
        }
        if let Some((x, y)) = self.pointer {
            scripted.mouse_x = x;
            scripted.mouse_y = y;
            // The script moves the pointer: its moves are the motion.
            scripted.raw_motion = 0;
        }
        if let Some(focused) = self.focus {
            scripted.focused = i32::from(focused);
        }
        // Held keys and buttons join the window's.
        let mut keys = self.held;
        if !input.keys.is_null() {
            let window = unsafe { std::slice::from_raw_parts(input.keys, 64) };
            for (k, w) in keys.iter_mut().zip(window) {
                *k |= w;
            }
        }
        for button in 0..7u16 {
            let code = scripted::MOUSE0 + button;
            if self.held[code as usize / 8] & (1 << (code % 8)) != 0 {
                scripted.mouse_buttons |= 1 << button;
            }
        }
        scripted.keys = keys.as_ptr();
        scripted.key_events = events.as_ptr();
        scripted.key_event_count = events.len() as i32;
        scripted.text = text.as_ptr();
        scripted.text_length = text.len() as i32;
        let status = unsafe { (self.frame)(&mut scripted) };
        if status != 0 {
            // Bit 0: quit; the exit code above it.
            self.exit_code = Some(status >> 8);
        }
        status == 0
    }

    /// The exit code the game asked to quit with, once it has.
    pub fn exit_code(&self) -> Option<i32> {
        self.exit_code
    }

    /// Input changes to play at the given frames (pointer moves, key and button presses and
    /// releases, wheel turns): for scripted runs.
    pub fn script_input(&mut self, script: Vec<(u64, ScriptedInput)>) {
        self.script = script;
    }

    /// Taps (a left click held for a few frames) to play at the given frames: for scripted runs.
    pub fn script_taps(&mut self, taps: Vec<(u64, f32, f32)>) {
        self.taps = taps;
    }

    /// Text to type at the given frames, a key press and release per character: for scripted
    /// runs. `\n` presses Return and `\b` Backspace.
    pub fn script_typing(&mut self, typing: Vec<(u64, String)>) {
        self.typing = typing;
    }

    /// Advances game time by exactly `1 / fps` per frame, whatever the wall clock says, so
    /// frame-scripted runs see the same game at the same frame every time.
    pub fn fix_frame_rate(&mut self, fps: f64) {
        self.fixed_delta = Some(1.0 / fps);
    }

    pub fn shutdown(&mut self) {
        if !self.stopped {
            self.stopped = true;
            unsafe { (self.shutdown)() };
            crate::clipboard::release();
        }
    }

    /// Runs frames without a window: for tests and for checking that a game boots.
    pub fn run_headless(&mut self, frames: u32, width: i32, height: i32) {
        let started = Instant::now();
        let mut last = Instant::now();
        for i in 0..frames {
            let now = Instant::now();
            let dt = if i == 0 {
                0.0
            } else {
                (now - last).as_secs_f64().max(1.0 / 60.0)
            };
            last = now;
            let input = FrameInput {
                real_delta_time: dt,
                screen_width: width,
                screen_height: height,
                mouse_x: 0.0,
                mouse_y: 0.0,
                scroll_x: 0.0,
                scroll_y: 0.0,
                mouse_buttons: 0,
                focused: 1,
                keys: self.keys.as_ptr(),
                text: std::ptr::null(),
                text_length: 0,
                quit_requested: 0,
                key_events: std::ptr::null(),
                key_event_count: 0,
                paced: 0,
                raw_motion_x: 0.0,
                raw_motion_y: 0.0,
                raw_motion: 0,
                display_version: crate::display::version(),
                ime_version: crate::ime::version(),
                pointer_known: 0,
                fixed_step: 0.0,
            };
            self.apply_changes();
            if restart_requested() || !self.frame(&input) {
                break;
            }
            // While watching, frames keep real time (60 Hz) so edits land during the run.
            if self.watcher.is_some() {
                let spent = now.elapsed();
                let frame = std::time::Duration::from_secs_f64(1.0 / 60.0);
                if spent < frame {
                    std::thread::sleep(frame - spent);
                }
            }
        }
        tracing::info!("ran {} frames in {:.2?}", self.frames, started.elapsed());
        self.shutdown();
    }
}

impl Drop for Game {
    fn drop(&mut self) {
        self.shutdown();
    }
}

#[cfg(test)]
mod tests {
    use super::FrameInput;
    #[test]
    fn frame_layout_matches_managed_cached_input_versions() {
        assert_eq!(std::mem::size_of::<FrameInput>(), 128);
        assert_eq!(std::mem::offset_of!(FrameInput, display_version), 96);
        assert_eq!(std::mem::offset_of!(FrameInput, ime_version), 104);
        assert_eq!(std::mem::offset_of!(FrameInput, fixed_step), 112);
        assert_eq!(std::mem::offset_of!(FrameInput, pointer_known), 120);
    }
}
