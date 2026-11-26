//! Tracker module decoding (`.mod`, `.xm`, `.s3m`, `.it`) through `libxmp-lite`
//! (`native/unbound_tracker.c`).
//!
//! Unity renders tracker modules to stereo PCM at the audio mixer's output sample rate, ignoring
//! `AudioImporter` `forceToMono` and `sampleRateOverride` (matches the Unity 6000.0.71f1 Editor,
//! `tests/projects/audiotracker`).

use anyhow::{Result, bail};
use std::ffi::c_int;

/// File extensions for tracker modules handled by `AudioImporter`.
pub const EXTENSIONS: &[&str] = &["mod", "xm", "s3m", "it"];

/// One of the four tracker module formats Unity supports.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(i32)]
pub enum TrackerFormat {
    Mod = 1,
    Xm = 2,
    S3m = 3,
    It = 4,
}

impl TrackerFormat {
    /// Format named by a file extension (without leading dot, case-insensitive).
    pub fn from_extension(ext: &str) -> Option<Self> {
        match ext.to_ascii_lowercase().as_str() {
            "mod" => Some(Self::Mod),
            "xm" => Some(Self::Xm),
            "s3m" => Some(Self::S3m),
            "it" => Some(Self::It),
            _ => None,
        }
    }

    /// Format named by a Unity `AudioType` integer value.
    pub fn from_audio_type(audio_type: i32) -> Option<Self> {
        match audio_type {
            10 => Some(Self::It),
            12 => Some(Self::Mod),
            17 => Some(Self::S3m),
            21 => Some(Self::Xm),
            _ => None,
        }
    }

    fn from_code(code: c_int) -> Option<Self> {
        match code {
            1 => Some(Self::Mod),
            2 => Some(Self::Xm),
            3 => Some(Self::S3m),
            4 => Some(Self::It),
            _ => None,
        }
    }
}

/// A decoded stereo tracker module.
#[derive(Debug, Clone)]
pub struct DecodedTracker {
    pub channels: u16,
    pub sample_rate: u32,
    /// Interleaved stereo samples in `[-1, 1]`.
    pub samples: Vec<f32>,
    pub format: TrackerFormat,
}

unsafe extern "C" {
    fn unbound_tracker_detect(data: *const u8, len: usize) -> c_int;
    fn unbound_tracker_decode(
        data: *const u8,
        len: usize,
        sample_rate: c_int,
        expected_format: c_int,
        out_samples: *mut *mut f32,
        out_frames: *mut usize,
        out_format: *mut c_int,
    ) -> c_int;
    fn unbound_tracker_free(samples: *mut f32);
}

/// Returns the tracker module format of `data`, or `None` when the bytes are not one of the four
/// supported tracker formats.
pub fn detect(data: &[u8]) -> Option<TrackerFormat> {
    if data.is_empty() {
        return None;
    }
    let code = unsafe { unbound_tracker_detect(data.as_ptr(), data.len()) };
    TrackerFormat::from_code(code)
}

/// Decodes a tracker module into stereo interleaved `f32` PCM at `sample_rate` Hz.
/// When `expected` is `Some(fmt)`, data of another format is rejected.
pub fn decode(
    data: &[u8],
    expected: Option<TrackerFormat>,
    sample_rate: u32,
) -> Result<DecodedTracker> {
    if data.is_empty() {
        bail!("empty tracker module");
    }
    let rate = sample_rate.clamp(4000, 768_000) as c_int;
    let expected_code = expected.map_or(0, |f| f as c_int);
    let mut ptr: *mut f32 = std::ptr::null_mut();
    let mut frames: usize = 0;
    let mut actual_code: c_int = 0;
    let rc = unsafe {
        unbound_tracker_decode(
            data.as_ptr(),
            data.len(),
            rate,
            expected_code,
            &mut ptr,
            &mut frames,
            &mut actual_code,
        )
    };
    if rc != 0 {
        if !ptr.is_null() {
            unsafe { unbound_tracker_free(ptr) };
        }
        bail!("decoding tracker module failed (code {rc})");
    }
    let samples = if ptr.is_null() || frames == 0 {
        if !ptr.is_null() {
            unsafe { unbound_tracker_free(ptr) };
        }
        Vec::new()
    } else {
        let slice = unsafe { std::slice::from_raw_parts(ptr, frames * 2) };
        let owned = slice.to_vec();
        unsafe { unbound_tracker_free(ptr) };
        owned
    };
    let format = TrackerFormat::from_code(actual_code)
        .ok_or_else(|| anyhow::anyhow!("unknown tracker format code {actual_code}"))?;
    Ok(DecodedTracker {
        channels: 2,
        sample_rate: rate as u32,
        samples,
        format,
    })
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::path::Path;

    fn probe_asset(name: &str) -> Vec<u8> {
        let root = Path::new(env!("CARGO_MANIFEST_DIR"))
            .join("../../tests/projects/audiotracker/Assets/Audio");
        std::fs::read(root.join(name)).unwrap()
    }

    #[test]
    fn detects_and_decodes_all_four_tracker_formats() {
        for (file, fmt, expected_frames) in [
            ("tone.mod", TrackerFormat::Mod, 92_160),
            ("tone.xm", TrackerFormat::Xm, 92_160),
            ("tone.s3m", TrackerFormat::S3m, 92_160),
            ("tone.it", TrackerFormat::It, 92_160),
            ("xm_bpm150.xm", TrackerFormat::Xm, 76_800),
        ] {
            let bytes = probe_asset(file);
            assert_eq!(detect(&bytes), Some(fmt), "{file}");
            let decoded = decode(&bytes, Some(fmt), 48_000).unwrap();
            assert_eq!(decoded.format, fmt, "{file}");
            assert_eq!(decoded.channels, 2, "{file}");
            assert_eq!(decoded.sample_rate, 48_000, "{file}");
            assert_eq!(decoded.samples.len() / 2, expected_frames, "{file}");
            let peak = decoded.samples.iter().fold(0.0f32, |m, &s| m.max(s.abs()));
            assert!(
                peak > 0.1,
                "{file} should produce audible signal, peak={peak}"
            );
        }
    }

    #[test]
    fn rejects_mismatched_formats_and_non_tracker_data() {
        let mod_bytes = probe_asset("tone.mod");
        assert!(decode(&mod_bytes, Some(TrackerFormat::Xm), 48_000).is_err());
        assert!(decode(&mod_bytes, Some(TrackerFormat::S3m), 48_000).is_err());
        assert!(decode(&mod_bytes, Some(TrackerFormat::It), 48_000).is_err());
        assert_eq!(detect(b"not a tracker module\n"), None);
        assert_eq!(detect(b""), None);
        assert!(decode(b"not a tracker module\n", None, 48_000).is_err());
        assert!(decode(b"", None, 48_000).is_err());
    }

    #[test]
    fn decodes_at_requested_output_sample_rate() {
        let xm_bytes = probe_asset("tone.xm");
        let at_44100 = decode(&xm_bytes, Some(TrackerFormat::Xm), 44_100).unwrap();
        assert_eq!(at_44100.sample_rate, 44_100);
        // 16 rows * 6 ticks * 882 samples/tick at 44100 Hz, BPM 125.
        assert_eq!(at_44100.samples.len() / 2, 84_672);
    }
}
