//! The render backend the managed engine talks to. Once the GPU device is installed, a dedicated
//! render thread (`Unbound Render`, an Unbound design: the measured Editor renders on its main
//! thread, tests/projects/nativegfx) owns [`UnityRenderer`] and executes uploads and command
//! streams in strict FIFO order while the game thread builds the next frame (up to one frame in
//! flight). Synchronous readbacks and queries flush in order on the render thread. Native plugin
//! callbacks and native-texture interop borrow the renderer onto the game thread in FIFO order so
//! plugin thread identity, OpenGL context ownership and log deferral are as before; once a native
//! plugin works with the device, every upload and command batch runs that way (see
//! [`GpuBackend`]). A panic on
//! the render thread stops the process. Calls made before the renderer exists wait in order until
//! [`GpuBackend::install`]; shader queries before then are answered by the backend's own resolver.
use crate::materials::Resolver;
use crate::render::executor::{UnityRenderer, Upload};
use crate::shaderlab::ShaderIndex;
use std::collections::VecDeque;
use std::path::PathBuf;
use std::sync::mpsc::{self, Receiver, SyncSender};
use std::sync::{Arc, Condvar, Mutex};
use std::thread::JoinHandle;
use unbound_runtime::render_protocol::{
    RenderBackend, ShaderWarmupRequest, TextureReadOptions, commands::Command, owned,
};

/// Maximum completed frames (`Command::EndFrame`) allowed to be queued ahead of the render thread.
/// One frame in flight overlaps managed frame building with GPU command encoding/submission
/// without growing latency or command-buffer memory unboundedly.
const MAX_IN_FLIGHT_FRAMES: usize = 1;

enum Pending {
    Upload(Upload),
    Commands(Vec<Command>),
    Warmup(ShaderWarmupRequest),
}

/// What the render thread drives: the renderer (a stand-in in tests).
trait Rendering: Send + 'static {
    fn apply(&mut self, upload: Upload);
    fn execute(&mut self, commands: Vec<Command>);
    fn shader_warmup(&mut self, request: ShaderWarmupRequest) -> Result<String, String>;
}

impl Rendering for UnityRenderer {
    fn apply(&mut self, upload: Upload) {
        UnityRenderer::apply(self, upload);
    }

    fn execute(&mut self, commands: Vec<Command>) {
        UnityRenderer::execute(self, commands);
    }

    fn shader_warmup(&mut self, request: ShaderWarmupRequest) -> Result<String, String> {
        UnityRenderer::shader_warmup(self, request)
    }
}

enum RenderMsg<R> {
    Upload(Upload),
    Commands {
        commands: Vec<Command>,
        end_frames: usize,
    },
    Call(Box<dyn FnOnce(&mut R) + Send>),
    /// Temporarily lends `UnityRenderer` to the caller thread (in strict FIFO order after all
    /// prior messages finish) so native plugin callbacks and GL interop calls run on the game
    /// thread with Unity's exact thread identity and OpenGL context ownership.
    Lend {
        take_tx: SyncSender<R>,
        return_rx: Receiver<R>,
    },
    Flush(SyncSender<()>),
    Shutdown(SyncSender<()>),
}

struct QueueState<R> {
    items: VecDeque<RenderMsg<R>>,
    in_flight_frames: usize,
    closed: bool,
}

struct RenderQueue<R> {
    state: Mutex<QueueState<R>>,
    has_work: Condvar,
    frame_slot: Condvar,
}

impl<R> RenderQueue<R> {
    fn new() -> Arc<Self> {
        Arc::new(Self {
            state: Mutex::new(QueueState {
                items: VecDeque::new(),
                in_flight_frames: 0,
                closed: false,
            }),
            has_work: Condvar::new(),
            frame_slot: Condvar::new(),
        })
    }

    fn push(&self, msg: RenderMsg<R>) -> bool {
        let mut q = self.state.lock().unwrap_or_else(|e| e.into_inner());
        if q.closed {
            return false;
        }
        q.items.push_back(msg);
        self.has_work.notify_one();
        true
    }

    fn push_commands(&self, commands: Vec<Command>, end_frames: usize) -> bool {
        let mut q = self.state.lock().unwrap_or_else(|e| e.into_inner());
        if end_frames > 0 {
            while q.in_flight_frames >= MAX_IN_FLIGHT_FRAMES && !q.closed {
                q = self.frame_slot.wait(q).unwrap_or_else(|e| e.into_inner());
            }
        }
        if q.closed {
            return false;
        }
        q.in_flight_frames += end_frames;
        q.items.push_back(RenderMsg::Commands {
            commands,
            end_frames,
        });
        self.has_work.notify_one();
        true
    }

    fn complete_frames(&self, count: usize) {
        if count == 0 {
            return;
        }
        let mut q = self.state.lock().unwrap_or_else(|e| e.into_inner());
        q.in_flight_frames = q.in_flight_frames.saturating_sub(count);
        self.frame_slot.notify_all();
    }

