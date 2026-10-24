//! The render backend the managed engine talks to. Its calls arrive on the game's thread and run
//! on the GPU right away (`UnityRenderer`), in order; calls made before the renderer exists (the
//! window's GPU is not up yet) wait and run when it is installed.
use crate::render::executor::{UnityRenderer, Upload};
use std::sync::Mutex;
use unbound_runtime::render_protocol::{RenderBackend, commands::Command, owned};

enum Pending {
    Upload(Upload),
    Commands(Vec<Command>),
}

#[derive(Default)]
struct Inner {
    renderer: Option<UnityRenderer>,
    pending: Vec<Pending>,
}

#[derive(Default)]
pub struct GpuBackend {
    inner: Mutex<Inner>,
}

impl GpuBackend {
    pub fn new() -> Self {
        Self::default()
    }

    /// Hands the backend its renderer and runs what waited for it.
    pub fn install(&self, mut renderer: UnityRenderer) {
        let mut inner = self.inner.lock().unwrap();
        for pending in std::mem::take(&mut inner.pending) {
            match pending {
                Pending::Upload(u) => renderer.apply(u),
                Pending::Commands(c) => renderer.execute(c),
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

    fn update_material(&self, handle: u64, material: owned::Material) {
        self.upload(Upload::UpdateMaterial(handle, material));
    }

    fn release_material(&self, handle: u64) {
        self.upload(Upload::ReleaseMaterial(handle));
    }

    fn submit_commands(&self, commands: Vec<Command>) {
        let mut inner = self.inner.lock().unwrap();
        match &mut inner.renderer {
            Some(r) => r.execute(commands),
            None => inner.pending.push(Pending::Commands(commands)),
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
}
