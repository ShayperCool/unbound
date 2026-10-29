//! Remote play: the running game's picture as hardware-encoded H.264 and its sound as Opus in a
//! browser, and the browser's keyboard, mouse and touch back in the game.
//!
//! A [`Session`] serves a page beneath a secret path on its HTTP listener. The game hands it each
//! presented frame ([`Session::push_frame`]) and its audio mix ([`Session::push_audio`]) while
//! someone watches; frames are encoded on a thread of their own (NVENC) and fanned out to the
//! viewers, over WebRTC (RTP over UDP: works on a plain-HTTP LAN address) or over the page's
//! WebSocket (WebCodecs: localhost or HTTPS only). Viewer input is collected for the game to
//! take each frame ([`Session::take_input`]).
//!
//! Adapted from Unity Micro Editor's remote session.
mod audio;
mod encoder;
mod http;
mod input;
mod webrtc;

pub use audio::{AudioEncoder, Mixdown};
pub use encoder::{Encoded, Encoder, Parameters, codec_string};
pub use input::RemoteEvent;

use axum::body::Bytes;
use serde_json::{Value, json};
use std::{
    net::{IpAddr, Ipv4Addr, SocketAddr, TcpListener, UdpSocket},
    sync::{
        Arc, Mutex,
        atomic::{AtomicBool, AtomicU32, AtomicU64, AtomicUsize, Ordering},
        mpsc as std_mpsc,
    },
    thread,
    time::{Duration, Instant},
};
use tokio::sync::{broadcast, mpsc, oneshot, watch};

/// The largest frame side the encoder accepts.
const MAXIMUM_SIDE: usize = 4096;
/// Viewers may fall this many frames behind before they resume at a keyframe.
const VIEWER_BACKLOG: usize = 120;
/// Audio packets of 20 ms a viewer may fall behind before it skips them.
const AUDIO_BACKLOG: usize = 50;

/// How viewers receive the media.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Transport {
    /// RTP tracks over UDP, played by the browser's WebRTC stack: works on `http://<lan-ip>`.
    WebRtc,
    /// Frames over the page's WebSocket, decoded with WebCodecs: one TCP port, but the page
    /// needs a secure context (localhost or HTTPS).
    WebSocket,
}

#[derive(Debug, Clone)]
pub struct Options {
    /// The HTTP listener (`0.0.0.0:8790` serves the local network).
    pub listen: SocketAddr,
    pub transport: Transport,
    /// WebRTC: the local address its UDP socket binds and announces (default: the listener's, or
    /// this machine's local-network address when the listener takes every interface).
    pub webrtc_address: Option<IpAddr>,
    /// Frames per second the encoder is configured for.
    pub fps: u32,
    /// Ceiling of the video bitrate.
    pub bitrate_kbps: u32,
}

impl Default for Options {
    fn default() -> Self {
        Options {
            listen: SocketAddr::new(IpAddr::V4(Ipv4Addr::UNSPECIFIED), 8790),
            transport: Transport::WebRtc,
            webrtc_address: None,
            fps: 60,
            bitrate_kbps: 12000,
        }
    }
}

/// One encoded frame in the form viewers receive it.
pub(crate) struct Packet {
    pub keyframe: bool,
    /// Microseconds since the session started.
    pub timestamp: u64,
    pub wire: Bytes,
    // Where the access unit starts in `wire`.
    offset: usize,
}

impl Packet {
    /// The H.264 Annex-B access unit.
    pub(crate) fn data(&self) -> &[u8] {
        &self.wire[self.offset..]
    }
}

/// One Opus packet of 20 ms: the flags byte with [`AUDIO_FLAG`], microseconds since the session
/// started, the packet.
pub(crate) struct AudioPacket {
    pub timestamp: u64,
    pub wire: Bytes,
}

/// Marks an audio message among the binary messages of a viewer's socket.
const AUDIO_FLAG: u8 = 2;