    fn pop(&self) -> Option<RenderMsg<R>> {
        let mut q = self.state.lock().unwrap_or_else(|e| e.into_inner());
        loop {
            if let Some(msg) = q.items.pop_front() {
                return Some(msg);
            }
            if q.closed {
                return None;
            }
            q = self.has_work.wait(q).unwrap_or_else(|e| e.into_inner());
        }
    }

    fn close(&self) {
        let mut q = self.state.lock().unwrap_or_else(|e| e.into_inner());
        q.closed = true;
        // Drop any queued sync senders so callers waiting on replies unblock immediately.
        q.items.clear();
        self.has_work.notify_all();
        self.frame_slot.notify_all();
    }
}

/// Ensures the render queue is closed and waiting callers unblock even if the render thread
/// unwinds on a panic.
struct CloseOnDrop<R>(Arc<RenderQueue<R>>);

impl<R> Drop for CloseOnDrop<R> {
    fn drop(&mut self) {
        self.0.close();
    }
}

struct Worker<R = UnityRenderer> {
    queue: Arc<RenderQueue<R>>,
    join: Mutex<Option<JoinHandle<()>>>,
}

impl<R: Rendering> Worker<R> {
    fn spawn(renderer: R) -> Arc<Self> {
        let queue = RenderQueue::new();
        let worker_queue = Arc::clone(&queue);
        let handle = std::thread::Builder::new()
            .name("Unbound Render".into())
            .spawn(move || {
                let _close_on_exit = CloseOnDrop(Arc::clone(&worker_queue));
                // A renderer that panics leaves nothing drawn from then on: stop the process
                // loudly, as a panic on the game thread did before the render thread existed.
                let run = std::panic::AssertUnwindSafe(|| Self::serve(&worker_queue, renderer));
                if let Err(panic) = std::panic::catch_unwind(run) {
                    let message = panic
                        .downcast_ref::<&str>()
                        .map(|s| s.to_string())
                        .or_else(|| panic.downcast_ref::<String>().cloned())
                        .unwrap_or_default();
                    tracing::error!("the render thread panicked: {message}");
                    eprintln!("Unbound: the render thread panicked: {message}");
                    std::process::abort();
                }
            })
            .expect("failed to spawn Unbound Render thread");
        Arc::new(Self {
            queue,
            join: Mutex::new(Some(handle)),
        })
    }

    /// The render thread's loop: the queue's messages in order until it closes or shuts down.
    fn serve(worker_queue: &RenderQueue<R>, mut renderer: R) {
        while let Some(msg) = worker_queue.pop() {
            match msg {
                RenderMsg::Upload(u) => renderer.apply(u),
                RenderMsg::Commands {
                    commands,
                    end_frames,
                } => {
                    // Plugin callbacks among them (multithreaded rendering) log after the batch.
                    unbound_runtime::plugin_api::while_rendering(|| renderer.execute(commands));
                    worker_queue.complete_frames(end_frames);
                }
                RenderMsg::Call(op) => op(&mut renderer),
                RenderMsg::Lend { take_tx, return_rx } => {
                    if take_tx.send(renderer).is_err() {
                        return;
                    }
                    match return_rx.recv() {
                        Ok(returned) => renderer = returned,
                        Err(_) => return,
                    }
                }
                RenderMsg::Flush(reply) => {
                    let _ = reply.send(());
                }
                RenderMsg::Shutdown(reply) => {
                    drop(renderer);
                    worker_queue.close();
                    let _ = reply.send(());
                    return;
                }
            }
        }
        drop(renderer);
    }

    fn call<T: Send + 'static>(&self, op: impl FnOnce(&mut R) -> T + Send + 'static) -> Option<T> {
        let (tx, rx) = mpsc::sync_channel(1);
        if !self.queue.push(RenderMsg::Call(Box::new(move |r| {
            let _ = tx.send(op(r));
        }))) {
            return None;
        }
        rx.recv().ok()
    }

    /// Runs `op` on the caller thread after every previously queued upload and command has
    /// completed on the render thread, then returns `UnityRenderer` to the render thread (even
    /// if `op` unwinds).
    fn with_renderer_on_caller<T>(&self, op: impl FnOnce(&mut R) -> T) -> Option<T> {
        let (take_tx, take_rx) = mpsc::sync_channel(1);
        let (return_tx, return_rx) = mpsc::sync_channel(1);
        if !self.queue.push(RenderMsg::Lend { take_tx, return_rx }) {
            return None;
        }
        let renderer = take_rx.recv().ok()?;
        struct ReturnOnDrop<R> {
            renderer: Option<R>,
            return_tx: SyncSender<R>,
        }
        impl<R> Drop for ReturnOnDrop<R> {
            fn drop(&mut self) {
                if let Some(renderer) = self.renderer.take() {
                    let _ = self.return_tx.send(renderer);
                }
            }
        }
        let mut guard = ReturnOnDrop {
            renderer: Some(renderer),
            return_tx,
        };
        Some(op(guard.renderer.as_mut().unwrap()))
    }

    fn flush(&self) {
        let (tx, rx) = mpsc::sync_channel(1);
        if self.queue.push(RenderMsg::Flush(tx)) {
            let _ = rx.recv();
        }
    }

    fn shutdown(&self) {
        let handle = self.join.lock().unwrap_or_else(|e| e.into_inner()).take();
        let Some(handle) = handle else {
            return;
        };
        let (tx, rx) = mpsc::sync_channel(1);
        if self.queue.push(RenderMsg::Shutdown(tx)) {
            let _ = rx.recv();
        } else {
            self.queue.close();
        }
        let _ = handle.join();
    }
}

