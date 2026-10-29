//! Gamepads and joysticks, as the Unity Editor reads them on Linux: through SDL (Unity embeds
//! SDL2 with its own controller database; Unbound loads the system's SDL2 and gives it Unity's
//! mappings, `data/profiles/sdl-gamecontrollerdb-<family>.txt`). The managed engine polls once per
//! frame and turns the snapshots into Input System devices and legacy joystick input.

use std::collections::HashMap;
use std::ffi::{CStr, CString, c_char, c_int, c_void};
use std::sync::Mutex;

use serde_json::{Value, json};

const SDL_INIT_JOYSTICK: u32 = 0x200;
const SDL_INIT_GAMECONTROLLER: u32 = 0x2000;
const SDL_INIT_EVENTS: u32 = 0x4000;
/// SDL's controller buttons (A..DPAD_RIGHT) and axes (LEFTX..TRIGGERRIGHT).
const CONTROLLER_BUTTONS: c_int = 15;
const CONTROLLER_AXES: c_int = 6;

/// The controller databases of the Unity families, newest last.
const MAPPINGS: &[(&str, &str)] = &[(
    "6000.0",
    include_str!("../../../data/profiles/sdl-gamecontrollerdb-6000.0.txt"),
)];

macro_rules! sdl_api {
    ($($name:ident: fn($($arg:ty),*) $(-> $ret:ty)?;)*) => {
        #[allow(non_snake_case)]
        struct Api {
            _library: libloading::Library,
            $($name: unsafe extern "C" fn($($arg),*) $(-> $ret)?,)*
        }

        impl Api {
            unsafe fn load(library: libloading::Library) -> Result<Self, libloading::Error> {
                Ok(Self {
                    $($name: unsafe { *library.get(concat!(stringify!($name), "\0").as_bytes())? },)*
                    _library: library,
                })
            }
        }
    };
}

sdl_api! {
    SDL_SetHint: fn(*const c_char, *const c_char) -> c_int;
    SDL_Init: fn(u32) -> c_int;
    SDL_GetError: fn() -> *const c_char;
    SDL_GameControllerAddMapping: fn(*const c_char) -> c_int;
    SDL_PumpEvents: fn();
    SDL_FlushEvents: fn(u32, u32);
    SDL_NumJoysticks: fn() -> c_int;
    SDL_JoystickGetDeviceInstanceID: fn(c_int) -> i32;
    SDL_IsGameController: fn(c_int) -> c_int;
    SDL_GameControllerOpen: fn(c_int) -> *mut c_void;
    SDL_GameControllerGetJoystick: fn(*mut c_void) -> *mut c_void;
    SDL_GameControllerName: fn(*mut c_void) -> *const c_char;
    SDL_GameControllerGetButton: fn(*mut c_void, c_int) -> u8;
    SDL_GameControllerGetAxis: fn(*mut c_void, c_int) -> i16;
    SDL_GameControllerClose: fn(*mut c_void);
    SDL_JoystickOpen: fn(c_int) -> *mut c_void;
    SDL_JoystickClose: fn(*mut c_void);
    SDL_JoystickName: fn(*mut c_void) -> *const c_char;
    SDL_JoystickGetAttached: fn(*mut c_void) -> c_int;
    SDL_JoystickNumAxes: fn(*mut c_void) -> c_int;
    SDL_JoystickNumButtons: fn(*mut c_void) -> c_int;
    SDL_JoystickNumHats: fn(*mut c_void) -> c_int;
    SDL_JoystickGetAxis: fn(*mut c_void, c_int) -> i16;
    SDL_JoystickGetButton: fn(*mut c_void, c_int) -> u8;
    SDL_JoystickGetHat: fn(*mut c_void, c_int) -> u8;
    SDL_JoystickGetVendor: fn(*mut c_void) -> u16;
    SDL_JoystickGetProduct: fn(*mut c_void) -> u16;
    SDL_JoystickGetProductVersion: fn(*mut c_void) -> u16;
}

struct Device {
    /// The controller when SDL knows the device's mapping, else null.
    controller: *mut c_void,
    joystick: *mut c_void,
    name: String,
    manufacturer: String,
    vendor: u16,
    product: u16,
    version: u16,
}

