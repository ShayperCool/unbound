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

/// A clip's properties: size, frame rate (as a fraction too), frame count, length, audio tracks
/// (their count, and per track its channels, sample rate and language), pixel aspect ratio.
pub fn probe(path: &str) -> Result<Json> {
    let output = Command::new("ffprobe")
        .args(["-v", "error", "-count_packets", "-show_entries"])
        .arg(
            "stream=codec_type,width,height,r_frame_rate,nb_read_packets,sample_aspect_ratio,channels,sample_rate\
             :stream_tags=language:format=duration",
        )
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
    clip_properties(path, &root)
}

/// [`probe`]'s answer from ffprobe's JSON (`streams`, `format`).
fn clip_properties(path: &str, root: &Json) -> Result<Json> {
    let streams = root["streams"].as_array().cloned().unwrap_or_default();
    let video = streams
        .iter()
        .find(|s| s["codec_type"] == "video")
        .with_context(|| format!("{path} has no video stream"))?;
    let audio_track_info: Vec<Json> = streams
        .iter()
        .filter(|s| s["codec_type"] == "audio")
        .map(audio_track)
        .collect();
    let audio_tracks = audio_track_info.len();
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
        "audioTrackInfo": audio_track_info,
    }))
}

/// An audio stream's format, in stream order: its channel count, its sample rate in Hz (ffprobe
/// writes it as text) and its container's language tag as written ("" without one); 0 for what
/// ffprobe does not report.
fn audio_track(stream: &Json) -> Json {
    let sample_rate = match &stream["sample_rate"] {
        Json::String(text) => text.parse::<u64>().unwrap_or(0),
        other => other.as_u64().unwrap_or(0),
    };
    json!({
        "channels": stream["channels"].as_u64().unwrap_or(0),
        "sampleRate": sample_rate,
        "language": stream["tags"]["language"].as_str().unwrap_or(""),
    })
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
                wrap_w: 0,
                aniso_level: 1,
                render_target: false,
                random_write: false,
                auto_generate_mips: false,
                defer_mips: false,
                retain_resolve: false,
                bind_ms: false,
                depth_compare: None,
                samples: 1,
                depth_bits: 0,
                layers: 1,
                dimension: 2,
                logical: None,
                screen_order: false,
                blue_first: false,
                packed: 0,
            },
        );
    }
    Ok(true)
}

#[cfg(test)]
mod tests {
    use super::*;