struct Inner<R = UnityRenderer> {
    worker: Option<Arc<Worker<R>>>,
    pending: Vec<Pending>,
    /// Where `.shader` files are, for the resolver.
    shader_roots: Vec<PathBuf>,
    /// The shader resolver before the renderer takes it, made on the first shader query (one
    /// made before the GPU is up, e.g. a render pipeline looking up its blit passes when it is
    /// created in the first frame's scripts).
    resolver: Option<Resolver>,
}

impl<R> Default for Inner<R> {
    fn default() -> Self {
        Self {
            worker: None,
            pending: Vec::new(),
            shader_roots: Vec::new(),
            resolver: None,
        }
    }
}

impl<R> Inner<R> {
    fn resolver(&mut self) -> &mut Resolver {
        let roots = &self.shader_roots;
        self.resolver.get_or_insert_with(|| {
            let index = ShaderIndex::scan(roots);
            if !roots.is_empty() {
                tracing::info!("indexed {} shaders", index.len());
            }
            Resolver::new(index)
        })
    }
}

/// The backend of the engine's renderer (see the module's description and [`Backend`]).
#[derive(Default)]
pub struct GpuBackend(Backend<UnityRenderer>);

/// Where uploads, command batches and calls to the renderer run (a stand-in renderer in tests).
struct Backend<R: Rendering> {
    inner: Mutex<Inner<R>>,
    /// A native plugin works with the device (it loaded, a plugin event was issued, or a native
    /// texture pointer was handed out): from then on uploads and command batches run on the
    /// calling game thread (the renderer lent to it, in order), as they did before the render
    /// thread, so a plugin's own device work from scripts (OpenGL calls on its context, Vulkan
    /// queue submits, Metal encoders outside callbacks) is ordered with Unity's rendering.
    direct: std::sync::atomic::AtomicBool,
    /// Held while the first switch to `direct` waits for the queue, so that every caller that
    /// sees the switch starts after that wait.
    going_direct: Mutex<()>,
    /// Multithreaded rendering (`SystemInfo.renderingThreadingMode` MultiThreaded: Vulkan with
    /// `-force-gfx-mt`): plugin callbacks run on the render thread in the order they were
    /// issued, and nothing moves to the game thread (`direct` stays off); a native pointer waits
    /// for the render thread. Read from the run's mode at the first need.
    multithreaded: std::sync::OnceLock<bool>,
}

impl<R: Rendering> Default for Backend<R> {
    fn default() -> Self {
        Self {
            inner: Mutex::new(Inner::default()),
            direct: Default::default(),
            going_direct: Mutex::new(()),
            multithreaded: std::sync::OnceLock::new(),
        }
    }
}

impl<R: Rendering> Drop for Backend<R> {
    fn drop(&mut self) {
        let worker = self
            .inner
            .lock()
            .unwrap_or_else(|e| e.into_inner())
            .worker
            .take();
        if let Some(worker) = worker {
            worker.shutdown();
        }
    }
}

impl GpuBackend {
    pub fn new() -> Self {
        Self::default()
    }

    /// A backend whose shaders are under `shader_roots` (Assets and package roots, then Unity's
    /// built-in shaders).
    pub fn with_shader_roots(shader_roots: Vec<PathBuf>) -> Self {
        let backend = Self::default();
        backend.0.inner.lock().unwrap().shader_roots = shader_roots;
        backend
    }

    /// The shader resolver for the renderer (the one that answered queries so far), under the
    /// built-in pipeline as a new one is: the pipeline tag among the pending commands reaches it
    /// in order when they run. `shader_roots` are used when the backend was made without any.
    pub fn take_resolver(&self, shader_roots: &[PathBuf]) -> Resolver {
        let mut inner = self.0.inner.lock().unwrap();
        if inner.resolver.is_none() && inner.shader_roots.is_empty() {
            inner.shader_roots = shader_roots.to_vec();
        }
        inner.resolver();
        let mut resolver = inner.resolver.take().expect("made above");
        resolver.set_pipeline("");
        resolver
    }

    /// Hands the backend its renderer, drains what waited for it, and starts the render thread.
    pub fn install(&self, renderer: UnityRenderer) {
        unbound_runtime::plugin_api::while_rendering(|| self.0.install(renderer));
    }

    /// Waits until every upload and command submitted before this call has finished executing on
    /// the render thread (for frame-exact screenshots/snapshots, plugin context handoff, and
    /// clean shutdown).
    pub fn flush(&self) {
        self.0.flush();
    }
}

impl<R: Rendering> Backend<R> {
    /// Runs what waited for the renderer, in order, then starts the render thread with it.
    fn install(&self, mut renderer: R) {
        let mut inner = self.inner.lock().unwrap();
        for pending in std::mem::take(&mut inner.pending) {
            match pending {
                Pending::Upload(u) => renderer.apply(u),
                Pending::Commands(c) => renderer.execute(c),
                Pending::Warmup(request) => {
                    renderer
                        .shader_warmup(request)
                        .unwrap_or_else(|why| panic!("startup shader warm-up failed: {why}"));
                }
            }
        }
        inner.worker = Some(Worker::spawn(renderer));
    }

