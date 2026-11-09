//! The GPU backend that draws each graphics device (`unbound_compat::GraphicsDevice`), and the
//! device's conventions as the renderer applies them. Resolved once at startup.
use unbound_compat::GraphicsDevice;

/// The wgpu backend that draws the device.
///
/// OpenGL Core is drawn by the OS's native backend for now (Vulkan on Linux, Metal on macOS):
/// wgpu's OpenGL backend lacks what the renderer needs (texture views in another format,
/// independent blending on its 3.3 context, sampler LOD bias), see docs/COMPATIBILITY_GAPS.md
/// (graphics devices). Native plugins on that device get an OpenGL context sharing the
/// renderer's memory on Linux (`gl_interop`).
pub fn backends(device: GraphicsDevice) -> wgpu::Backends {
    backends_on(device, unbound_compat::HostOs::current())
}

fn backends_on(device: GraphicsDevice, os: unbound_compat::HostOs) -> wgpu::Backends {
    match (device, os) {
        (GraphicsDevice::Metal, _) | (_, unbound_compat::HostOs::MacOs) => wgpu::Backends::METAL,
        (GraphicsDevice::OpenGLCore | GraphicsDevice::Vulkan, unbound_compat::HostOs::Linux) => {
            wgpu::Backends::VULKAN
        }
    }
}

/// Whether the device's backend has an adapter on this machine.
pub fn starts(device: GraphicsDevice) -> bool {
    let backends = backends(device);
    let instance = wgpu::Instance::new(wgpu::InstanceDescriptor {
        backends,
        ..wgpu::InstanceDescriptor::new_without_display_handle_from_env()
    });
    !bevy::tasks::block_on(instance.enumerate_adapters(backends)).is_empty()
}

/// How the renderer draws for the device: Unity's GPU projection, depth direction and target
/// orientation (see `unbound_compat::Conventions`).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct DeviceConventions {
    /// The screen's rows are stored top-down (Unity draws it with the projection unflipped);
    /// render textures stay bottom-up (drawn with the projection's y flipped).
    pub screen_top_down: bool,
    /// Depth 1 is near: depth tests, clears and biases are mirrored.
    pub reversed_z: bool,
    /// Clip depth from 0 to w: the GPU projection maps [-1, 1] there.
    pub clip_depth_zero_to_one: bool,
    /// The sample a multisampled depth buffer's resolved copy reads, as the Editor's device
    /// resolves depth (measured at 2 samples, tests/projects/rtdepth `fmt.msaa2.depth.*`, in
    /// Vulkan's standard positions): OpenGL Core the one a quarter pixel left of the center
    /// (sample 1), Vulkan sample 0.
    pub depth_resolve_sample: u32,
}

impl DeviceConventions {
    pub fn of(device: GraphicsDevice) -> Self {
        let c = device.conventions();
        Self {
            screen_top_down: c.uv_starts_at_top,
            reversed_z: c.reversed_z,
            clip_depth_zero_to_one: c.clip_depth_zero_to_one,
            depth_resolve_sample: match device {
                GraphicsDevice::OpenGLCore => 1,
                GraphicsDevice::Vulkan | GraphicsDevice::Metal => 0,
            },
        }
    }
}

impl DeviceConventions {
    /// Whether draws into a target use Unity's flipped projection (render textures on a device
    /// whose UV starts at the top; never the screen).
    pub fn flips(&self, screen: bool) -> bool {
        self.screen_top_down && !screen
    }

    /// Whether the target's rows are stored top-down (the screen on such a device).
    pub fn top_down(&self, screen: bool) -> bool {
        self.screen_top_down && screen
    }

