//! The run's graphics device (see `unbound_compat::GraphicsDevice`): chosen once at startup,
//! before the game, the shader compiler or the renderer starts, and read by all of them.
use std::sync::OnceLock;
pub use unbound_compat::GraphicsDevice;

static DEVICE: OnceLock<GraphicsDevice> = OnceLock::new();

/// Sets the run's device. A device is chosen once: setting another one later is a bug.
pub fn set(device: GraphicsDevice) {
    let current = *DEVICE.get_or_init(|| device);
    assert_eq!(current, device, "the graphics device was already chosen");
}

/// The run's device; OpenGL Core where nothing chose one (tools and tests that start no run).
pub fn get() -> GraphicsDevice {
    DEVICE.get().copied().unwrap_or(GraphicsDevice::OpenGLCore)
}