    fn worker(&self) -> Option<Arc<Worker<R>>> {
        self.inner.lock().unwrap().worker.clone()
    }

    fn flush(&self) {
        if let Some(worker) = self.worker() {
            worker.flush();
        }
    }

    fn upload(&self, upload: Upload) {
        let mut inner = self.inner.lock().unwrap();
        match &inner.worker {
            Some(worker) => {
                let worker = Arc::clone(worker);
                drop(inner);
                if self.is_direct() {
                    let _ = worker.with_renderer_on_caller(|r| r.apply(upload));
                } else {
                    worker.queue.push(RenderMsg::Upload(upload));
                }
            }
            None => inner.pending.push(Pending::Upload(upload)),
        }
    }

    /// A command batch: queued for the render thread, or run on the caller once direct (a batch
    /// with a plugin event makes it direct first).
    fn submit(&self, commands: Vec<Command>) {
        // A plugin event, also one waiting for the device: the plugin works with it from now on
        // (multithreaded: on the render thread, as the batch's other commands).
        if !self.multithreaded() && commands.iter().any(|c| matches!(c, Command::Plugin(_))) {
            self.go_direct();
        }
        let worker = {
            let mut inner = self.inner.lock().unwrap();
            match &inner.worker {
                Some(w) => Arc::clone(w),
                None => {
                    inner.pending.push(Pending::Commands(commands));
                    return;
                }
            }
        };
        if self.is_direct() {
            unbound_runtime::plugin_api::while_rendering(|| {
                let _ = worker.with_renderer_on_caller(|r| r.execute(commands));
            });
        } else {
            let end_frames = commands
                .iter()
                .filter(|c| matches!(c, Command::EndFrame))
                .count();
            worker.queue.push_commands(commands, end_frames);
        }
    }

    /// Native device work on the renderer (a native pointer handed to a plugin): on the caller,
    /// direct from now on. None without a renderer or when it stopped.
    fn lend<T>(&self, op: impl FnOnce(&mut R) -> T) -> Option<T> {
        let worker = self.worker()?;
        // Multithreaded, a native pointer only waits for the render thread (Unity 6000.0
        // Texture.GetNativeTexturePtr: "will synchronize with the rendering thread").
        if !self.multithreaded() {
            self.go_direct();
        }
        worker.with_renderer_on_caller(op)
    }

    /// A plugin loading, queried or shutting down: rendering runs on the game thread from now
    /// on, and on OpenGL Core the work runs with the plugins' context current.
    fn plugin_context(&self, work: &mut dyn FnMut()) {
        if self.multithreaded() {
            // The callbacks already issued run before the plugin's own work.
            self.flush();
        } else {
            self.go_direct();
        }
        // Only OpenGL Core has a context of the plugins' to make current (the other devices'
        // plugins use the device's own interface): the work alone elsewhere.
        if unbound_runtime::graphics_device::get() != unbound_compat::GraphicsDevice::OpenGLCore {
            return work();
        }
        // Flush any queued texture/target uploads before the plugin inspects the GL share group.
        self.flush();
        // The renderer is not held meanwhile: the plugin may call back into the engine.
        let mut ran = false;
        let _ = crate::render::gl_interop::with(|_| {
            ran = true;
            work();
        });
        if !ran {
            work();
        }
    }

    fn is_direct(&self) -> bool {
        self.direct.load(std::sync::atomic::Ordering::Acquire)
    }

    fn multithreaded(&self) -> bool {
        *self.multithreaded.get_or_init(|| {
            unbound_runtime::graphics_device::forced_threading()
                == Some(unbound_runtime::graphics_device::RenderingThreadingMode::MultiThreaded)
        })
    }

    /// Runs everything on the calling game thread from now on (see `direct`), once what was
    /// queued before has finished.
    fn go_direct(&self) {
        if self.is_direct() {
            return;
        }
        let _switching = self.going_direct.lock().unwrap_or_else(|e| e.into_inner());
        if !self.is_direct() {
            self.flush();
            self.direct
                .store(true, std::sync::atomic::Ordering::Release);
        }
    }
}

impl RenderBackend for GpuBackend {
    fn upload_mesh(&self, handle: u64, mesh: owned::Mesh) {
        self.0.upload(Upload::UploadMesh(handle, mesh));
    }

    fn release_mesh(&self, handle: u64) {
        self.0.upload(Upload::ReleaseMesh(handle));
    }

    fn upload_texture(&self, handle: u64, texture: owned::Texture) {
        self.0.upload(Upload::UploadTexture(handle, texture));
    }

    fn release_texture(&self, handle: u64) {
        self.0.upload(Upload::ReleaseTexture(handle));
    }

    fn texture_mip_bias(&self, handle: u64, bias: f32) {
        self.0.upload(Upload::TextureMipBias(handle, bias));
    }