    /// `GL.GetGPUProjectionMatrix`: an OpenGL-convention projection (column-major) as the device
    /// draws with it. Depth from [-1, 1] to [0, 1] (reversed: 1 near), y negated when `flip`.
    /// Matches the Unity 6000.0.71f1 Editor on Vulkan to the bit for the measured perspective
    /// and orthographic matrices.
    pub fn gpu_projection(&self, proj: &[f32; 16], flip: bool) -> [f32; 16] {
        let mut m = *proj;
        if self.clip_depth_zero_to_one {
            let sign = if self.reversed_z { -0.5 } else { 0.5 };
            for column in 0..4 {
                let (z, w) = (proj[column * 4 + 2], proj[column * 4 + 3]);
                m[column * 4 + 2] = z * sign + w * 0.5;
            }
        }
        if flip {
            for column in 0..4 {
                m[column * 4 + 1] = -m[column * 4 + 1];
            }
        }
        m
    }

    /// Depth `d` as the device stores it (reversed: 1 - d).
    pub fn depth(&self, d: f32) -> f32 {
        if self.reversed_z { 1.0 - d } else { d }
    }
}

/// Unity's CompareFunction with its operands swapped (Less and Greater, LessEqual and
/// GreaterEqual trade places; the others stay).
pub fn mirror_compare(f: u8) -> u8 {
    match f {
        2 => 5,
        5 => 2,
        4 => 7,
        7 => 4,
        f => f,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Row-major text as the Editor prints a Matrix4x4, to column-major.
    fn columns(rows: [f32; 16]) -> [f32; 16] {
        let mut m = [0.0; 16];
        for r in 0..4 {
            for c in 0..4 {
                m[c * 4 + r] = rows[r * 4 + c];
            }
        }
        m
    }

    #[test]
    fn vulkan_gpu_projection_matches_the_editor() {
        // Matrix4x4.Perspective(60, 16/9, 0.3, 1000) and Ortho(-5, 5, -3, 3, 1, 11) through
        // GL.GetGPUProjectionMatrix on the 6000.0.71f1 Editor's Vulkan and OpenGL Core devices.
        let persp = columns([
            0.974278569,
            0.0,
            0.0,
            0.0,
            0.0,
            1.73205078,
            0.0,
            0.0,
            0.0,
            0.0,
            -1.0006001,
            -0.60018003,
            0.0,
            0.0,
            -1.0,
            0.0,
        ]);
        let ortho = columns([
            0.2,
            0.0,
            0.0,
            0.0,
            0.0,
            0.333333343,
            0.0,
            0.0,
            0.0,
            0.0,
            -0.2,
            -1.2,
            0.0,
            0.0,
            0.0,
            1.0,
        ]);
        let vulkan = DeviceConventions::of(GraphicsDevice::Vulkan);
        let gl = DeviceConventions::of(GraphicsDevice::OpenGLCore);
        assert_eq!(gl.gpu_projection(&persp, gl.flips(false)), persp);
        assert_eq!(
            vulkan.gpu_projection(&persp, vulkan.flips(true)),
            columns([
                0.974278569,
                0.0,
                0.0,
                0.0,
                0.0,
                1.73205078,
                0.0,
                0.0,
                0.0,
                0.0,
                0.000300049782,
                0.300090015,
                0.0,
                0.0,
                -1.0,
                0.0
            ])
        );
        assert_eq!(
            vulkan.gpu_projection(&ortho, vulkan.flips(false)),
            columns([
                0.2,
                0.0,
                0.0,
                0.0,
                0.0,
                -0.333333343,
                0.0,
                0.0,
                0.0,
                0.0,
                0.1,
                1.1,
                0.0,
                0.0,
                0.0,
                1.0
            ])
        );
        assert_eq!(vulkan.depth(1.0), 0.0);
        // The OS's native backend draws OpenGL Core for now (macOS: Metal, as before devices).
        use unbound_compat::HostOs;
        assert_eq!(
            backends_on(GraphicsDevice::OpenGLCore, HostOs::Linux),
            wgpu::Backends::VULKAN
        );
        assert_eq!(
            backends_on(GraphicsDevice::OpenGLCore, HostOs::MacOs),
            wgpu::Backends::METAL
        );
        assert_eq!(mirror_compare(4), 7);
        assert_eq!(mirror_compare(8), 8);
    }
}