    /// ffprobe's JSON for a clip with a video stream and two audio tracks of different formats
    /// (stereo English at 48 kHz, 5.1 French at 44.1 kHz), as `probe`'s `-show_entries` asks.
    const TWO_TRACKS: &str = r#"{
        "programs": [],
        "streams": [
            {"codec_type": "video", "width": 64, "height": 48, "r_frame_rate": "10/1",
             "sample_aspect_ratio": "1:1", "nb_read_packets": "30"},
            {"codec_type": "audio", "sample_rate": "48000", "channels": 2, "r_frame_rate": "0/0",
             "nb_read_packets": "141", "tags": {"language": "eng"}},
            {"codec_type": "audio", "sample_rate": "44100", "channels": 6, "r_frame_rate": "0/0",
             "nb_read_packets": "130", "tags": {"language": "fre"}}
        ],
        "format": {"duration": "3.000000"}
    }"#;

    /// A video-only clip: no audio streams, no tags.
    const VIDEO_ONLY: &str = r#"{
        "programs": [],
        "streams": [
            {"codec_type": "video", "width": 64, "height": 48, "r_frame_rate": "10/1",
             "nb_read_packets": "30"}
        ],
        "format": {"duration": "3.000000"}
    }"#;

    fn parse(text: &str) -> Json {
        clip_properties("clip.webm", &serde_json::from_str(text).unwrap()).unwrap()
    }

    #[test]
    fn audio_tracks_report_their_channels_rate_and_language_in_stream_order() {
        let clip = parse(TWO_TRACKS);
        assert_eq!(clip["audioTracks"], 2);
        assert_eq!(
            clip["audioTrackInfo"],
            json!([
                {"channels": 2, "sampleRate": 48000, "language": "eng"},
                {"channels": 6, "sampleRate": 44100, "language": "fre"},
            ])
        );
    }

    #[test]
    fn a_track_without_a_language_tag_reports_an_empty_language() {
        let text = TWO_TRACKS.replace(r#", "tags": {"language": "fre"}"#, "");
        let clip = parse(&text);
        assert_eq!(clip["audioTrackInfo"][0]["language"], "eng");
        assert_eq!(clip["audioTrackInfo"][1]["language"], "");
        assert_eq!(clip["audioTrackInfo"][1]["channels"], 6);
    }

    #[test]
    fn a_video_only_clip_has_no_audio_track_info() {
        let clip = parse(VIDEO_ONLY);
        assert_eq!(clip["audioTracks"], 0);
        assert_eq!(clip["audioTrackInfo"], json!([]));
    }

    /// The fields earlier hosts answered keep their names and values; only `audioTrackInfo` is new.
    #[test]
    fn the_existing_fields_are_unchanged() {
        for (text, audio) in [(TWO_TRACKS, 2), (VIDEO_ONLY, 0)] {
            let clip = parse(text);
            let mut keys: Vec<&str> = clip
                .as_object()
                .unwrap()
                .keys()
                .map(String::as_str)
                .collect();
            keys.sort_unstable();
            assert_eq!(
                keys,
                [
                    "aspectDenominator",
                    "aspectNumerator",
                    "audioTrackInfo",
                    "audioTracks",
                    "frameCount",
                    "frameRate",
                    "frameRateDenominator",
                    "frameRateNumerator",
                    "height",
                    "length",
                    "width",
                ]
            );
            assert_eq!(clip["width"], 64);
            assert_eq!(clip["height"], 48);
            assert_eq!(clip["frameRate"], 10.0);
            assert_eq!(clip["frameRateNumerator"], 10);
            assert_eq!(clip["frameRateDenominator"], 1);
            assert_eq!(clip["frameCount"], 30);
            assert_eq!(clip["length"], 3.0);
            assert_eq!(clip["audioTracks"], audio);
            assert_eq!(clip["aspectNumerator"], 1);
            assert_eq!(clip["aspectDenominator"], 1);
        }
    }

    #[test]
    fn a_clip_without_a_video_stream_is_an_error() {
        let text = r#"{"streams": [{"codec_type": "audio", "channels": 2, "sample_rate": "48000"}], "format": {}}"#;
        let error =
            clip_properties("sound.webm", &serde_json::from_str(text).unwrap()).unwrap_err();
        assert!(error.to_string().contains("has no video stream"), "{error}");
    }
}

/// A video's audio track decoded by ffmpeg into interleaved floats at the track's rate and
/// channel count, read by the mixer as a streamed clip's data (see [`AudioStream::provide`]). The
/// player keeps it in step with its pictures: it plays or stays silent, starts again at a time
/// (start, seek, loop), and at a playback speed above 1 skips what the faster pictures leave
/// behind, so the sound keeps its pitch (measured: tests/projects/videoaudio).
pub struct AudioStream {
    path: String,
    track: usize,
    channels: usize,
    rate: u32,
    state: Mutex<AudioState>,
}

#[derive(Default)]
struct AudioState {
    playing: bool,
    speed: f64,
    /// Decoded samples not read yet, shared with the reading thread of `generation`.
    queue: std::sync::Arc<Mutex<std::collections::VecDeque<f32>>>,
    process: Option<Child>,
    /// Bumped at each restart, so an earlier reading thread stops adding to the queue.
    generation: std::sync::Arc<std::sync::atomic::AtomicU64>,
    /// Samples a speed above 1 still has to skip.
    skip: f64,
    /// The track time (in frames) of the next frame given out: where decoding started plus what
    /// was given out or skipped since.
    position: f64,
}

/// At most this many seconds are decoded ahead.
const AUDIO_AHEAD_SECONDS: f64 = 2.0;
/// How far the sound may be from the pictures before it starts again at their time.
const AUDIO_DRIFT_SECONDS: f64 = 0.2;

