//! How the Editor's graphics device runs its commands and native plugin callbacks
//! (`SystemInfo.renderingThreadingMode`) with `-force-gfx-mt` (without it, each device's own
//! mode: the managed GraphicsDeviceProfile's).
use crate::{GraphicsDevice, MeasuredFamily, UnityVersion};

/// The `UnityEngine.Rendering.RenderingThreadingMode` values a run can have.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum RenderingThreadingMode {
    /// Commands and plugin callbacks on the main thread, executed at once (OpenGL Core).
    Direct,
    /// A client device on the main thread (the batchmode Editor's Vulkan and Metal devices).
    SingleThreaded,
    /// A render thread: commands and plugin callbacks run on it, in the order they were issued.
    MultiThreaded,
}

impl RenderingThreadingMode {
    /// The enum value's name, as `SystemInfo.renderingThreadingMode` prints it.
    pub fn name(self) -> &'static str {
        match self {
            Self::Direct => "Direct",
            Self::SingleThreaded => "SingleThreaded",
            Self::MultiThreaded => "MultiThreaded",
        }
    }
}

/// What `-force-gfx-mt` gives a run of `version` on `device`, with the warning the Editor logs
/// when it keeps another mode; an error where that is not measured.
///
/// Measured for the 6000.0 family (Unity 6000.0.71f1, Linux, batchmode;
/// tests/projects/nativegfx `run.py --mt`, Reference/unity-6000.0.71f1/opengl-mt.jsonl and
/// vulkan-mt.jsonl): OpenGL Core keeps Direct, its callbacks on the main thread, and the Editor
/// logs that the threaded mode is not supported; Vulkan becomes MultiThreaded, its callbacks on
/// the render thread. Metal and other families are not measured.
pub fn forced_multithreading(
    version: UnityVersion,
    device: GraphicsDevice,
) -> Result<(RenderingThreadingMode, Option<&'static str>), String> {
    if version.measured_family() != Some(MeasuredFamily::Unity6000_0) {
        return Err(format!(
            "multithreaded rendering (-force-gfx-mt) is not measured for Unity {version}"
        ));
    }
    match device {
        GraphicsDevice::OpenGLCore => Ok((
            RenderingThreadingMode::Direct,
            Some(
                "kGfxThreadingModeThreaded is not supported on OpenGL Core. Reverting to \
                 kGfxThreadingModeDirect instead.",
            ),
        )),
        GraphicsDevice::Vulkan => Ok((RenderingThreadingMode::MultiThreaded, None)),
        GraphicsDevice::Metal => Err(
            "multithreaded rendering (-force-gfx-mt) on the Metal device is not measured".into(),
        ),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn the_measured_family_reverts_opengl_core_and_threads_vulkan() {
        for version in [
            UnityVersion::new(6000, 0, 71),
            UnityVersion::new(6000, 0, 0),
        ] {
            let (mode, warning) =
                forced_multithreading(version, GraphicsDevice::OpenGLCore).unwrap();
            assert_eq!(mode, RenderingThreadingMode::Direct);
            assert!(warning.unwrap().contains("not supported on OpenGL Core"));
            assert_eq!(
                forced_multithreading(version, GraphicsDevice::Vulkan).unwrap(),
                (RenderingThreadingMode::MultiThreaded, None)
            );
            assert!(forced_multithreading(version, GraphicsDevice::Metal).is_err());
        }
    }

    #[test]
    fn other_families_are_not_measured() {
        for version in [UnityVersion::new(6000, 3, 0), UnityVersion::new(6000, 1, 5)] {
            for device in [
                GraphicsDevice::OpenGLCore,
                GraphicsDevice::Vulkan,
                GraphicsDevice::Metal,
            ] {
                assert!(forced_multithreading(version, device).is_err());
            }
        }
    }
}
