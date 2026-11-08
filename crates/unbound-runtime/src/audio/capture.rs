//! Offline AudioRenderer output. Group sinks retain DSP floats; the returned device stream uses
//! the signed-16-bit conversion measured independently in Unity 6000.0.71f1.
use serde::{Deserialize, Serialize};

/// Engine resource limit for one interleaved stereo capture allocation.
pub(super) const MAX_SAMPLES: usize = 16_777_216;

#[derive(Clone, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SinkRequest {
    pub handle: u64,
    pub length: usize,
    pub exclude_from_mix: bool,
}

pub struct Sink {
    pub request: SinkRequest,
    pub samples: Vec<f32>,
}

#[derive(Serialize)]
pub struct SinkOutput {
    pub handle: u64,
    pub samples: Vec<f32>,
}

#[derive(Default, Serialize)]
pub struct Output {
    pub ok: bool,
    pub samples: Vec<f32>,
    pub sinks: Vec<SinkOutput>,
}

pub fn pcm16(sample: f32) -> f32 {
    // Rust's integer conversion saturates and truncates toward zero; no dither is added.
    (sample * 32768.0) as i16 as f32 / 32768.0
}