impl AudioPacket {
    fn new(timestamp: u64, data: &[u8]) -> AudioPacket {
        let mut wire = Vec::with_capacity(9 + data.len());
        wire.push(AUDIO_FLAG);
        wire.extend_from_slice(&timestamp.to_le_bytes());
        wire.extend_from_slice(data);
        AudioPacket {
            timestamp,
            wire: wire.into(),
        }
    }

    pub(crate) fn data(&self) -> &[u8] {
        &self.wire[9..]
    }
}

// flags, encoder epoch, microseconds since the session started, picture size and the codec
// string a decoder is configured with, then the access unit.
fn packet(
    keyframe: bool,
    epoch: u32,
    timestamp: u64,
    width: u16,
    height: u16,
    codec: &str,
    data: &[u8],
) -> Packet {
    let mut out = Vec::with_capacity(18 + codec.len() + data.len());
    out.push(keyframe as u8);
    out.extend_from_slice(&epoch.to_le_bytes());
    out.extend_from_slice(&timestamp.to_le_bytes());
    out.extend_from_slice(&width.to_le_bytes());
    out.extend_from_slice(&height.to_le_bytes());
    out.push(codec.len() as u8);
    out.extend_from_slice(codec.as_bytes());
    let offset = out.len();
    out.extend_from_slice(data);
    Packet {
        keyframe,
        timestamp,
        wire: out.into(),
        offset,
    }
}

#[derive(Default)]
struct Status {
    streaming: bool,
    viewport: (u32, u32),
    encoder: &'static str,
    frames: u64,
    error: String,
    audio_error: String,
}

pub(crate) struct Shared {
    options: Options,
    pub(crate) token: String,
    address: SocketAddr,
    started: Instant,
    pub(crate) video: broadcast::Sender<Arc<Packet>>,
    pub(crate) audio: broadcast::Sender<Arc<AudioPacket>>,
    pub(crate) changed: watch::Sender<u64>,
    status: Mutex<Status>,
    epochs: AtomicU32,
    viewers: AtomicUsize,
    viewer_ids: AtomicU64,
    keyframe: AtomicBool,
    input: Mutex<Vec<RemoteEvent>>,
    // The WebRTC task and the UDP address it serves, with that transport.
    rtc: Option<(mpsc::UnboundedSender<webrtc::Command>, SocketAddr)>,
}

impl Shared {
    fn notify(&self) {
        self.changed.send_modify(|version| *version += 1);
    }

    pub(crate) fn request_keyframe(&self) {
        self.keyframe.store(true, Ordering::Relaxed);
    }

    /// Counts a viewer in and returns the id its messages carry.
    pub(crate) fn viewer_joined(&self) -> u64 {
        self.viewers.fetch_add(1, Ordering::Relaxed);
        self.request_keyframe();
        self.notify();
        tracing::info!("remote: a viewer joined");
        self.viewer_ids.fetch_add(1, Ordering::Relaxed) + 1
    }

    pub(crate) fn viewer_left(&self, viewer: u64) {
        if let Some((commands, _)) = &self.rtc {
            let _ = commands.send(webrtc::Command::Leave(viewer));
        }
        self.viewers.fetch_sub(1, Ordering::Relaxed);
        self.input.lock().unwrap().push(RemoteEvent::Release);
        self.notify();
        tracing::info!("remote: a viewer left");
    }

    pub(crate) fn uses_webrtc(&self) -> bool {
        self.rtc.is_some()
    }

    /// A viewer's WebRTC offer; the reply is the message to send back.
    pub(crate) async fn viewer_offer(&self, viewer: u64, offer: Value) -> String {
        let outcome = match &self.rtc {
            None => Err("this session does not use WebRTC".to_string()),
            Some((commands, _)) => {
                let (reply, answer) = oneshot::channel();
                let command = webrtc::Command::Offer {
                    viewer,
                    offer,
                    reply,
                };
                match commands.send(command) {
                    Ok(()) => answer
                        .await
                        .unwrap_or_else(|_| Err("the session ended".into())),
                    Err(_) => Err("the session ended".into()),
                }
            }
        };
        match outcome {
            Ok(answer) => json!({"answer": answer}),
            Err(error) => json!({"rtcError": error}),
        }
        .to_string()
    }

