//! A running game: the managed engine's start, frame and shutdown entry points.
use crate::{config::GameOptions, native_api, render_protocol::RenderBackend, state};
use anyhow::{Result, bail};
use std::{sync::Arc, time::Instant};
use unbound_host::ManagedRuntime;
use unbound_project::Project;

/// Input sampled for one frame; the layout matches `FrameInput` in the managed engine.
#[repr(C)]
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
}

type StartFn = unsafe extern "system" fn(*const native_api::NativeApi, *const u8, i32) -> i32;
type FrameFn = unsafe extern "system" fn(*const FrameInput) -> i32;
type ShutdownFn = unsafe extern "system" fn();

pub struct Game {
    pub runtime: ManagedRuntime,
    frame: FrameFn,
    shutdown: ShutdownFn,
    /// The API table must stay where the managed side saw it.
    _api: Box<native_api::NativeApi>,
    pub keys: [u8; 64],
    stopped: bool,
}

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
        let runtime = ManagedRuntime::start()?;
        state::initialize(project)?;
        if let Some(backend) = backend {
            state::set_backend(backend);
        }
        let config = crate::config::prepare(project, &runtime, options)?;
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
            frame,
            shutdown,
            _api: api,
            keys: [0; 64],
            stopped: false,
        })
    }

    /// Runs one frame; false when the game asked to quit.
    pub fn frame(&mut self, input: &FrameInput) -> bool {
        let status = unsafe { (self.frame)(input) };
        status == 0
    }

    pub fn shutdown(&mut self) {
        if !self.stopped {
            self.stopped = true;
            unsafe { (self.shutdown)() };
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
            };
            if !self.frame(&input) {
                break;
            }
        }
        tracing::info!("ran {frames} frames in {:.2?}", started.elapsed());
        self.shutdown();
    }
}

impl Drop for Game {
    fn drop(&mut self) {
        self.shutdown();
    }
}
