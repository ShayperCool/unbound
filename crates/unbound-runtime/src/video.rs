//! Video clips through ffmpeg (the machine's `ffprobe` and `ffmpeg`): a clip's properties, and
//! decoders that bring a clip's frames to textures by number. A decoder keeps an `ffmpeg` process
//! that writes the frames' Y, U and V planes (4:2:0) to a pipe; frames ahead are read in turn, a
//! seek elsewhere restarts it at the frame's time. The planes go to three Alpha8 textures, which
//! Unity's `Hidden/VideoDecode` shader turns into colours, as Unity's players do.

use std::collections::HashMap;
use std::io::Read;
use std::process::{Child, ChildStdout, Command, Stdio};
use std::sync::{Mutex, OnceLock};

use anyhow::{Context, Result, bail};
use serde_json::{Value as Json, json};

fn rate(text: &str) -> f64 {
    match text.split_once('/') {
        Some((n, d)) => {
            let (n, d): (f64, f64) = (n.parse().unwrap_or(0.0), d.parse().unwrap_or(1.0));
            if d == 0.0 { 0.0 } else { n / d }
        }
        None => text.parse().unwrap_or(0.0),
    }
}

/// A clip's properties: size, frame rate (as a fraction too), frame count, length, audio tracks,
/// pixel aspect ratio.
pub fn probe(path: &str) -> Result<Json> {
    let output = Command::new("ffprobe")
        .args(["-v", "error", "-count_packets", "-show_entries"])
        .arg("stream=codec_type,width,height,r_frame_rate,nb_read_packets,sample_aspect_ratio:format=duration")
        .args(["-of", "json", path])
        .output()
        .context("Unbound plays video through ffmpeg; `ffprobe` could not be run (is ffmpeg installed?)")?;
    if !output.status.success() {
        bail!(
            "ffprobe cannot read {path}: {}",
            String::from_utf8_lossy(&output.stderr).trim()
        );
    }
    let root: Json = serde_json::from_slice(&output.stdout)?;
    let streams = root["streams"].as_array().cloned().unwrap_or_default();
    let video = streams
        .iter()
        .find(|s| s["codec_type"] == "video")
        .with_context(|| format!("{path} has no video stream"))?;
    let audio_tracks = streams
        .iter()
        .filter(|s| s["codec_type"] == "audio")
        .count();
    let rate_text = video["r_frame_rate"].as_str().unwrap_or("0/1");
    let (numerator, denominator) = match rate_text.split_once('/') {
        Some((n, d)) => (n.parse::<u64>().unwrap_or(0), d.parse::<u64>().unwrap_or(1)),
        None => (rate_text.parse::<u64>().unwrap_or(0), 1),
    };
    let fps = rate(rate_text);
    let frames: u64 = video["nb_read_packets"]
        .as_str()
        .and_then(|s| s.parse().ok())
        .unwrap_or(0);
    let duration: f64 = root["format"]["duration"]
        .as_str()
        .and_then(|s| s.parse().ok())
        .unwrap_or(0.0);
    let (aspect_n, aspect_d) = match video["sample_aspect_ratio"]
        .as_str()
        .and_then(|s| s.split_once(':'))
    {
        Some((n, d)) => (
            n.parse::<u32>().unwrap_or(1).max(1),
            d.parse::<u32>().unwrap_or(1).max(1),
        ),
        None => (1, 1),
    };
    Ok(json!({
        "width": video["width"],
        "height": video["height"],
        "frameRate": fps,
        "frameRateNumerator": numerator,
        "frameRateDenominator": denominator,
        "frameCount": frames,
        "length": duration,
        "audioTracks": audio_tracks,
        "aspectNumerator": aspect_n,
        "aspectDenominator": aspect_d,
    }))
}

struct Decoder {
    path: String,
    width: usize,
    height: usize,
    fps: f64,
    process: Option<(Child, ChildStdout)>,
    /// The frame the process writes next.
    next: i64,
}

