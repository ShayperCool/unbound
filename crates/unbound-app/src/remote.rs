//! Remote play ([`unbound_remote::Session`]) in the windowed app: each presented frame goes to the
//! session while someone watches, the viewers' input joins the window's, and the game's sound
//! plays in the browser (the session is the audio output device).
use crate::input::{self, KeySet};
use anyhow::Result;
use bevy::{
    prelude::*,
    render::view::window::screenshot::{Screenshot, ScreenshotCaptured},
};
use std::{
    sync::{
        Arc,
        atomic::{AtomicBool, Ordering},
    },
    thread,
    time::{Duration, Instant},
};
use unbound_remote::{RemoteEvent, Session};
use unbound_runtime::audio::{AudioDevice, DeviceOpener, RenderFn};

/// What the viewers hold down and did since the last frame.
#[derive(Default)]
pub struct RemoteInput {
    /// The pointer, as fractions of the picture from its top-left corner.
    pub pointer: Option<Vec2>,
    /// Whether the pointer moved or a button changed this frame.
    pub active: bool,
    /// Held buttons, as `FrameInput::mouse_buttons` bits.
    pub buttons: u32,
    pub keys: KeySet,
    /// What the game sees this frame: the held buttons and keys, and those pressed and released
    /// again since the last frame (a quick click must last a frame to be seen).
    pub shown_buttons: u32,
    pub shown_keys: KeySet,
    /// Presses and releases this frame: Unity key, pressed, typed text.
    pub key_events: Vec<(u16, bool, Option<char>)>,
    pub scroll: Vec2,
}

impl RemoteInput {
    /// Takes the viewers' events since the last frame.
    pub fn update(&mut self, events: Vec<RemoteEvent>) {
        self.active = false;
        self.key_events.clear();
        self.scroll = Vec2::ZERO;
        let mut pressed_buttons = 0u32;
        let mut pressed_keys = KeySet::default();
        for event in events {
            match event {
                RemoteEvent::Move { x, y } => {
                    self.pointer = Some(Vec2::new(x, y));
                    self.active = true;
                }
                RemoteEvent::Button { button, down, at } => {
                    if let Some((x, y)) = at {
                        self.pointer = Some(Vec2::new(x, y));
                    }
                    // `FrameInput` bits: left, right, middle, back, forward; DOM: left, middle,
                    // right, back, forward.
                    let bit = match button {
                        0 => 0,
                        1 => 2,
                        2 => 1,
                        other => other as u32,
                    };
                    if down {
                        self.buttons |= 1 << bit;
                        pressed_buttons |= 1 << bit;
                    } else {
                        self.buttons &= !(1 << bit);
                    }
                    self.active = true;
                }
                RemoteEvent::Scroll { x, y } => self.scroll += Vec2::new(x, y),
                RemoteEvent::Key { code, down } => {
                    let (key, text) = input::dom_key(&code);
                    if key == 0 {
                        continue;
                    }
                    if down {
                        self.keys.set(key);
                        pressed_keys.set(key);
                    } else {
                        self.keys.clear(key);
                    }
                    self.key_events.push((key, down, text.filter(|_| down)));
                }
                RemoteEvent::Release => {
                    self.buttons = 0;
                    for code in 0..512u16 {
                        if self.keys.contains(code) {
                            self.key_events.push((code, false, None));
                        }
                    }
                    self.keys = KeySet::default();
                }
            }
        }
        self.shown_buttons = self.buttons | pressed_buttons;
        self.shown_keys = self.keys.clone();
        for code in 0..512u16 {
            if pressed_keys.contains(code) {
                self.shown_keys.set(code);
            }
        }
    }
}

/// When the capture in flight was requested (none: no capture in flight).
pub type CaptureState = Arc<std::sync::Mutex<Option<Instant>>>;

/// Captures the window's next presented image for the session, unless a capture is in flight. A
/// capture that never completes (the window was not presented) is given up after a second.
pub fn capture(commands: &mut Commands, session: &Arc<Session>, in_flight: &CaptureState) {
    if !session.watching() {
        return;
    }
    {
        let mut state = in_flight.lock().unwrap();
        if state.is_some_and(|at| at.elapsed() < Duration::from_secs(1)) {
            return;
        }
        *state = Some(Instant::now());
    }
    let session = session.clone();
    let in_flight = in_flight.clone();
    commands.spawn(Screenshot::primary_window()).observe(
        move |captured: On<ScreenshotCaptured>| {
            match rgba(&captured.image) {
                Some((width, height, rgba)) => session.push_frame(width, height, rgba),
                None => tracing::warn!(
                    "remote: the window's image ({:?}) cannot be streamed",
                    captured.image.texture_descriptor.format
                ),
            }
            *in_flight.lock().unwrap() = None;
        },
    );
}

/// A captured window image as RGBA, top row first.
fn rgba(image: &Image) -> Option<(u32, u32, Vec<u8>)> {
    use bevy::render::render_resource::TextureFormat;
    let data = image.data.as_ref()?;
    let mut out = Vec::with_capacity(data.len());
    match image.texture_descriptor.format {
        TextureFormat::Bgra8Unorm | TextureFormat::Bgra8UnormSrgb => {
            for px in data.as_chunks::<4>().0 {
                out.extend_from_slice(&[px[2], px[1], px[0], 255]);
            }
        }
        TextureFormat::Rgba8Unorm | TextureFormat::Rgba8UnormSrgb => {
            for px in data.as_chunks::<4>().0 {
                out.extend_from_slice(&[px[0], px[1], px[2], 255]);
            }
        }
        _ => return None,
    }
    Some((image.width(), image.height(), out))
}

/// The session as the game's audio output: a thread renders the mix in 10 ms chunks at 48 kHz on
/// the wall clock and sends it to the viewers.
pub struct RemoteAudio(pub Arc<Session>);

const RATE: u32 = 48_000;

struct Device {
    stop: Arc<AtomicBool>,
}

impl AudioDevice for Device {
    fn sample_rate(&self) -> u32 {
        RATE
    }

    fn name(&self) -> String {
        "the remote viewers' browser".into()
    }
}

impl Drop for Device {
    fn drop(&mut self) {
        self.stop.store(true, Ordering::Release);
    }
}

impl DeviceOpener for RemoteAudio {
    fn open(
        &self,
        _sample_rate: Option<u32>,
        _buffer_size: u32,
        mut render: RenderFn,
    ) -> Result<Box<dyn AudioDevice>> {
        let stop = Arc::new(AtomicBool::new(false));
        let session = self.0.clone();
        let stopped = stop.clone();
        thread::Builder::new()
            .name("unbound-remote-audio".into())
            .spawn(move || {
                const CHUNK: usize = (RATE / 100) as usize;
                let mut buffer = vec![0f32; CHUNK * 2];
                let mut next = Instant::now();
                while !stopped.load(Ordering::Acquire) {
                    buffer.fill(0.0);
                    render(&mut buffer, 2);
                    session.push_audio(RATE, 2, &buffer);
                    next += Duration::from_millis(10);
                    let now = Instant::now();
                    if next > now {
                        thread::sleep(next - now);
                    } else if now - next > Duration::from_millis(200) {
                        // Fell far behind (the machine stalled): resume from now.
                        next = now;
                    }
                }
            })?;
        Ok(Box::new(Device { stop }))
    }
}