    fn texture_sampler(&self, handle: u64, properties: [i32; 5]) {
        self.0.upload(Upload::TextureSampler(handle, properties));
    }

    fn update_material(&self, handle: u64, material: owned::Material) {
        self.0.upload(Upload::UpdateMaterial(handle, material));
    }

    fn release_material(&self, handle: u64) {
        self.0.upload(Upload::ReleaseMaterial(handle));
    }

    fn submit_commands(&self, commands: Vec<Command>) {
        // The game thread keeps the plugins' OpenGL context current between script calls.
        crate::render::gl_interop::mark_render_thread();
        self.0.submit(commands);
    }

    fn read_buffer(&self, buffer: u64, offset: u64, size: u64) -> Option<Vec<u8>> {
        let worker = self.0.inner.lock().unwrap().worker.clone()?;
        worker.call(move |r| r.read_buffer(buffer, offset, size))?
    }

    fn graphics_fence(&self, insert: bool) -> Result<u64, String> {
        let worker = self
            .0
            .inner
            .lock()
            .unwrap()
            .worker
            .clone()
            .ok_or_else(|| "the renderer is not up yet".to_string())?;
        worker
            .call(move |r| r.graphics_fence(insert))
            .ok_or_else(|| "the renderer has stopped".to_string())?
    }

    fn shader_passes(&self, shader: &str) -> Result<String, String> {
        let worker = {
            let mut inner = self.0.inner.lock().unwrap();
            if let Some(w) = &inner.worker {
                Some(Arc::clone(w))
            } else {
                if inner.shader_roots.is_empty() {
                    return Err("the renderer is not up yet".into());
                }
                // The pipeline the waiting commands leave active, as the renderer will see it.
                let tag = inner
                    .pending
                    .iter()
                    .rev()
                    .filter_map(|p| match p {
                        Pending::Commands(commands) => {
                            commands.iter().rev().find_map(|c| match c {
                                Command::RenderPipeline(tag) => Some(tag.as_str()),
                                _ => None,
                            })
                        }
                        Pending::Upload(_) | Pending::Warmup(_) => None,
                    })
                    .next()
                    .unwrap_or("")
                    .to_string();
                let resolver = inner.resolver();
                resolver.set_pipeline(&tag);
                return Ok(resolver.describe(shader));
            }
        };
        let shader = shader.to_string();
        worker
            .unwrap()
            .call(move |r| r.shader_passes(&shader))
            .ok_or_else(|| "the renderer has stopped".to_string())
    }

    fn shader_warmup(&self, request: ShaderWarmupRequest) -> Result<String, String> {
        let worker = {
            let mut inner = self.0.inner.lock().unwrap();
            if let Some(w) = &inner.worker {
                Some(Arc::clone(w))
            } else {
                if request.stats {
                    return Err("shader statistics require an active renderer".into());
                }
                // Like uploads and draws, preparation waits for the device and keeps submission order.
                inner.pending.push(Pending::Warmup(request));
                return Ok("{}".into());
            }
        };
        worker
            .unwrap()
            .call(move |r| r.shader_warmup(request))
            .ok_or_else(|| "the renderer has stopped".to_string())?
    }

    fn kernel_threads(
        &self,
        file: &str,
        kernel: &str,
        keywords: &[String],
    ) -> Result<[u32; 3], String> {
        let worker = self
            .0
            .inner
            .lock()
            .unwrap()
            .worker
            .clone()
            .ok_or_else(|| "the renderer is not up yet".to_string())?;
        let file = file.to_string();
        let kernel = kernel.to_string();
        let keywords = keywords.to_vec();
        worker
            .call(move |r| r.kernel_threads(&file, &kernel, &keywords))
            .ok_or_else(|| "the renderer has stopped".to_string())?
    }

    fn with_plugin_context(&self, work: &mut dyn FnMut()) {
        self.0.plugin_context(work);
    }

    fn native_texture(&self, texture: u64, depth: bool) -> u64 {
        if texture == 0 {
            // No texture (one not created): the query only waits for the render thread.
            return self.0.lend(|_| 0).unwrap_or(0);
        }
        // A native pointer is for a plugin's own device work.
        self.0
            .lend(|r| r.native_texture(texture, depth))
            .unwrap_or(0)
    }

    #[cfg(target_os = "macos")]
    fn render_buffer_texture(&self, texture: u64, image: u8) -> u64 {
        self.0
            .lend(|r| r.metal_render_buffer_texture(texture, image))
            .unwrap_or(0)
    }

    fn external_texture(
        &self,
        texture: u64,
        native: u64,
        info: &unbound_runtime::render_protocol::ExternalTextureInfo,
    ) -> Result<(), String> {
        if self.0.worker().is_none() {
            return Err("the renderer is not up yet".to_string());
        }
        self.0
            .lend(|r| r.external_texture(texture, native, info))
            .ok_or_else(|| "the renderer has stopped".to_string())?
    }

    fn read_pixels(&self, texture: u64, rect: [i32; 4], float: bool) -> Option<Vec<u8>> {
        let worker = self.0.inner.lock().unwrap().worker.clone()?;
        worker.call(move |r| r.read_pixels(texture, rect, float))?
    }