    /// A text message of a viewer: input events, a keyframe request after a decoder error, or
    /// the release of everything it holds down.
    pub(crate) fn viewer_message(&self, text: &str) {
        let Ok(message) = serde_json::from_str::<Value>(text) else {
            return;
        };
        if message.get("k").is_some() {
            self.request_keyframe();
        }
        let mut input = self.input.lock().unwrap();
        if message.get("r").is_some() {
            input.push(RemoteEvent::Release);
        }
        if let Some(events) = message.get("e").and_then(Value::as_array) {
            input.extend(input::translate(events));
            // A viewer that sends faster than the game reads keeps only the recent past.
            let excess = input.len().saturating_sub(4096);
            input.drain(..excess);
        }
    }

    /// What viewers are told whenever it changes.
    pub(crate) fn state_json(&self) -> String {
        let status = self.status.lock().unwrap();
        let error = if status.error.is_empty() {
            &status.audio_error
        } else {
            &status.error
        };
        json!({
            "playerConnected": status.streaming,
            "cursorLocked": false,
            "viewport": {"width": status.viewport.0, "height": status.viewport.1},
            "viewers": self.viewers.load(Ordering::Relaxed),
            "transport": match self.options.transport {
                Transport::WebRtc => "webrtc",
                Transport::WebSocket => "websocket",
            },
            "encoder": status.encoder,
            "error": error,
        })
        .to_string()
    }
}

/// A raw frame on its way to the encoder.
struct Frame {
    width: u16,
    height: u16,
    rgba: Vec<u8>,
}

/// A running session. Dropping it closes the listener and every viewer.
pub struct Session {
    shared: Arc<Shared>,
    runtime: Option<tokio::runtime::Runtime>,
    frames: std_mpsc::SyncSender<Frame>,
    sound: Mutex<Sound>,
}

/// This machine's address on its local network: the one that reaches the gateway of the main
/// routing table's default route (a VPN's policy routes do not count), else the address
/// traffic to other hosts leaves from.
pub fn lan_address() -> Option<IpAddr> {
    let via = |target: IpAddr| -> Option<IpAddr> {
        let socket = UdpSocket::bind((Ipv4Addr::UNSPECIFIED, 0)).ok()?;
        // Connecting a UDP socket selects the route without sending anything.
        socket.connect((target, 9)).ok()?;
        socket.local_addr().ok().map(|address| address.ip())
    };
    let gateway = std::fs::read_to_string("/proc/net/route")
        .ok()
        .and_then(|table| {
            table.lines().skip(1).find_map(|line| {
                let fields: Vec<&str> = line.split_whitespace().collect();
                if fields.len() < 3 || fields[1] != "00000000" {
                    return None;
                }
                let raw = u32::from_str_radix(fields[2], 16).ok()?;
                (raw != 0).then(|| IpAddr::V4(Ipv4Addr::from(raw.to_le_bytes())))
            })
        });
    gateway
        .and_then(via)
        .or_else(|| via(IpAddr::V4(Ipv4Addr::new(192, 0, 2, 1))))
}

/// A secret path segment: a hash of the moment, the process and a stack address.
fn new_token() -> String {
    let mut h = blake3::Hasher::new();
    let now = std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .unwrap_or_default();
    h.update(&now.as_nanos().to_le_bytes());
    h.update(&std::process::id().to_le_bytes());
    let local = 0u8;
    h.update(&(&local as *const u8 as usize).to_le_bytes());
    if let Ok(random) = std::fs::read("/proc/sys/kernel/random/uuid") {
        h.update(&random);
    }
    h.finalize().to_hex()[..24].to_string()
}