impl Decoder {
    fn start(&mut self, frame: i64) -> Result<()> {
        self.stop();
        let time = frame.max(0) as f64 / self.fps.max(1e-6);
        let mut child = Command::new("ffmpeg")
            .args(["-v", "error", "-nostdin", "-ss", &format!("{time:.6}"), "-i", &self.path])
            .args(["-an", "-f", "rawvideo", "-pix_fmt", "yuv420p", "-"])
            .stdin(Stdio::null())
            .stdout(Stdio::piped())
            .stderr(Stdio::null())
            .spawn()
            .context("Unbound plays video through ffmpeg; `ffmpeg` could not be run (is ffmpeg installed?)")?;
        let stdout = child.stdout.take().context("no ffmpeg output")?;
        self.process = Some((child, stdout));
        self.next = frame.max(0);
        Ok(())
    }

    fn stop(&mut self) {
        if let Some((mut child, _)) = self.process.take() {
            let _ = child.kill();
            let _ = child.wait();
        }
    }

    /// The planes' sizes: Y, then U and V at half the size (rounded up).
    fn planes(&self) -> [(usize, usize); 3] {
        let chroma = (self.width.div_ceil(2), self.height.div_ceil(2));
        [(self.width, self.height), chroma, chroma]
    }

    /// Frame `n`'s Y, U and V planes one after the other, top rows first; None past the end.
    fn frame(&mut self, n: i64) -> Result<Option<Vec<u8>>> {
        let ahead = n - self.next;
        if self.process.is_none() || ahead < 0 || ahead as f64 > self.fps * 2.0 {
            self.start(n)?;
        }
        let size: usize = self.planes().iter().map(|(w, h)| w * h).sum();
        let mut buffer = vec![0u8; size];
        loop {
            let (_, stdout) = self.process.as_mut().unwrap();
            if stdout.read_exact(&mut buffer).is_err() {
                self.stop();
                return Ok(None);
            }
            self.next += 1;
            if self.next - 1 == n {
                return Ok(Some(buffer));
            }
        }
    }
}

impl Drop for Decoder {
    fn drop(&mut self) {
        self.stop();
    }
}

fn decoders() -> &'static Mutex<HashMap<u64, Decoder>> {
    static DECODERS: OnceLock<Mutex<HashMap<u64, Decoder>>> = OnceLock::new();
    DECODERS.get_or_init(|| Mutex::new(HashMap::new()))
}

pub fn open(path: &str, width: usize, height: usize, fps: f64) -> u64 {
    static NEXT: std::sync::atomic::AtomicU64 = std::sync::atomic::AtomicU64::new(1);
    let id = NEXT.fetch_add(1, std::sync::atomic::Ordering::Relaxed);
    decoders().lock().unwrap().insert(
        id,
        Decoder {
            path: path.to_string(),
            width,
            height,
            fps,
            process: None,
            next: 0,
        },
    );
    id
}

pub fn close(id: u64) {
    decoders().lock().unwrap().remove(&id);
}

/// Decodes frame `n` of a decoder into the Y, U and V textures `handles` (rows bottom first, as
/// Unity's textures are); false past the clip's end.
pub fn frame(id: u64, n: i64, handles: [u64; 3]) -> Result<bool> {
    let (data, planes) = {
        let mut all = decoders().lock().unwrap();
        let decoder = all
            .get_mut(&id)
            .with_context(|| format!("video decoder {id} is not open"))?;
        let Some(data) = decoder.frame(n)? else {
            return Ok(false);
        };
        (data, decoder.planes())
    };
    let mut at = 0;
    for ((width, height), handle) in planes.into_iter().zip(handles) {
        let plane = &data[at..at + width * height];
        at += width * height;
        let mut flipped = Vec::with_capacity(plane.len());
        for row in plane.chunks_exact(width).rev() {
            flipped.extend_from_slice(row);
        }
        crate::state::backend().upload_texture(
            handle,
            crate::render_protocol::owned::Texture {
                width: width as i32,
                height: height as i32,
                format: 3,
                mip_count: 1,
                data: flipped,
                filter_mode: 0,
                wrap_u: 1,
                wrap_v: 1,
                aniso_level: 1,
                render_target: false,
                random_write: false,
                auto_generate_mips: false,
                samples: 1,
                depth_bits: 0,
                layers: 1,
                dimension: 2,
            },
        );
    }
    Ok(true)
}
