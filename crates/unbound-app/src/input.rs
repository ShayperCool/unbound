//! Window input → Unity's `FrameInput`: keys as Unity `KeyCode` values, mouse in pixels.
use bevy::input::keyboard::KeyCode as K;

/// The Unity `KeyCode` of a physical key (0: none).
pub fn unity_key(key: K) -> u16 {
    match key {
        K::Backspace => 8,
        K::Tab => 9,
        K::Enter => 13,
        K::Pause => 19,
        K::Escape => 27,
        K::Space => 32,
        K::Quote => 39,
        K::Comma => 44,
        K::Minus => 45,
        K::Period => 46,
        K::Slash => 47,
        K::Digit0 => 48,
        K::Digit1 => 49,
        K::Digit2 => 50,
        K::Digit3 => 51,
        K::Digit4 => 52,
        K::Digit5 => 53,
        K::Digit6 => 54,
        K::Digit7 => 55,
        K::Digit8 => 56,
        K::Digit9 => 57,
        K::Semicolon => 59,
        K::Equal => 61,
        K::BracketLeft => 91,
        K::Backslash => 92,
        K::BracketRight => 93,
        K::Backquote => 96,
        K::KeyA => 97,
        K::KeyB => 98,
        K::KeyC => 99,
        K::KeyD => 100,
        K::KeyE => 101,
        K::KeyF => 102,
        K::KeyG => 103,
        K::KeyH => 104,
        K::KeyI => 105,
        K::KeyJ => 106,
        K::KeyK => 107,
        K::KeyL => 108,
        K::KeyM => 109,
        K::KeyN => 110,
        K::KeyO => 111,
        K::KeyP => 112,
        K::KeyQ => 113,
        K::KeyR => 114,
        K::KeyS => 115,
        K::KeyT => 116,
        K::KeyU => 117,
        K::KeyV => 118,
        K::KeyW => 119,
        K::KeyX => 120,
        K::KeyY => 121,
        K::KeyZ => 122,
        K::Delete => 127,
        K::Numpad0 => 256,
        K::Numpad1 => 257,
        K::Numpad2 => 258,
        K::Numpad3 => 259,
        K::Numpad4 => 260,
        K::Numpad5 => 261,
        K::Numpad6 => 262,
        K::Numpad7 => 263,
        K::Numpad8 => 264,
        K::Numpad9 => 265,
        K::NumpadDecimal => 266,
        K::NumpadDivide => 267,
        K::NumpadMultiply => 268,
        K::NumpadSubtract => 269,
        K::NumpadAdd => 270,
        K::NumpadEnter => 271,
        K::NumpadEqual => 272,
        K::ArrowUp => 273,
        K::ArrowDown => 274,
        K::ArrowRight => 275,
        K::ArrowLeft => 276,
        K::Insert => 277,
        K::Home => 278,
        K::End => 279,
        K::PageUp => 280,
        K::PageDown => 281,
        K::F1 => 282,
        K::F2 => 283,
        K::F3 => 284,
        K::F4 => 285,
        K::F5 => 286,
        K::F6 => 287,
        K::F7 => 288,
        K::F8 => 289,
        K::F9 => 290,
        K::F10 => 291,
        K::F11 => 292,
        K::F12 => 293,
        K::F13 => 294,
        K::F14 => 295,
        K::F15 => 296,
        K::NumLock => 300,
        K::CapsLock => 301,
        K::ScrollLock => 302,
        K::ShiftRight => 303,
        K::ShiftLeft => 304,
        K::ControlRight => 305,
        K::ControlLeft => 306,
        K::AltRight => 307,
        K::AltLeft => 308,
        K::SuperRight => 309,
        K::SuperLeft => 310,
        K::Help => 315,
        K::PrintScreen => 316,
        K::ContextMenu => 319,
        _ => 0,
    }
}

/// Unity `KeyCode.Mouse0` + button.
pub const MOUSE0: u16 = 323;

/// The 512-bit key set `FrameInput.keys` points to.
#[derive(Clone)]
pub struct KeySet(pub [u8; 64]);

impl Default for KeySet {
    fn default() -> Self {
        KeySet([0; 64])
    }
}

impl KeySet {
    pub fn set(&mut self, code: u16) {
        if code != 0 && (code as usize) < 512 {
            self.0[code as usize / 8] |= 1 << (code % 8);
        }
    }

    pub fn contains(&self, code: u16) -> bool {
        (code as usize) < 512 && self.0[code as usize / 8] & (1 << (code % 8)) != 0
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn keys_map_to_unity_codes() {
        assert_eq!(unity_key(K::KeyA), 97);
        assert_eq!(unity_key(K::Digit0), 48);
        assert_eq!(unity_key(K::ArrowLeft), 276);
        assert_eq!(unity_key(K::ShiftLeft), 304);
        let mut set = KeySet::default();
        set.set(unity_key(K::Space));
        set.set(MOUSE0);
        assert!(set.contains(32) && set.contains(323) && !set.contains(33));
    }
}