struct Sdl {
    api: Api,
    devices: HashMap<i32, Device>,
    /// Counts the changes to the attached set: the managed side asks for the devices' names and
    /// ids (`poll`) only when it moved.
    generation: u32,
    /// Instance ids attached and detached since the last `poll`.
    added: Vec<i32>,
    removed: Vec<i32>,
}

// SDL's handles are used under the mutex only.
unsafe impl Send for Sdl {}

enum State {
    Uninitialized,
    Unavailable,
    Ready(Box<Sdl>),
}

static STATE: Mutex<State> = Mutex::new(State::Uninitialized);

fn text(ptr: *const c_char) -> String {
    if ptr.is_null() {
        String::new()
    } else {
        unsafe { CStr::from_ptr(ptr) }
            .to_string_lossy()
            .into_owned()
    }
}

fn mappings(family: &str) -> &'static str {
    MAPPINGS
        .iter()
        .find(|(f, _)| *f == family)
        .or_else(|| MAPPINGS.last())
        .map(|(_, m)| *m)
        .unwrap_or("")
}

fn init(family: &str) -> Option<Box<Sdl>> {
    let names: &[&str] = if cfg!(target_os = "macos") {
        // Homebrew's prefixes are not on the default search path.
        &[
            "libSDL2-2.0.0.dylib",
            "libSDL2.dylib",
            "/opt/homebrew/lib/libSDL2-2.0.0.dylib",
            "/usr/local/lib/libSDL2-2.0.0.dylib",
        ]
    } else {
        &["libSDL2-2.0.so.0", "libSDL2-2.0.so", "libSDL2.so"]
    };
    let library = names
        .iter()
        .find_map(|name| unsafe { libloading::Library::new(name) }.ok());
    let Some(library) = library else {
        tracing::warn!(
            "libSDL2 is not installed: gamepads and joysticks are not read (install SDL2 to use them)"
        );
        return None;
    };
    let api = match unsafe { Api::load(library) } {
        Ok(api) => api,
        Err(e) => {
            tracing::warn!(
                "libSDL2 lacks a function Unbound needs ({e}): gamepads and joysticks are not read"
            );
            return None;
        }
    };
    unsafe {
        // The Editor reads its pads whether or not it has the focus.
        let hint = |name: &str, value: &str| {
            let (name, value) = (CString::new(name).unwrap(), CString::new(value).unwrap());
            (api.SDL_SetHint)(name.as_ptr(), value.as_ptr());
        };
        hint("SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS", "1");
        if cfg!(target_os = "macos") {
            // SDL's HIDAPI backend runs the main thread's default run loop while it enumerates
            // devices, from inside the window's event handling, which the windowing layer does not
            // allow (it panics); the IOKit backend runs a run loop mode of its own.
            hint("SDL_JOYSTICK_HIDAPI", "0");
        }
        if (api.SDL_Init)(SDL_INIT_JOYSTICK | SDL_INIT_GAMECONTROLLER | SDL_INIT_EVENTS) != 0 {
            tracing::warn!(
                "SDL could not start its joystick subsystem: {}",
                text((api.SDL_GetError)())
            );
            return None;
        }
        // Unity's mappings replace SDL's own for the same pads (and give them Unity's names).
        for line in mappings(family).lines().filter(|l| !l.trim().is_empty()) {
            if let Ok(line) = CString::new(line) {
                (api.SDL_GameControllerAddMapping)(line.as_ptr());
            }
        }
    }
    Some(Box::new(Sdl {
        api,
        devices: HashMap::new(),
        generation: 0,
        added: Vec::new(),
        removed: Vec::new(),
    }))
}

/// The manufacturer udev's USB database names for a vendor ("Microsoft Corp." for 045e).
fn manufacturer(vendor: u16) -> String {
    use std::sync::OnceLock;
    static VENDORS: OnceLock<HashMap<u16, String>> = OnceLock::new();
    let vendors = VENDORS.get_or_init(|| {
        let mut map = HashMap::new();
        for path in [
            "/usr/lib/udev/hwdb.d/20-usb-vendor-model.hwdb",
            "/lib/udev/hwdb.d/20-usb-vendor-model.hwdb",
        ] {
            let Ok(db) = std::fs::read_to_string(path) else {
                continue;
            };
            let mut current = None;
            for line in db.lines() {
                if let Some(id) = line.strip_prefix("usb:v").and_then(|r| r.strip_suffix('*')) {
                    current = (id.len() == 4)
                        .then(|| u16::from_str_radix(id, 16).ok())
                        .flatten();
                } else if let (Some(id), Some(name)) = (
                    current,
                    line.trim().strip_prefix("ID_VENDOR_FROM_DATABASE="),
                ) {
                    map.insert(id, name.to_string());
                    current = None;
                } else if !line.starts_with(' ') {
                    current = None;
                }
            }
            break;
        }
        map
    });
    vendors.get(&vendor).cloned().unwrap_or_default()
}