    fn read_texture(
        &self,
        texture: u64,
        mip: u32,
        layer: u32,
        rect: [i32; 4],
        options: TextureReadOptions,
    ) -> Option<Vec<u8>> {
        let worker = self.0.inner.lock().unwrap().worker.clone()?;
        worker.call(move |r| r.read_texture(texture, mip, layer, rect, options))?
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    const SHADER: &str = r#"Shader "Test/Pipelines" {
SubShader {
    Tags { "RenderPipeline" = "UniversalPipeline" }
    Pass { Name "First" }
    Pass { Name "Second" }
}
SubShader {
    Pass { Name "Builtin" }
}
}"#;

    fn names(json: &str) -> Vec<String> {
        let value: serde_json::Value = serde_json::from_str(json).unwrap();
        value["passes"]
            .as_array()
            .unwrap()
            .iter()
            .map(|p| p["name"].as_str().unwrap().to_string())
            .collect()
    }

    #[test]
    fn startup_warmup_keeps_command_order_and_does_not_fake_statistics() {
        let backend = GpuBackend::new();
        backend.submit_commands(vec![Command::ColorSpace(1)]);
        backend
            .shader_warmup(ShaderWarmupRequest {
                shader: "Test/Warm".into(),
                passes: vec![0],
                ..Default::default()
            })
            .unwrap();
        assert!(
            backend
                .shader_warmup(ShaderWarmupRequest {
                    stats: true,
                    ..Default::default()
                })
                .is_err()
        );
        backend.submit_commands(vec![Command::ColorSpace(0)]);
        let inner = backend.0.inner.lock().unwrap();
        assert!(matches!(
            &inner.pending[..],
            [
                Pending::Commands(_),
                Pending::Warmup(_),
                Pending::Commands(_)
            ]
        ));
    }

