//! A running game: the managed engine's start, frame and shutdown entry points.
use crate::{
    config::GameOptions,
    native_api,
    render_protocol::RenderBackend,
    state,
    watch::{Changes, ProjectWatcher},
};
use anyhow::{Result, bail};
use serde_json::Value as Json;
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
    project: Project,
    options: GameOptions,
    watcher: Option<ProjectWatcher>,
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
        let project = Project::open(&project.root)?;
        let runtime = ManagedRuntime::start()?;
        state::initialize(&project)?;
        state::set_build_target(&options.target);
        if let Some(backend) = backend {
            state::set_backend(backend);
        }
        let config = crate::config::prepare(&project, &runtime, options)?;
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
    pub fn apply_changes(&mut self) {
        let Some(watcher) = self.watcher.as_mut() else {
            return;
        };
        let changes = watcher.poll();
        if changes.is_empty() {
            return;
        }
        if let Err(error) = self.apply(&changes) {
            tracing::error!("applying changes: {error:#}");
        }
    }

    fn apply(&mut self, changes: &Changes) -> Result<()> {
        if !changes.scripts.is_empty() || changes.graph {
            let started = Instant::now();
            let graph = self
                .project
                .script_graph(self.options.mode, &self.options.target)?;
            // The same assemblies as the initial compile (editor ones in Editor Play mode).
            let editor = self.options.mode == unbound_compat::CompileMode::EditorPlayMode;
            let request = self.project.compile_request(
                &graph,
                self.options.mode,
                &self.options.target,
                editor,
            );
            let response: Json = self
                .runtime
                .call("hot-reload", &serde_json::json!({ "compile": request }))?;
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
                        "this edit cannot be applied to the running game ({}); restart to see it",
                        response["reason"].as_str().unwrap_or("")
                    );
                }
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
            self.apply_changes();
            if !self.frame(&input) {
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
        tracing::info!("ran {frames} frames in {:.2?}", started.elapsed());
        self.shutdown();
    }
}

impl Drop for Game {
    fn drop(&mut self) {
        self.shutdown();
    }
}