impl Sdl {
    /// Pumps SDL's events and opens and closes the devices that came and went.
    fn update(&mut self) {
        let api = &self.api;
        let added = &mut self.added;
        let removed = &mut self.removed;
        let before = (added.len(), removed.len());
        unsafe {
            (api.SDL_PumpEvents)();
            (api.SDL_FlushEvents)(0, u32::MAX);
            // Pads that went away.
            self.devices.retain(|id, device| {
                if (api.SDL_JoystickGetAttached)(device.joystick) != 0 {
                    return true;
                }
                if device.controller.is_null() {
                    (api.SDL_JoystickClose)(device.joystick);
                } else {
                    (api.SDL_GameControllerClose)(device.controller);
                }
                removed.push(*id);
                false
            });
            // Pads that arrived.
            for index in 0..(api.SDL_NumJoysticks)() {
                let id = (api.SDL_JoystickGetDeviceInstanceID)(index);
                if id < 0 || self.devices.contains_key(&id) {
                    continue;
                }
                let (controller, joystick, name) = if (api.SDL_IsGameController)(index) != 0 {
                    let controller = (api.SDL_GameControllerOpen)(index);
                    if controller.is_null() {
                        continue;
                    }
                    let joystick = (api.SDL_GameControllerGetJoystick)(controller);
                    (
                        controller,
                        joystick,
                        text((api.SDL_GameControllerName)(controller)),
                    )
                } else {
                    let joystick = (api.SDL_JoystickOpen)(index);
                    if joystick.is_null() {
                        continue;
                    }
                    (
                        std::ptr::null_mut(),
                        joystick,
                        text((api.SDL_JoystickName)(joystick)),
                    )
                };
                let vendor = (api.SDL_JoystickGetVendor)(joystick);
                let device = Device {
                    controller,
                    joystick,
                    name,
                    manufacturer: manufacturer(vendor),
                    vendor,
                    product: (api.SDL_JoystickGetProduct)(joystick),
                    version: (api.SDL_JoystickGetProductVersion)(joystick),
                };
                added.push(id);
                self.devices.insert(id, device);
            }
        }
        if (added.len(), removed.len()) != before {
            self.generation = self.generation.wrapping_add(1);
        }
    }

    fn sorted_ids(&self) -> Vec<i32> {
        let mut ids: Vec<_> = self.devices.keys().copied().collect();
        ids.sort();
        ids
    }

    fn poll(&mut self) -> Value {
        self.update();
        let api = &self.api;
        let added = std::mem::take(&mut self.added);
        let removed = std::mem::take(&mut self.removed);
        let ids = self.sorted_ids();
        let devices: Vec<Value> = ids
            .iter()
            .map(|id| {
                let d = &self.devices[id];
                unsafe {
                    let j = d.joystick;
                    let axes: Vec<i16> = (0..(api.SDL_JoystickNumAxes)(j))
                        .map(|i| (api.SDL_JoystickGetAxis)(j, i))
                        .collect();
                    let buttons: Vec<u8> = (0..(api.SDL_JoystickNumButtons)(j))
                        .map(|i| (api.SDL_JoystickGetButton)(j, i))
                        .collect();
                    let hats: Vec<u8> = (0..(api.SDL_JoystickNumHats)(j))
                        .map(|i| (api.SDL_JoystickGetHat)(j, i))
                        .collect();
                    let mut device = json!({
                        "id": id,
                        "name": d.name,
                        "manufacturer": d.manufacturer,
                        "vendor": d.vendor,
                        "product": d.product,
                        "version": d.version,
                        "axes": axes,
                        "buttons": buttons,
                        "hats": hats,
                    });
                    if !d.controller.is_null() {
                        let c = d.controller;
                        let pad_buttons: u32 = (0..CONTROLLER_BUTTONS)
                            .filter(|b| (api.SDL_GameControllerGetButton)(c, *b) != 0)
                            .fold(0, |bits, b| bits | 1 << b);
                        let pad_axes: Vec<i16> = (0..CONTROLLER_AXES)
                            .map(|a| (api.SDL_GameControllerGetAxis)(c, a))
                            .collect();
                        device["padButtons"] = json!(pad_buttons);
                        device["padAxes"] = json!(pad_axes);
                    }
                    device
                }
            })
            .collect();
        json!({ "added": added, "removed": removed, "devices": devices, "generation": self.generation })
    }

