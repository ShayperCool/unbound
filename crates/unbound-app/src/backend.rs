//! The render backend the managed engine talks to. Once the GPU device is installed, a dedicated
//! render thread (`Unbound Render`, an Unbound design: the measured Editor renders on its main
//! thread, tests/projects/nativegfx) owns [`UnityRenderer`] and executes uploads and command
//! streams in strict FIFO order while the game thread builds the next frame (up to one frame in
//! flight). Synchronous readbacks and queries flush in order on the render thread. Native plugin
//! callbacks and native-texture interop borrow the renderer onto the game thread in FIFO order so
//! plugin thread identity, OpenGL context ownership and log deferral are as before; once a native
//! rendering plugin is loaded, every command batch runs that way (see [`GpuBackend`]). A panic on
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
    RenderBackend, ShaderWarmupRequest, commands::Command, owned,
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

enum RenderMsg {
    Upload(Upload),
    Commands {
        commands: Vec<Command>,
        end_frames: usize,
    },
    Call(Box<dyn FnOnce(&mut UnityRenderer) + Send>),
    /// Temporarily lends `UnityRenderer` to the caller thread (in strict FIFO order after all
    /// prior messages finish) so native plugin callbacks and GL interop calls run on the game
    /// thread with Unity's exact thread identity and OpenGL context ownership.
    Lend {
        take_tx: SyncSender<UnityRenderer>,
        return_rx: Receiver<UnityRenderer>,
    },
    Flush(SyncSender<()>),
    Shutdown(SyncSender<()>),
}

#[derive(Default)]
struct QueueState {
    items: VecDeque<RenderMsg>,
    in_flight_frames: usize,
    closed: bool,
}

struct RenderQueue {
    state: Mutex<QueueState>,
    has_work: Condvar,
    frame_slot: Condvar,
}

impl RenderQueue {
    fn new() -> Arc<Self> {
        Arc::new(Self {
            state: Mutex::new(QueueState::default()),
            has_work: Condvar::new(),
            frame_slot: Condvar::new(),
        })
    }