impl Session {
    /// Binds the listeners and starts serving.
    pub fn start(options: Options) -> Result<Session, String> {
        if !(1..=120).contains(&options.fps) {
            return Err("fps must be between 1 and 120".into());
        }
        let listener = TcpListener::bind(options.listen)
            .map_err(|error| format!("listen tcp {}: {error}", options.listen))?;
        let address = listener.local_addr().map_err(|error| error.to_string())?;
        let runtime = tokio::runtime::Builder::new_multi_thread()
            .worker_threads(2)
            .thread_name("unbound-remote")
            .enable_all()
            .build()
            .map_err(|error| error.to_string())?;
        let mut rtc = None;
        let mut rtc_task = None;
        if options.transport == Transport::WebRtc {
            let ip = match options.webrtc_address {
                Some(ip) => ip,
                None if !address.ip().is_unspecified() => address.ip(),
                None => lan_address().unwrap_or(IpAddr::V4(Ipv4Addr::LOCALHOST)),
            };
            let socket = UdpSocket::bind(SocketAddr::new(ip, 0))
                .map_err(|error| format!("listen udp {ip}: {error}"))?;
            socket
                .set_nonblocking(true)
                .map_err(|error| error.to_string())?;
            let media = socket.local_addr().map_err(|error| error.to_string())?;
            let (commands, receiver) = mpsc::unbounded_channel();
            rtc = Some((commands, media));
            rtc_task = Some((socket, media, receiver));
        }
        let shared = Arc::new(Shared {
            options,
            token: new_token(),
            address,
            started: Instant::now(),
            video: broadcast::channel(VIEWER_BACKLOG).0,
            audio: broadcast::channel(AUDIO_BACKLOG).0,
            changed: watch::channel(0).0,
            status: Mutex::default(),
            epochs: AtomicU32::new(0),
            viewers: AtomicUsize::new(0),
            viewer_ids: AtomicU64::new(0),
            keyframe: AtomicBool::new(false),
            input: Mutex::default(),
            rtc,
        });
        listener
            .set_nonblocking(true)
            .map_err(|error| error.to_string())?;
        {
            let _context = runtime.enter();
            let listener =
                tokio::net::TcpListener::from_std(listener).map_err(|error| error.to_string())?;
            let router = http::router(shared.clone());
            runtime.spawn(async move {
                let _ = axum::serve(listener, router).await;
            });
            if let Some((socket, media, commands)) = rtc_task {
                let socket =
                    tokio::net::UdpSocket::from_std(socket).map_err(|error| error.to_string())?;
                runtime.spawn(webrtc::run(shared.clone(), socket, media, commands));
            }
        }
        // One frame in flight: a frame the encoder is still busy with makes the next one wait
        // its turn, and one more is dropped.
        let (frames, receiver) = std_mpsc::sync_channel::<Frame>(1);
        {
            let shared = shared.clone();
            thread::Builder::new()
                .name("unbound-remote-video".into())
                .spawn(move || encode_frames(&shared, receiver))
                .map_err(|error| error.to_string())?;
        }
        Ok(Session {
            shared,
            runtime: Some(runtime),
            frames,
            sound: Mutex::default(),
        })
    }

    /// The address to open in a browser (it carries the session's secret). A listener on every
    /// interface is named by this machine's local-network address.
    pub fn url(&self) -> String {
        let mut address = self.shared.address;
        if address.ip().is_unspecified() {
            address.set_ip(lan_address().unwrap_or(IpAddr::V4(Ipv4Addr::LOCALHOST)));
        }
        format!("http://{address}/{}/", self.shared.token)
    }

    /// Whether anyone watches: frames and sound are only worth capturing then.
    pub fn watching(&self) -> bool {
        self.shared.viewers.load(Ordering::Relaxed) > 0
    }