impl AudioStream {
    /// Starts decoding again at `time` seconds, dropping what was decoded.
    fn restart(&self, state: &mut AudioState, time: f64) -> Result<()> {
        if let Some(mut child) = state.process.take() {
            let _ = child.kill();
            let _ = child.wait();
        }
        let generation = state
            .generation
            .fetch_add(1, std::sync::atomic::Ordering::SeqCst)
            + 1;
        state.queue.lock().unwrap().clear();
        state.skip = 0.0;
        state.position = time.max(0.0) * self.rate as f64;
        let mut child = Command::new("ffmpeg")
            .args(["-v", "error", "-nostdin", "-ss", &format!("{:.6}", time.max(0.0)), "-i", &self.path])
            .args(["-vn", "-map", &format!("0:a:{}", self.track), "-f", "f32le"])
            .args(["-ac", &self.channels.to_string(), "-ar", &self.rate.to_string(), "-"])
            .stdin(Stdio::null())
            .stdout(Stdio::piped())
            .stderr(Stdio::null())
            .spawn()
            .context("Unbound plays a video's sound through ffmpeg; `ffmpeg` could not be run (is ffmpeg installed?)")?;
        let mut stdout = child.stdout.take().context("no ffmpeg output")?;
        state.process = Some(child);
        let queue = state.queue.clone();
        let current = state.generation.clone();
        let limit = (AUDIO_AHEAD_SECONDS * self.rate as f64) as usize * self.channels;
        std::thread::spawn(move || {
            let mut bytes = vec![0u8; 4096];
            let mut carry = Vec::<u8>::new();
            loop {
                if current.load(std::sync::atomic::Ordering::SeqCst) != generation {
                    return;
                }
                if queue.lock().unwrap().len() >= limit {
                    std::thread::sleep(std::time::Duration::from_millis(5));
                    continue;
                }
                let n = match stdout.read(&mut bytes) {
                    Ok(0) | Err(_) => return,
                    Ok(n) => n,
                };
                carry.extend_from_slice(&bytes[..n]);
                let whole = carry.len() / 4 * 4;
                let mut q = queue.lock().unwrap();
                if current.load(std::sync::atomic::Ordering::SeqCst) != generation {
                    return;
                }
                q.extend(
                    carry[..whole]
                        .chunks_exact(4)
                        .map(|b| f32::from_le_bytes([b[0], b[1], b[2], b[3]])),
                );
                drop(q);
                carry.drain(..whole);
            }
        });
        Ok(())
    }

    /// Fills `out` (interleaved frames) for the mixer: silence while not playing or before the
    /// decoder has caught up; at a speed above 1 the frames the pictures skip are dropped after.
    pub fn provide(&self, out: &mut [f32]) {
        out.fill(0.0);
        let mut state = self.state.lock().unwrap();
        if !state.playing {
            return;
        }
        let mut queue = state.queue.lock().unwrap();
        let n = out.len().min(queue.len());
        for (slot, sample) in out[..n].iter_mut().zip(queue.drain(..n)) {
            *slot = sample;
        }
        drop(queue);
        let frames = out.len() / self.channels.max(1);
        state.position += frames as f64;
        if state.speed > 1.0 {
            state.skip += (state.speed - 1.0) * frames as f64;
            let whole = state.skip.floor() as usize;
            state.skip -= whole as f64;
            {
                let mut queue = state.queue.lock().unwrap();
                let drop_samples = (whole * self.channels).min(queue.len());
                queue.drain(..drop_samples);
            }
            state.position += whole as f64;
        }
    }

    /// The player's state: playing or not, its speed, a time to start decoding again at, and the
    /// pictures' time, which starts decoding again there when the sound is more than
    /// [`AUDIO_DRIFT_SECONDS`] away from it.
    pub fn control(
        &self,
        playing: Option<bool>,
        speed: Option<f64>,
        restart: Option<f64>,
        sync: Option<f64>,
    ) -> Result<()> {
        let mut state = self.state.lock().unwrap();
        if let Some(time) = restart {
            self.restart(&mut state, time)?;
        } else if let Some(time) = sync {
            if (state.position / self.rate as f64 - time).abs() > AUDIO_DRIFT_SECONDS {
                self.restart(&mut state, time)?;
            }
        }
        if let Some(playing) = playing {
            state.playing = playing;
        }
        if let Some(speed) = speed {
            state.speed = speed;
        }
        Ok(())
    }
}