    #[test]
    fn shader_queries_are_answered_before_the_renderer_is_up() {
        let dir = std::env::temp_dir().join(format!("unbound-backend-{}", std::process::id()));
        std::fs::create_dir_all(&dir).unwrap();
        std::fs::write(dir.join("s.shader"), SHADER).unwrap();
        let backend = GpuBackend::with_shader_roots(vec![dir.clone()]);
        // The built-in pipeline's SubShader, then the one the waiting pipeline tag selects.
        assert_eq!(
            names(&backend.shader_passes("Test/Pipelines").unwrap()),
            ["Builtin"]
        );
        backend.submit_commands(vec![Command::RenderPipeline("UniversalPipeline".into())]);
        assert_eq!(
            names(&backend.shader_passes("Test/Pipelines").unwrap()),
            ["First", "Second"]
        );
        // The renderer takes the same resolver back under the built-in pipeline: the waiting
        // tag reaches it in order.
        let mut resolver = backend.take_resolver(&[]);
        assert_eq!(names(&resolver.describe("Test/Pipelines")), ["Builtin"]);
        // Without shader roots nothing can answer before the renderer.
        assert!(GpuBackend::new().shader_passes("Test/Pipelines").is_err());
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn render_queue_backpressures_second_end_frame_until_first_completes() {
        use std::sync::atomic::{AtomicBool, Ordering};
        use std::time::Duration;

        let queue = RenderQueue::<UnityRenderer>::new();
        // Frame 1 Enqueues immediately (0 -> 1 in flight) and mid-frame chunks do not block.
        assert!(queue.push_commands(vec![Command::ColorSpace(1)], 0));
        assert!(queue.push_commands(vec![Command::EndFrame], 1));

        let pushed_second = Arc::new(AtomicBool::new(false));
        let flag = Arc::clone(&pushed_second);
        let q2 = Arc::clone(&queue);
        let producer = std::thread::spawn(move || {
            assert!(q2.push_commands(vec![Command::EndFrame], 1));
            flag.store(true, Ordering::SeqCst);
        });

        // Give the producer a moment to hit the backpressure wait.
        std::thread::sleep(Duration::from_millis(25));
        assert!(!pushed_second.load(Ordering::SeqCst));

        // FIFO: first item is ColorSpace(1) (0 end frames), second is EndFrame (1 end frame).
        let first = queue.pop().unwrap();
        assert!(matches!(first, RenderMsg::Commands { end_frames: 0, .. }));
        assert!(!pushed_second.load(Ordering::SeqCst));

        let second = queue.pop().unwrap();
        assert!(matches!(second, RenderMsg::Commands { end_frames: 1, .. }));
        queue.complete_frames(1);

        producer.join().unwrap();
        assert!(pushed_second.load(Ordering::SeqCst));

        let third = queue.pop().unwrap();
        assert!(matches!(third, RenderMsg::Commands { end_frames: 1, .. }));
        queue.complete_frames(1);
        queue.close();
        assert!(queue.pop().is_none());
    }

    #[test]
    fn render_queue_close_unblocks_waiting_end_frame_and_lend_waiter() {
        use std::time::Duration;

        let queue = RenderQueue::<UnityRenderer>::new();
        // Queue a lend request and an in-flight frame, then close (simulating worker unwind).
        let (take_tx, take_rx) = mpsc::sync_channel(1);
        let (_return_tx, return_rx) = mpsc::sync_channel(1);
        assert!(queue.push(RenderMsg::Lend { take_tx, return_rx }));
        assert!(queue.push_commands(vec![Command::EndFrame], 1));

        let q2 = Arc::clone(&queue);
        let blocked_producer =
            std::thread::spawn(move || q2.push_commands(vec![Command::EndFrame], 1));

        std::thread::sleep(Duration::from_millis(25));
        queue.close();

        // Both the queued lend receiver and the backpressured EndFrame producer unblock immediately.
        assert!(take_rx.recv().is_err());
        assert!(!blocked_producer.join().unwrap());
        let (take_tx2, _take_rx2) = mpsc::sync_channel(1);
        let (_return_tx2, return_rx2) = mpsc::sync_channel(1);
        assert!(!queue.push(RenderMsg::Lend {
            take_tx: take_tx2,
            return_rx: return_rx2,
        }));
    }

    /// A stand-in renderer: what ran, in order, and on which thread; `ColorSpace(99)` panics.
    struct Recorder(Arc<Mutex<Vec<(String, std::thread::ThreadId)>>>);

    impl Rendering for Recorder {
        fn apply(&mut self, _upload: Upload) {
            self.0
                .lock()
                .unwrap()
                .push(("upload".into(), std::thread::current().id()));
        }

        fn execute(&mut self, commands: Vec<Command>) {
            for command in commands {
                if let Command::ColorSpace(n) = command {
                    assert_ne!(n, 99, "test panic on the render thread");
                    self.0
                        .lock()
                        .unwrap()
                        .push((format!("commands {n}"), std::thread::current().id()));
                }
            }
        }

        fn shader_warmup(&mut self, _request: ShaderWarmupRequest) -> Result<String, String> {
            self.0
                .lock()
                .unwrap()
                .push(("warmup".into(), std::thread::current().id()));
            Ok("{}".into())
        }
    }

    type Log = Arc<Mutex<Vec<(String, std::thread::ThreadId)>>>;

    fn recording() -> (Backend<Recorder>, Log) {
        let log = Log::default();
        let backend = Backend::default();
        backend.install(Recorder(Arc::clone(&log)));
        (backend, log)
    }

    fn plugin_event() -> Command {
        Command::Plugin(unbound_runtime::render_commands::PluginCall::Event { callback: 0, id: 0 })
    }

    /// What ran, in order, and whether on the calling thread (else on the render thread).
    fn ran(log: &Log) -> Vec<(String, bool)> {
        let caller = std::thread::current().id();
        let log = log.lock().unwrap();
        log.iter()
            .map(|(name, thread)| (name.clone(), *thread == caller))
            .collect()
    }

    fn on(entries: &[(&str, bool)]) -> Vec<(String, bool)> {
        entries
            .iter()
            .map(|(name, caller)| (name.to_string(), *caller))
            .collect()
    }

    /// Uploads and batches run on the render thread until a batch with a plugin event; that
    /// batch and everything after it run on the calling thread, in order.
    #[test]
    fn a_plugin_event_moves_rendering_to_the_caller() {
        let (backend, log) = recording();
        backend.upload(Upload::ReleaseMesh(1));
        backend.submit(vec![Command::ColorSpace(1)]);
        backend.submit(vec![plugin_event(), Command::ColorSpace(2)]);
        backend.upload(Upload::ReleaseMesh(2));
        backend.submit(vec![Command::ColorSpace(3)]);
        backend.flush();
        assert_eq!(
            ran(&log),
            on(&[
                ("upload", false),
                ("commands 1", false),
                ("commands 2", true),
                ("upload", true),
                ("commands 3", true),
            ])
        );
    }

    /// A native pointer handed out (native texture, external texture, render buffer) moves
    /// rendering to the caller as well.
    #[test]
    fn a_native_pointer_moves_rendering_to_the_caller() {
        let (backend, log) = recording();
        backend.submit(vec![Command::ColorSpace(1)]);
        assert_eq!(backend.lend(|_| 7), Some(7));
        backend.upload(Upload::ReleaseMesh(1));
        backend.submit(vec![Command::ColorSpace(2)]);
        backend.flush();
        assert_eq!(
            ran(&log),
            on(&[
                ("commands 1", false),
                ("upload", true),
                ("commands 2", true)
            ])
        );
    }

    /// A plugin's load, query or shutdown moves rendering to the caller.
    #[test]
    fn a_plugin_context_moves_rendering_to_the_caller() {
        let (backend, log) = recording();
        backend.submit(vec![Command::ColorSpace(1)]);
        // The plugin's work starts after what was queued before it has run.
        let mut seen = Vec::new();
        // On OpenGL Core the plugin context goes through gl_interop::with.
        let pending = crate::render::gl_interop::PENDING_TESTS
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner());
        backend.plugin_context(&mut || seen = ran(&log));
        drop(pending);
        assert_eq!(seen, on(&[("commands 1", false)]));
        backend.submit(vec![Command::ColorSpace(2)]);
        backend.flush();
        assert_eq!(
            ran(&log),
            on(&[("commands 1", false), ("commands 2", true)])
        );
    }

