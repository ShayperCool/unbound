//! Scripted input for runs without a person (`unbound run --move/--press/--release/--scroll`):
//! changes of the pointer, keys, mouse buttons and wheel at given frames, which stay in effect
//! (a pressed key is held until released, the pointer stays where it moved).
use anyhow::{Context, Result, bail};

/// A change of input at a frame.
#[derive(Clone, Copy, Debug, PartialEq)]
pub enum ScriptedInput {
    /// The pointer moves to window pixels from the top left.
    Move(f32, f32),
    /// A key or mouse button, as a Unity `KeyCode` (`Mouse0` is the left button), goes down or up.
    Key { code: u16, down: bool },
    /// The wheel turns by notches (positive: right, up).
    Scroll(f32, f32),
}

/// Unity `KeyCode.Mouse0`.
pub const MOUSE0: u16 = 323;

impl ScriptedInput {
    /// Parses `FRAME:X,Y` (move), `FRAME:KEY` (press/release, a Unity `KeyCode` name) or
    /// `FRAME:DY` / `FRAME:DX,DY` (scroll), as `kind` (`move`, `press`, `release`, `scroll`).
    pub fn parse(kind: &str, entry: &str) -> Result<(u64, Self)> {
        let (frame, value) = entry
            .split_once(':')
            .with_context(|| format!("--{kind} {entry}: expected FRAME:..."))?;
        let frame: u64 = frame
            .trim()
            .parse()
            .with_context(|| format!("--{kind} {entry}: bad frame"))?;
        let pair = |value: &str| -> Result<(f32, f32)> {
            let (a, b) = value
                .split_once(',')
                .with_context(|| format!("--{kind} {entry}: expected two numbers, X,Y"))?;
            Ok((a.trim().parse()?, b.trim().parse()?))
        };
        let input = match kind {
            "move" => {
                let (x, y) = pair(value)?;
                ScriptedInput::Move(x, y)
            }
            "press" | "release" => {
                let code = key_code(value.trim())
                    .with_context(|| format!("--{kind} {entry}: unknown key (a Unity KeyCode name, e.g. W, Space, LeftArrow, Mouse0)"))?;
                ScriptedInput::Key {
                    code,
                    down: kind == "press",
                }
            }
            "scroll" => {
                let (x, y) = if value.contains(',') {
                    pair(value)?
                } else {
                    (0.0, value.trim().parse()?)
                };
                ScriptedInput::Scroll(x, y)
            }
            other => bail!("unknown scripted input `{other}`"),
        };
        Ok((frame, input))
    }
}

/// The Unity `KeyCode` of a `KeyCode` name (case-insensitive; single letters and digits too).
pub fn key_code(name: &str) -> Option<u16> {
    let lower = name.to_ascii_lowercase();
    if lower.len() == 1 {
        let c = lower.as_bytes()[0];
        if c.is_ascii_lowercase() || c.is_ascii_digit() {
            return Some(c as u16);
        }
    }
    if let Some(n) = lower.strip_prefix("alpha")
        && let Ok(n @ 0..=9) = n.parse::<u16>()
    {
        return Some(48 + n);
    }
    if let Some(n) = lower.strip_prefix("keypad")
        && let Ok(n @ 0..=9) = n.parse::<u16>()
    {
        return Some(256 + n);
    }
    if let Some(n) = lower.strip_prefix("mouse")
        && let Ok(n @ 0..=6) = n.parse::<u16>()
    {
        return Some(MOUSE0 + n);
    }
    if let Some(n) = lower.strip_prefix('f')
        && let Ok(n @ 1..=15) = n.parse::<u16>()
    {
        return Some(281 + n);
    }
    Some(match lower.as_str() {
        "backspace" => 8,
        "tab" => 9,
        "return" | "enter" => 13,
        "pause" => 19,
        "escape" => 27,
        "space" => 32,
        "quote" => 39,
        "comma" => 44,
        "minus" => 45,
        "period" => 46,
        "slash" => 47,
        "semicolon" => 59,
        "equals" => 61,
        "leftbracket" => 91,
        "backslash" => 92,
        "rightbracket" => 93,
        "backquote" => 96,
        "delete" => 127,
        "keypadperiod" => 266,
        "keypaddivide" => 267,
        "keypadmultiply" => 268,
        "keypadminus" => 269,
        "keypadplus" => 270,
        "keypadenter" => 271,
        "keypadequals" => 272,
        "uparrow" => 273,
        "downarrow" => 274,
        "rightarrow" => 275,
        "leftarrow" => 276,
        "insert" => 277,
        "home" => 278,
        "end" => 279,
        "pageup" => 280,
        "pagedown" => 281,
        "numlock" => 300,
        "capslock" => 301,
        "scrolllock" => 302,
        "rightshift" => 303,
        "leftshift" => 304,
        "rightcontrol" => 305,
        "leftcontrol" => 306,
        "rightalt" => 307,
        "leftalt" => 308,
        "rightmeta" | "rightcommand" | "rightwindows" => 309,
        "leftmeta" | "leftcommand" | "leftwindows" => 310,
        "print" => 316,
        "menu" => 319,
        _ => return None,
    })
}

/// The text a key types without modifiers, control characters included (as a window reports
/// them: Return types `\r`, Backspace `\u{8}`, Escape `\u{1b}`).
pub fn key_text(code: u16) -> Option<char> {
    match code {
        8 | 9 | 27 | 32 | 39 | 44..=57 | 59 | 61 | 91..=93 | 96..=122 | 127 => Some(code as u8 as char),
        13 | 271 => Some('\r'),
        256..=265 => Some((b'0' + (code - 256) as u8) as char),
        _ => None,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parses_scripted_input() {
        assert_eq!(
            ScriptedInput::parse("move", "10:480,80").unwrap(),
            (10, ScriptedInput::Move(480.0, 80.0))
        );
        assert_eq!(
            ScriptedInput::parse("press", "11:Mouse0").unwrap(),
            (11, ScriptedInput::Key { code: MOUSE0, down: true })
        );
        assert_eq!(
            ScriptedInput::parse("release", "60:W").unwrap(),
            (60, ScriptedInput::Key { code: 119, down: false })
        );
        assert_eq!(
            ScriptedInput::parse("press", "1:LeftArrow").unwrap().1,
            ScriptedInput::Key { code: 276, down: true }
        );
        assert_eq!(
            ScriptedInput::parse("scroll", "181:1").unwrap(),
            (181, ScriptedInput::Scroll(0.0, 1.0))
        );
        assert!(ScriptedInput::parse("press", "1:Nope").is_err());
        assert_eq!(key_code("Return"), Some(13));
        assert_eq!(key_code("F12"), Some(293));
        assert_eq!(key_text(119), Some('w'));
        assert_eq!(key_text(13), Some('\r'));
        assert_eq!(key_text(276), None);
    }
}