impl Drop for AudioStream {
    fn drop(&mut self) {
        let state = self.state.get_mut().unwrap();
        state
            .generation
            .fetch_add(1, std::sync::atomic::Ordering::SeqCst);
        if let Some(mut child) = state.process.take() {
            let _ = child.kill();
            let _ = child.wait();
        }
    }
}

fn audio_streams() -> &'static Mutex<HashMap<u64, std::sync::Arc<AudioStream>>> {
    static STREAMS: OnceLock<Mutex<HashMap<u64, std::sync::Arc<AudioStream>>>> = OnceLock::new();
    STREAMS.get_or_init(|| Mutex::new(HashMap::new()))
}

/// A decoder of track `track` of a video for the mixer's streamed clip; its id and the clip's
/// data source.
pub fn audio_open(
    path: &str,
    track: usize,
    channels: usize,
    rate: u32,
) -> (u64, std::sync::Arc<AudioStream>) {
    static NEXT: std::sync::atomic::AtomicU64 = std::sync::atomic::AtomicU64::new(1);
    let id = NEXT.fetch_add(1, std::sync::atomic::Ordering::Relaxed);
    let stream = std::sync::Arc::new(AudioStream {
        path: path.to_string(),
        track,
        channels: channels.max(1),
        rate: rate.max(1),
        state: Mutex::new(AudioState {
            speed: 1.0,
            ..AudioState::default()
        }),
    });
    audio_streams().lock().unwrap().insert(id, stream.clone());
    (id, stream)
}

pub fn audio_control(
    id: u64,
    playing: Option<bool>,
    speed: Option<f64>,
    restart: Option<f64>,
    sync: Option<f64>,
) -> Result<()> {
    let stream = audio_streams()
        .lock()
        .unwrap()
        .get(&id)
        .cloned()
        .context("no such video audio stream")?;
    stream.control(playing, speed, restart, sync)
}

pub fn audio_close(id: u64) {
    audio_streams().lock().unwrap().remove(&id);
}

#[cfg(test)]
mod audio_stream_tests {
    use super::*;

    fn stream(samples: &[f32], playing: bool, speed: f64) -> AudioStream {
        let state = AudioState {
            playing,
            speed,
            ..AudioState::default()
        };
        state.queue.lock().unwrap().extend(samples.iter().copied());
        AudioStream {
            path: String::new(),
            track: 0,
            channels: 2,
            rate: 10,
            state: Mutex::new(state),
        }
    }

    #[test]
    fn a_playing_stream_gives_its_frames_and_counts_them() {
        let samples: Vec<f32> = (1..=8).map(|v| v as f32).collect();
        let s = stream(&samples, true, 1.0);
        let mut out = [0.0; 6];
        s.provide(&mut out);
        assert_eq!(out, [1.0, 2.0, 3.0, 4.0, 5.0, 6.0]);
        // Past what was decoded: silence for the rest.
        s.provide(&mut out);
        assert_eq!(out, [7.0, 8.0, 0.0, 0.0, 0.0, 0.0]);
        assert_eq!(s.state.lock().unwrap().position, 6.0);
    }

    #[test]
    fn a_paused_stream_is_silent_and_keeps_its_place() {
        let s = stream(&[1.0, 2.0, 3.0, 4.0], false, 1.0);
        let mut out = [9.0; 4];
        s.provide(&mut out);
        assert_eq!(out, [0.0; 4]);
        assert_eq!(s.state.lock().unwrap().queue.lock().unwrap().len(), 4);
        assert_eq!(s.state.lock().unwrap().position, 0.0);
    }

    #[test]
    fn at_double_speed_the_frames_after_each_read_are_skipped() {
        // Two-channel frames 1..=8: a read of two frames gives frames 1-2 and skips 3-4.
        let samples: Vec<f32> = (1..=8).flat_map(|f| [f as f32, -(f as f32)]).collect();
        let s = stream(&samples, true, 2.0);
        let mut out = [0.0; 4];
        s.provide(&mut out);
        assert_eq!(out, [1.0, -1.0, 2.0, -2.0]);
        s.provide(&mut out);
        assert_eq!(out, [5.0, -5.0, 6.0, -6.0]);
        assert_eq!(s.state.lock().unwrap().position, 8.0);
    }
}