    /// A plugin event issued before the renderer exists: everything after the renderer is
    /// installed runs on the caller (what waited runs where the renderer is installed).
    #[test]
    fn a_plugin_event_before_the_renderer_moves_its_rendering_to_the_caller() {
        let log = Log::default();
        let backend = Backend::default();
        backend.submit(vec![plugin_event(), Command::ColorSpace(1)]);
        backend.install(Recorder(Arc::clone(&log)));
        backend.upload(Upload::ReleaseMesh(1));
        backend.submit(vec![Command::ColorSpace(2)]);
        backend.flush();
        assert_eq!(
            ran(&log),
            on(&[("commands 1", true), ("upload", true), ("commands 2", true)])
        );
    }

    /// Multithreaded rendering: plugin events run on the render thread with the batches around
    /// them, in order, and neither a native pointer nor a plugin context moves rendering to the
    /// caller (a native pointer runs on the caller after what was queued).
    #[test]
    fn multithreaded_rendering_keeps_plugin_work_on_the_render_thread() {
        let log = Log::default();
        let backend = Backend::default();
        backend.multithreaded.set(true).unwrap();
        backend.install(Recorder(Arc::clone(&log)));
        backend.submit(vec![Command::ColorSpace(1)]);
        backend.submit(vec![plugin_event(), Command::ColorSpace(2)]);
        backend.upload(Upload::ReleaseMesh(1));
        let mut seen = Vec::new();
        // On OpenGL Core the plugin context goes through gl_interop::with.
        let pending = crate::render::gl_interop::PENDING_TESTS
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner());
        backend.plugin_context(&mut || seen = ran(&log));
        drop(pending);
        assert_eq!(
            seen,
            on(&[
                ("commands 1", false),
                ("commands 2", false),
                ("upload", false)
            ])
        );
        assert_eq!(
            backend.lend(|r| r.execute(vec![Command::ColorSpace(3)])),
            Some(())
        );
        backend.submit(vec![Command::ColorSpace(4)]);
        backend.flush();
        assert_eq!(
            ran(&log),
            on(&[
                ("commands 1", false),
                ("commands 2", false),
                ("upload", false),
                ("commands 3", true),
                ("commands 4", false),
            ])
        );
    }

    /// Without any plugin work everything stays on the render thread.
    #[test]
    fn without_plugins_rendering_stays_on_the_render_thread() {
        let (backend, log) = recording();
        backend.upload(Upload::ReleaseMesh(1));
        backend.submit(vec![Command::ColorSpace(1)]);
        backend.flush();
        assert_eq!(ran(&log), on(&[("upload", false), ("commands 1", false)]));
    }

    /// A lent renderer runs the caller's work on the calling thread, after everything queued
    /// before it ran on the render thread, and goes back to the render thread afterwards.
    #[test]
    fn a_lent_renderer_runs_on_the_caller_after_what_was_queued() {
        let log = Arc::new(Mutex::new(Vec::new()));
        let worker = Worker::spawn(Recorder(Arc::clone(&log)));
        assert!(worker.queue.push(RenderMsg::Upload(Upload::ReleaseMesh(1))));
        assert!(worker.queue.push_commands(vec![Command::ColorSpace(1)], 0));
        let caller = std::thread::current().id();
        worker
            .with_renderer_on_caller(|r| r.execute(vec![Command::ColorSpace(2)]))
            .unwrap();
        assert!(worker.queue.push_commands(vec![Command::ColorSpace(3)], 0));
        worker.flush();
        let log = log.lock().unwrap().clone();
        let names: Vec<&str> = log.iter().map(|(name, _)| name.as_str()).collect();
        assert_eq!(names, ["upload", "commands 1", "commands 2", "commands 3"]);
        assert_ne!(log[0].1, caller);
        assert_eq!(log[2].1, caller);
        assert_eq!(log[3].1, log[0].1);
        worker.shutdown();
    }

    /// A panic on the render thread stops the process (it would otherwise go on without
    /// rendering). Run as a child process that must die.
    #[test]
    fn a_render_thread_panic_aborts_the_process() {
        const CHILD: &str = "UNBOUND_RENDER_PANIC_CHILD";
        if std::env::var_os(CHILD).is_some() {
            let worker = Worker::spawn(Recorder(Arc::new(Mutex::new(Vec::new()))));
            worker.queue.push_commands(vec![Command::ColorSpace(99)], 0);
            worker.flush();
            // Not reached: the process aborts before the flush returns.
            std::process::exit(0);
        }
        let status = std::process::Command::new(std::env::current_exe().unwrap())
            .args([
                "--exact",
                "backend::tests::a_render_thread_panic_aborts_the_process",
                "--nocapture",
            ])
            .env(CHILD, "1")
            .stdout(std::process::Stdio::null())
            .stderr(std::process::Stdio::null())
            .status()
            .unwrap();
        assert!(
            !status.success(),
            "the child process went on after the panic"
        );
        #[cfg(unix)]
        {
            use std::os::unix::process::ExitStatusExt;
            assert_eq!(status.signal(), Some(6), "{status:?}");
        }
    }
}