    fn push(&self, msg: RenderMsg) -> bool {
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

    fn pop(&self) -> Option<RenderMsg> {
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
struct CloseOnDrop(Arc<RenderQueue>);

impl Drop for CloseOnDrop {
    fn drop(&mut self) {
        self.0.close();
    }
}

struct Worker {
    queue: Arc<RenderQueue>,
    join: Mutex<Option<JoinHandle<()>>>,
}

impl Worker {
    fn spawn(renderer: UnityRenderer) -> Arc<Self> {
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
    fn serve(worker_queue: &RenderQueue, mut renderer: UnityRenderer) {
        while let Some(msg) = worker_queue.pop() {
            match msg {
                RenderMsg::Upload(u) => renderer.apply(u),
                RenderMsg::Commands {
                    commands,
                    end_frames,
                } => {
                    renderer.execute(commands);
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

    fn call<R: Send + 'static>(
        &self,
        op: impl FnOnce(&mut UnityRenderer) -> R + Send + 'static,
    ) -> Option<R> {
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
    fn with_renderer_on_caller<R>(&self, op: impl FnOnce(&mut UnityRenderer) -> R) -> Option<R> {
        let (take_tx, take_rx) = mpsc::sync_channel(1);
        let (return_tx, return_rx) = mpsc::sync_channel(1);
        if !self.queue.push(RenderMsg::Lend { take_tx, return_rx }) {
            return None;
        }
        let renderer = take_rx.recv().ok()?;
        struct ReturnOnDrop {
            renderer: Option<UnityRenderer>,
            return_tx: SyncSender<UnityRenderer>,
        }
        impl Drop for ReturnOnDrop {
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

#[derive(Default)]
struct Inner {
    worker: Option<Arc<Worker>>,
    pending: Vec<Pending>,
    /// Where `.shader` files are, for the resolver.
    shader_roots: Vec<PathBuf>,
    /// The shader resolver before the renderer takes it, made on the first shader query (one
    /// made before the GPU is up, e.g. a render pipeline looking up its blit passes when it is
    /// created in the first frame's scripts).
    resolver: Option<Resolver>,
}

impl Inner {
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

#[derive(Default)]
pub struct GpuBackend {
    inner: Mutex<Inner>,
    /// A native rendering plugin is loaded: command batches run on the game thread from then on
    /// (the renderer lent to it, in order), as they did before the render thread, because a
    /// plugin's own device work from scripts (OpenGL calls on its context, Vulkan queue submits,
    /// Metal encoders outside callbacks) is ordered with Unity's rendering on that thread.
    plugins: std::sync::atomic::AtomicBool,
}

impl Drop for GpuBackend {
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
        backend.inner.lock().unwrap().shader_roots = shader_roots;
        backend
    }

    /// The shader resolver for the renderer (the one that answered queries so far), under the
    /// built-in pipeline as a new one is: the pipeline tag among the pending commands reaches it
    /// in order when they run. `shader_roots` are used when the backend was made without any.
    pub fn take_resolver(&self, shader_roots: &[PathBuf]) -> Resolver {
        let mut inner = self.inner.lock().unwrap();
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
        unbound_runtime::plugin_api::while_rendering(|| self.install_holding(renderer));
    }

    fn install_holding(&self, mut renderer: UnityRenderer) {
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

    /// Waits until every upload and command submitted before this call has finished executing on
    /// the render thread (for frame-exact screenshots/snapshots, plugin context handoff, and
    /// clean shutdown).
    pub fn flush(&self) {
        let worker = self.inner.lock().unwrap().worker.clone();
        if let Some(worker) = worker {
            worker.flush();
        }
    }

    fn upload(&self, upload: Upload) {
        let mut inner = self.inner.lock().unwrap();
        match &inner.worker {
            Some(worker) => {
                let worker = Arc::clone(worker);
                drop(inner);
                worker.queue.push(RenderMsg::Upload(upload));
            }
            None => inner.pending.push(Pending::Upload(upload)),
        }
    }
}

impl RenderBackend for GpuBackend {
    fn upload_mesh(&self, handle: u64, mesh: owned::Mesh) {
        self.upload(Upload::UploadMesh(handle, mesh));
    }

    fn release_mesh(&self, handle: u64) {
        self.upload(Upload::ReleaseMesh(handle));
    }

    fn upload_texture(&self, handle: u64, texture: owned::Texture) {
        self.upload(Upload::UploadTexture(handle, texture));
    }

    fn release_texture(&self, handle: u64) {
        self.upload(Upload::ReleaseTexture(handle));
    }

    fn texture_mip_bias(&self, handle: u64, bias: f32) {
        self.upload(Upload::TextureMipBias(handle, bias));
    }

    fn texture_sampler(&self, handle: u64, properties: [i32; 5]) {
        self.upload(Upload::TextureSampler(handle, properties));
    }

    fn update_material(&self, handle: u64, material: owned::Material) {
        self.upload(Upload::UpdateMaterial(handle, material));
    }

    fn release_material(&self, handle: u64) {
        self.upload(Upload::ReleaseMaterial(handle));
    }

    fn submit_commands(&self, commands: Vec<Command>) {
        // The game thread keeps the plugins' OpenGL context current between script calls.
        crate::render::gl_interop::mark_render_thread();
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
        let has_plugin = commands.iter().any(|c| matches!(c, Command::Plugin(_)));
        if has_plugin || self.plugins.load(std::sync::atomic::Ordering::Relaxed) {
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

    fn read_buffer(&self, buffer: u64, offset: u64, size: u64) -> Option<Vec<u8>> {
        let worker = self.inner.lock().unwrap().worker.clone()?;
        worker.call(move |r| r.read_buffer(buffer, offset, size))?
    }

    fn graphics_fence(&self, insert: bool) -> Result<u64, String> {
        let worker = self
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
            let mut inner = self.inner.lock().unwrap();
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
            let mut inner = self.inner.lock().unwrap();
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
        // A plugin is loading, queried or shutting down: rendering runs synchronously from now on.
        self.plugins
            .store(true, std::sync::atomic::Ordering::Relaxed);
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

    fn native_texture(&self, texture: u64, depth: bool) -> u64 {
        let Some(worker) = self.inner.lock().unwrap().worker.clone() else {
            return 0;
        };
        worker
            .with_renderer_on_caller(|r| r.native_texture(texture, depth))
            .unwrap_or(0)
    }

    #[cfg(target_os = "macos")]
    fn render_buffer_texture(&self, texture: u64, image: u8) -> u64 {
        let Some(worker) = self.inner.lock().unwrap().worker.clone() else {
            return 0;
        };
        worker
            .with_renderer_on_caller(|r| r.metal_render_buffer_texture(texture, image))
            .unwrap_or(0)
    }

    fn external_texture(
        &self,
        texture: u64,
        native: u64,
        info: &unbound_runtime::render_protocol::ExternalTextureInfo,
    ) -> Result<(), String> {
        let worker = self
            .inner
            .lock()
            .unwrap()
            .worker
            .clone()
            .ok_or_else(|| "the renderer is not up yet".to_string())?;
        worker
            .with_renderer_on_caller(|r| r.external_texture(texture, native, info))
            .ok_or_else(|| "the renderer has stopped".to_string())?
    }

    fn read_pixels(&self, texture: u64, rect: [i32; 4], float: bool) -> Option<Vec<u8>> {
        let worker = self.inner.lock().unwrap().worker.clone()?;
        worker.call(move |r| r.read_pixels(texture, rect, float))?
    }

    fn read_texture(
        &self,
        texture: u64,
        mip: u32,
        layer: u32,
        rect: [i32; 4],
        float: bool,
        current_attachment: bool,
    ) -> Option<Vec<u8>> {
        let worker = self.inner.lock().unwrap().worker.clone()?;
        worker
            .call(move |r| r.read_texture(texture, mip, layer, rect, float, current_attachment))?
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
        let inner = backend.inner.lock().unwrap();
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

        let queue = RenderQueue::new();
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

        let queue = RenderQueue::new();
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
}