    /// The game's presented picture, `width * height` RGBA texels, top row first. Dropped when
    /// the encoder is still busy with the previous one or nobody watches.
    pub fn push_frame(&self, width: u32, height: u32, rgba: Vec<u8>) {
        if !self.watching() {
            return;
        }
        // H.264 wants even sides: an odd last row or column is cut.
        let (even_w, even_h) = (width & !1, height & !1);
        if even_w < 2
            || even_h < 2
            || even_w as usize > MAXIMUM_SIDE
            || even_h as usize > MAXIMUM_SIDE
        {
            return;
        }
        if rgba.len() != (width * height * 4) as usize {
            return;
        }
        let rgba = if (even_w, even_h) == (width, height) {
            rgba
        } else {
            let mut cut = Vec::with_capacity((even_w * even_h * 4) as usize);
            for row in 0..even_h as usize {
                let start = row * width as usize * 4;
                cut.extend_from_slice(&rgba[start..start + even_w as usize * 4]);
            }
            cut
        };
        {
            let mut status = self.shared.status.lock().unwrap();
            if !status.streaming || status.viewport != (width, height) {
                status.streaming = true;
                status.viewport = (width, height);
                drop(status);
                self.shared.notify();
            }
        }
        let _ = self.frames.try_send(Frame {
            width: even_w as u16,
            height: even_h as u16,
            rgba,
        });
    }

    /// Interleaved samples of the game's audio mix at `rate` with `channels` channels.
    pub fn push_audio(&self, rate: u32, channels: usize, samples: &[f32]) {
        if !self.watching() || !(8_000..=192_000).contains(&rate) || !(1..=8).contains(&channels) {
            return;
        }
        self.sound
            .lock()
            .unwrap()
            .take(&self.shared, rate, channels, samples);
    }

    /// The viewers' input since the last call, in order.
    pub fn take_input(&self) -> Vec<RemoteEvent> {
        std::mem::take(&mut *self.shared.input.lock().unwrap())
    }
}

impl std::fmt::Debug for Session {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("Session").field("url", &self.url()).finish()
    }
}

impl Drop for Session {
    fn drop(&mut self) {
        if let Some(runtime) = self.runtime.take() {
            runtime.shutdown_background();
        }
    }
}

struct Active {
    width: u16,
    height: u16,
    epoch: u32,
    codec: String,
    encoder: Box<dyn Encoder>,
}