    /// The frame's input of every device (see [`state`]), written into `out` when it fits;
    /// returns the bytes it takes.
    fn state(&mut self, out: &mut [u8]) -> usize {
        self.update();
        let api = &self.api;
        let mut bytes = Vec::with_capacity(out.len());
        bytes.extend_from_slice(&self.generation.to_le_bytes());
        bytes.extend_from_slice(&(self.devices.len() as i32).to_le_bytes());
        for id in self.sorted_ids() {
            let d = &self.devices[&id];
            unsafe {
                let j = d.joystick;
                let (axes, buttons, hats) = (
                    (api.SDL_JoystickNumAxes)(j).max(0),
                    (api.SDL_JoystickNumButtons)(j).max(0),
                    (api.SDL_JoystickNumHats)(j).max(0),
                );
                let c = d.controller;
                let pad_buttons: u32 = if c.is_null() {
                    0
                } else {
                    (0..CONTROLLER_BUTTONS)
                        .filter(|b| (api.SDL_GameControllerGetButton)(c, *b) != 0)
                        .fold(0, |bits, b| bits | 1 << b)
                };
                for value in [id, axes, buttons, hats, (!c.is_null()) as i32] {
                    bytes.extend_from_slice(&value.to_le_bytes());
                }
                bytes.extend_from_slice(&pad_buttons.to_le_bytes());
                for a in 0..CONTROLLER_AXES {
                    let v = if c.is_null() {
                        0
                    } else {
                        (api.SDL_GameControllerGetAxis)(c, a)
                    };
                    bytes.extend_from_slice(&v.to_le_bytes());
                }
                for i in 0..axes {
                    bytes.extend_from_slice(&(api.SDL_JoystickGetAxis)(j, i).to_le_bytes());
                }
                bytes.extend((0..buttons).map(|i| (api.SDL_JoystickGetButton)(j, i)));
                bytes.extend((0..hats).map(|i| (api.SDL_JoystickGetHat)(j, i)));
                while bytes.len() % 4 != 0 {
                    bytes.push(0);
                }
            }
        }
        if bytes.len() <= out.len() {
            out[..bytes.len()].copy_from_slice(&bytes);
        }
        bytes.len()
    }
}

/// The frame's snapshot: the devices attached (in SDL instance order), their joystick axes,
/// buttons and hats, and for the pads SDL maps, the controller's buttons (SDL's
/// `SDL_GameControllerButton` bits) and axes.
pub fn poll(family: &str) -> Value {
    match &mut *ready(family) {
        State::Ready(sdl) => sdl.poll(),
        _ => json!({ "added": [], "removed": [], "devices": [], "generation": 0 }),
    }
}

/// The frame's input, the per-frame path that `poll` (names and ids, only when the attached set
/// changed) complements: `u32 generation` (of the attached set, as `poll` reports it), `i32
/// count`, then per device in instance-id order `i32 id, axes, buttons, hats, is_pad; u32
/// pad_buttons; i16 pad_axes[6]; i16 axes[axes]; u8 buttons[buttons]; u8 hats[hats]`, padded to
/// 4 bytes; little-endian. Written into `out` when it fits; returns the bytes it takes, 0 when
/// SDL is unavailable.
pub fn state(family: &str, out: &mut [u8]) -> usize {
    match &mut *ready(family) {
        State::Ready(sdl) => sdl.state(out),
        _ => 0,
    }
}

fn ready(family: &str) -> std::sync::MutexGuard<'static, State> {
    let mut state = STATE.lock().unwrap();
    if let State::Uninitialized = *state {
        *state = match init(family) {
            Some(sdl) => State::Ready(sdl),
            None => State::Unavailable,
        };
    }
    state
}
