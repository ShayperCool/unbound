//! The render backend the managed engine talks to. Calls arrive on the main thread while a game
//! frame runs; they are queued in order and handed to the render world at extraction, when the
//! main world is between frames, so a frame's uploads and its draw lists always travel together.
use std::sync::Mutex;
use unbound_runtime::render_protocol::{RenderBackend, owned};

#[derive(Debug)]
pub enum Command {
    UploadMesh(u64, owned::Mesh),
    ReleaseMesh(u64),
    UploadTexture(u64, owned::Texture),
    ReleaseTexture(u64),
    UpdateMaterial(u64, owned::Material),
    ReleaseMaterial(u64),
}

#[derive(Default)]
struct Pending {
    commands: Vec<Command>,
    frame: Option<owned::Frame>,
}

/// Shared by the main world (producer) and the render world (consumer).
#[derive(Default)]
pub struct QueuedBackend {
    pending: Mutex<Pending>,
}

impl QueuedBackend {
    pub fn new() -> Self {
        Self::default()
    }

    /// Everything queued since the last call, and the latest submitted frame (if any).
    pub fn take(&self) -> (Vec<Command>, Option<owned::Frame>) {
        let mut pending = self.pending.lock().unwrap();
        (std::mem::take(&mut pending.commands), pending.frame.take())
    }

    fn push(&self, command: Command) {
        self.pending.lock().unwrap().commands.push(command);
    }
}

impl RenderBackend for QueuedBackend {
    fn upload_mesh(&self, handle: u64, mesh: owned::Mesh) {
        self.push(Command::UploadMesh(handle, mesh));
    }

    fn release_mesh(&self, handle: u64) {
        self.push(Command::ReleaseMesh(handle));
    }

    fn upload_texture(&self, handle: u64, texture: owned::Texture) {
        self.push(Command::UploadTexture(handle, texture));
    }

    fn release_texture(&self, handle: u64) {
        self.push(Command::ReleaseTexture(handle));
    }

    fn update_material(&self, handle: u64, material: owned::Material) {
        self.push(Command::UpdateMaterial(handle, material));
    }

    fn release_material(&self, handle: u64) {
        self.push(Command::ReleaseMaterial(handle));
    }

    fn submit_frame(&self, frame: owned::Frame) {
        self.pending.lock().unwrap().frame = Some(frame);
    }
}