/// Encodes the frames the game hands over and gives them to the viewers.
fn encode_frames(shared: &Shared, frames: std_mpsc::Receiver<Frame>) {
    let mut active: Option<Active> = None;
    let mut retry = Instant::now();
    loop {
        // Nobody watching for a while: the encoder (and its CUDA context, hundreds of MB of
        // video memory) goes until the next viewer.
        let frame = match frames.recv_timeout(Duration::from_secs(2)) {
            Ok(frame) => frame,
            Err(std_mpsc::RecvTimeoutError::Timeout) => {
                if shared.viewers.load(Ordering::Relaxed) == 0 {
                    active = None;
                }
                continue;
            }
            Err(std_mpsc::RecvTimeoutError::Disconnected) => break,
        };
        if shared.viewers.load(Ordering::Relaxed) == 0 {
            active = None;
            continue;
        }
        let (width, height) = (frame.width, frame.height);
        if active
            .as_ref()
            .is_none_or(|active| (active.width, active.height) != (width, height))
        {
            active = None;
            if Instant::now() < retry {
                continue;
            }
            let parameters = Parameters {
                width: width.into(),
                height: height.into(),
                fps: shared.options.fps,
                bitrate: shared.options.bitrate_kbps * 1000,
            };
            match encoder::hardware(&parameters) {
                Ok(encoder) => {
                    let mut status = shared.status.lock().unwrap();
                    status.encoder = encoder.name();
                    status.error.clear();
                    active = Some(Active {
                        width,
                        height,
                        epoch: shared.epochs.fetch_add(1, Ordering::Relaxed) + 1,
                        codec: String::new(),
                        encoder,
                    });
                }
                Err(error) => {
                    retry = Instant::now() + Duration::from_secs(5);
                    tracing::warn!("remote: video encoder unavailable: {error}");
                    shared.status.lock().unwrap().error =
                        format!("video encoder unavailable: {error}");
                    shared.notify();
                    continue;
                }
            }
        }
        let current = active.as_mut().expect("encoder was just ensured");
        // A stream's first frame carries the parameter sets the codec string is read from.
        let force = shared.keyframe.swap(false, Ordering::Relaxed) || current.codec.is_empty();
        let timestamp = shared.started.elapsed().as_micros() as u64;
        match current.encoder.encode(&frame.rgba, force, timestamp) {
            Ok(encoded) => {
                if encoded.keyframe
                    && let Some(codec) = codec_string(&encoded.data)
                {
                    current.codec = codec;
                }
                if current.codec.is_empty() {
                    continue;
                }
                let packet = packet(
                    encoded.keyframe,
                    current.epoch,
                    timestamp,
                    width,
                    height,
                    &current.codec,
                    &encoded.data,
                );
                shared.status.lock().unwrap().frames += 1;
                // No receiver only means the last viewer has just left.
                let _ = shared.video.send(Arc::new(packet));
            }
            Err(error) => {
                active = None;
                retry = Instant::now() + Duration::from_secs(2);
                tracing::warn!("remote: video encoding failed: {error}");
                shared.status.lock().unwrap().error = format!("video encoding failed: {error}");
                shared.notify();
            }
        }
    }
}

/// The game's sound on its way to the encoder.
#[derive(Default)]
struct Sound {
    mixdown: Mixdown,
    encoder: Option<Box<dyn AudioEncoder>>,
    retry: Option<Instant>,
    // When the first packet of the current run was due, in microseconds since the session
    // started, and the stereo samples encoded since.
    clock: Option<(u64, u64)>,
}

impl Sound {
    // Encodes what a chunk of samples completes and hands it to the viewers. Packets are timed
    // by their samples; a run that drifts from the session's clock starts again from it.
    fn take(&mut self, shared: &Shared, rate: u32, channels: usize, samples: &[f32]) {
        let packets = self.mixdown.push(rate, channels, samples);
        for pcm in packets {
            if self.encoder.is_none() {
                if self.retry.is_some_and(|retry| Instant::now() < retry) {
                    return;
                }
                match audio::opus() {
                    Ok(encoder) => {
                        shared.status.lock().unwrap().audio_error.clear();
                        self.encoder = Some(encoder);
                    }
                    Err(error) => {
                        self.retry = Some(Instant::now() + Duration::from_secs(5));
                        shared.status.lock().unwrap().audio_error =
                            format!("audio encoder unavailable: {error}");
                        return;
                    }
                }
            }
            let now = shared.started.elapsed().as_micros() as u64;
            let (start, encoded) = *self.clock.get_or_insert((now, 0));
            let due = start + encoded * 1_000_000 / audio::SAMPLE_RATE as u64;
            let timestamp = if due.abs_diff(now) > 500_000 {
                self.clock = Some((now, 0));
                now
            } else {
                due
            };
            if let Some((_, encoded)) = &mut self.clock {
                *encoded += audio::FRAME as u64;
            }
            let encoder = self.encoder.as_mut().expect("encoder was just ensured");
            match encoder.encode(&pcm) {
                Ok(data) => {
                    let _ = shared
                        .audio
                        .send(Arc::new(AudioPacket::new(timestamp, &data)));
                }
                Err(error) => {
                    self.encoder = None;
                    self.retry = Some(Instant::now() + Duration::from_secs(2));
                    shared.status.lock().unwrap().audio_error =
                        format!("audio encoding failed: {error}");
                    return;
                }
            }
        }
    }
}
