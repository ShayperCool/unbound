//! The run's graphics device (see `unbound_compat::GraphicsDevice`) and its threading mode:
//! chosen once at startup, before the game, the shader compiler or the renderer starts, and read
//! by all of them.
use std::sync::OnceLock;
pub use unbound_compat::{GraphicsDevice, RenderingThreadingMode};

static DEVICE: OnceLock<GraphicsDevice> = OnceLock::new();
static FORCED_THREADING: OnceLock<RenderingThreadingMode> = OnceLock::new();

/// Sets the run's device. A device is chosen once: setting another one later is a bug.
pub fn set(device: GraphicsDevice) {
    let current = *DEVICE.get_or_init(|| device);
    assert_eq!(current, device, "the graphics device was already chosen");
}

/// The run's device; where nothing chose one (tools and tests that start no run) the OS's
/// Editor's default (OpenGL Core on Linux, Metal on macOS).
pub fn get() -> GraphicsDevice {
    DEVICE
        .get()
        .copied()
        .unwrap_or_else(|| unbound_compat::HostOs::current().default_devices()[0])
}

/// Sets the threading mode `-force-gfx-mt` gives the run (`unbound_compat::forced_multithreading`).
pub fn set_forced_threading(mode: RenderingThreadingMode) {
    let current = *FORCED_THREADING.get_or_init(|| mode);
    assert_eq!(
        current, mode,
        "the rendering threading mode was already chosen"
    );
}

/// The threading mode `-force-gfx-mt` gave the run; None: the device's own (the managed
/// GraphicsDeviceProfile's).
pub fn forced_threading() -> Option<RenderingThreadingMode> {
    FORCED_THREADING.get().copied()
}
