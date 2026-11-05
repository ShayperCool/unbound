//! The cursor the game asks for (`Cursor.lockState`, `Cursor.visible`, `Cursor.SetCursor`), for
//! the window to apply: the managed engine sets it through the `cursor` control call.
use anyhow::{Result, bail};
use std::sync::Mutex;

/// A cursor image: RGBA8 rows from the top, the hotspot in pixels from the top left.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct CursorImage {
    pub width: u32,
    pub height: u32,
    pub rgba: Vec<u8>,
    pub hotspot: (u16, u16),
}

/// What the game asked for.
#[derive(Clone, Debug, PartialEq)]
pub struct CursorState {
    /// Unity's `CursorLockMode`: 0 none, 1 locked, 2 confined.
    pub lock: i32,
    pub visible: bool,
    /// None: the system's arrow.
    pub image: Option<CursorImage>,
    /// A `Mouse.WarpCursorPosition` not yet applied: Unity's screen coordinates (window pixels
    /// from the bottom left). One-shot: [`changed`] hands it out once.
    pub warp: Option<[f64; 2]>,
}

impl Default for CursorState {
    fn default() -> Self {
        Self {
            lock: 0,
            visible: true,
            image: None,
            warp: None,
        }
    }
}

static STATE: Mutex<(u64, Option<CursorState>)> = Mutex::new((0, None));

/// The `cursor` control call: `{"lock":0,"visible":true}` and, when the image changes,
/// `"image":{"width":W,"height":H,"rgba":[...],"hotspot":[X,Y]}` (or `"image":null` for the
/// system's arrow), or `{"warp":[X,Y]}` alone to move the system's cursor once.
pub fn set(request: &str) -> Result<String> {
    let json: serde_json::Value = serde_json::from_str(request)?;
    let mut guard = STATE.lock().unwrap();
    let mut state = guard.1.clone().unwrap_or_default();
    if let Some(lock) = json.get("lock").and_then(|v| v.as_i64()) {
        state.lock = lock as i32;
    }
    if let Some(visible) = json.get("visible").and_then(|v| v.as_bool()) {
        state.visible = visible;
    }
    if let Some(image) = json.get("image") {
        state.image = if image.is_null() {
            None
        } else {
            let width = image["width"].as_u64().unwrap_or(0) as u32;
            let height = image["height"].as_u64().unwrap_or(0) as u32;
            let rgba: Vec<u8> = image["rgba"]
                .as_array()
                .map(|a| a.iter().map(|v| v.as_u64().unwrap_or(0) as u8).collect())
                .unwrap_or_default();
            if width == 0 || height == 0 || rgba.len() != (width * height * 4) as usize {
                bail!("cursor image: {width}x{height} with {} bytes", rgba.len());
            }
            let x = image["hotspot"][0]
                .as_u64()
                .unwrap_or(0)
                .min(width as u64 - 1) as u16;
            let y = image["hotspot"][1]
                .as_u64()
                .unwrap_or(0)
                .min(height as u64 - 1) as u16;
            Some(CursorImage {
                width,
                height,
                rgba,
                hotspot: (x, y),
            })
        };
    }
    if let Some(warp) = json.get("warp") {
        let position = warp.as_array().and_then(|a| match a.as_slice() {
            [x, y] => Some([x.as_f64()?, y.as_f64()?]),
            _ => None,
        });
        match position {
            Some([x, y]) if x.is_finite() && y.is_finite() => state.warp = Some([x, y]),
            _ => bail!("cursor warp: {warp} is not a finite [x, y] position"),
        }
    }
    guard.0 += 1;
    guard.1 = Some(state);
    Ok("{}".into())
}

/// The cursor the game asked for, when it changed since `seen` (which it updates).
pub fn changed(seen: &mut u64) -> Option<CursorState> {
    let mut guard = STATE.lock().unwrap();
    if guard.0 == *seen {
        return None;
    }
    *seen = guard.0;
    let state = guard.1.clone();
    // A warp is applied once; the lock, visibility and image stay.
    if let Some(stored) = &mut guard.1 {
        stored.warp = None;
    }
    state
}
