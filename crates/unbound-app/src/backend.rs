//! The render backend the managed engine talks to. Its calls arrive on the game's thread and run
//! on the GPU right away (`UnityRenderer`), in order; calls made before the renderer exists (the
//! window's GPU is not up yet) wait and run when it is installed. Shader queries need no GPU:
//! until then the backend's own resolver answers them.
use crate::materials::Resolver;
use crate::render::executor::{UnityRenderer, Upload};
use crate::shaderlab::ShaderIndex;
use std::path::PathBuf;
use std::sync::Mutex;
use unbound_runtime::render_protocol::{
    RenderBackend, ShaderWarmupRequest, commands::Command, owned,
};

enum Pending {
    Upload(Upload),
    Commands(Vec<Command>),
    Warmup(ShaderWarmupRequest),
}

#[derive(Default)]
struct Inner {
    renderer: Option<UnityRenderer>,
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

    /// Hands the backend its renderer and runs what waited for it.
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
        inner.renderer = Some(renderer);
    }

    fn upload(&self, upload: Upload) {
        let mut inner = self.inner.lock().unwrap();
        match &mut inner.renderer {
            Some(r) => r.apply(upload),
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
        // Plugin callbacks among the commands may log; scripts see that once the renderer is free.
        unbound_runtime::plugin_api::while_rendering(|| {
            let mut inner = self.inner.lock().unwrap();
            match &mut inner.renderer {
                Some(r) => r.execute(commands),
                None => inner.pending.push(Pending::Commands(commands)),
            }
        })
    }

    fn read_buffer(&self, buffer: u64, offset: u64, size: u64) -> Option<Vec<u8>> {
        self.inner
            .lock()
            .unwrap()
            .renderer
            .as_mut()?
            .read_buffer(buffer, offset, size)
    }

    fn graphics_fence(&self, insert: bool) -> Result<u64, String> {
        self.inner
            .lock()
            .unwrap()
            .renderer
            .as_mut()
            .ok_or_else(|| "the renderer is not up yet".to_string())?
            .graphics_fence(insert)
    }

    fn shader_passes(&self, shader: &str) -> Result<String, String> {
        let mut inner = self.inner.lock().unwrap();
        if let Some(r) = inner.renderer.as_mut() {
            return Ok(r.shader_passes(shader));
        }
        if inner.shader_roots.is_empty() {
            return Err("the renderer is not up yet".into());
        }
        // The pipeline the waiting commands leave active, as the renderer will see it.
        let tag = inner
            .pending
            .iter()
            .rev()
            .filter_map(|p| match p {
                Pending::Commands(commands) => commands.iter().rev().find_map(|c| match c {
                    Command::RenderPipeline(tag) => Some(tag.as_str()),
                    _ => None,
                }),
                Pending::Upload(_) | Pending::Warmup(_) => None,
            })
            .next()
            .unwrap_or("")
            .to_string();
        let resolver = inner.resolver();
        resolver.set_pipeline(&tag);
        Ok(resolver.describe(shader))
    }

    fn shader_warmup(&self, request: ShaderWarmupRequest) -> Result<String, String> {
        let mut inner = self.inner.lock().unwrap();
        if let Some(renderer) = inner.renderer.as_mut() {
            return renderer.shader_warmup(request);
        }
        if request.stats {
            return Err("shader statistics require an active renderer".into());
        }
        // Like uploads and draws, preparation waits for the device and keeps submission order.
        inner.pending.push(Pending::Warmup(request));
        Ok("{}".into())
    }

    fn kernel_threads(
        &self,
        file: &str,
        kernel: &str,
        keywords: &[String],
    ) -> Result<[u32; 3], String> {
        match self.inner.lock().unwrap().renderer.as_mut() {
            Some(r) => r.kernel_threads(file, kernel, keywords),
            None => Err("the renderer is not up yet".into()),
        }
    }

    fn plugin_context(&self) -> bool {
        match self.inner.lock().unwrap().renderer.as_mut() {
            Some(r) => r.plugin_context(),
            // Before the GPU is up: the first one that can share textures.
            None => crate::render::gl_interop::with(None, |_| ()).is_ok(),
        }
    }

    fn native_texture(&self, texture: u64, depth: bool) -> u64 {
        match self.inner.lock().unwrap().renderer.as_mut() {
            Some(r) => r.native_texture(texture, depth),
            None => 0,
        }
    }

    fn external_texture(
        &self,
        texture: u64,
        native: u64,
        width: u32,
        height: u32,
        srgb: bool,
    ) -> Result<(), String> {
        match self.inner.lock().unwrap().renderer.as_mut() {
            Some(r) => r.external_texture(texture, native, width, height, srgb),
            None => Err("the renderer is not up yet".into()),
        }
    }

    fn read_pixels(&self, texture: u64, rect: [i32; 4], float: bool) -> Option<Vec<u8>> {
        self.inner
            .lock()
            .unwrap()
            .renderer
            .as_mut()?
            .read_pixels(texture, rect, float)
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
        self.inner.lock().unwrap().renderer.as_mut()?.read_texture(
            texture,
            mip,
            layer,
            rect,
            float,
            current_attachment,
        )
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
}
