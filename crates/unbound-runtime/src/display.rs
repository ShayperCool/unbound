//! The display the game's window is on, for `Screen.currentResolution`, `Screen.resolutions`,
//! `Screen.dpi` and `Screen.mainWindowDisplayInfo`. The windowed app publishes it from the
//! monitors the window system reports; the managed engine reads it through the `display-info`
//! control call. Nothing is published before the window opens (the first scene starts before
//! it) nor in a run without a window (headless), and a remote session publishes that its
//! viewers' displays are unknown: the managed side then keeps its constants and says so.
use anyhow::Result;
use serde_json::{Value as Json, json};
use std::sync::Mutex;

/// A mode the monitor supports, as the window system reports it.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct DisplayMode {
    pub width: u32,
    pub height: u32,
    pub refresh_rate_millihertz: u32,
}

/// A monitor, as the window system reports it.
#[derive(Clone, Debug, PartialEq)]
pub struct DisplaySnapshot {
    /// The monitor's identity for the session (`DisplayInfo`'s handle).
    pub id: u64,
    pub dpi: f32,
    pub info_refresh_numerator: u32,
    pub info_refresh_denominator: u32,
    pub work_area: [i32; 4],
    pub name: Option<String>,
    /// The desktop's current resolution on this monitor, in physical pixels.
    pub width: u32,
    pub height: u32,
    /// None: the window system does not know it.
    pub refresh_rate_millihertz: Option<u32>,
    /// The window system's scale factor for the monitor (1 on an unscaled desktop).
    pub scale_factor: f64,
    /// In the window system's order.
    pub modes: Vec<DisplayMode>,
}

/// What the app knows of the window's display.
#[derive(Clone, Debug, PartialEq)]
pub enum Display {
    Known(DisplaySnapshot),
    /// No display to report, and why.
    Unavailable(String),
}

/// Why there is no display when the app has not published one.
pub const UNPUBLISHED: &str =
    "no window has reported its display: the run is headless, or the window is not open yet";

/// The display last published, with a version that counts its changes (0: never published).
static STATE: Mutex<(u64, Option<Display>)> = Mutex::new((0, None));

/// Publishes the window's display; a change bumps the version the managed side compares.
pub fn publish(display: Display) {
    let mut guard = STATE.lock().unwrap();
    if guard.1.as_ref() != Some(&display) {
        guard.0 += 1;
        guard.1 = Some(display);
    }
}

/// Version of the cached monitor description, passed with frame input. Reading it performs
/// no display query and allocates nothing.
pub fn version() -> u64 {
    STATE.lock().unwrap().0
}

/// The `display-info` control call. The request is `{}` or `{"since":N}` with the version
/// the caller already has; the answer is `{"version":N,"unchanged":true}` when it is still
/// current, else the whole display (see [`encode`]).
pub fn query(request: &str) -> Result<String> {
    let since = if request.trim().is_empty() {
        None
    } else {
        let json: Json = serde_json::from_str(request)?;
        json.get("since").and_then(Json::as_u64)
    };
    let guard = STATE.lock().unwrap();
    Ok(encode(guard.0, guard.1.as_ref(), since).to_string())
}

/// The answer for `version` and `display` to a caller holding `since`:
/// - `{"version":N,"unchanged":true}`;
/// - `{"version":N,"available":false,"reason":"..."}`;
/// - `{"version":N,"available":true,"id":..,"name":..|null,"width":..,"height":..,
///   "refreshRate":{"numerator":..,"denominator":..}|null,"scaleFactor":..,
///   "modes":[{"width":..,"height":..,"refreshRate":{..}|null}, ...]}`.
pub fn encode(version: u64, display: Option<&Display>, since: Option<u64>) -> Json {
    if since == Some(version) {
        return json!({ "version": version, "unchanged": true });
    }
    match display {
        None => json!({ "version": version, "available": false, "reason": UNPUBLISHED }),
        Some(Display::Unavailable(reason)) => {
            json!({ "version": version, "available": false, "reason": reason })
        }
        Some(Display::Known(d)) => json!({
            "version": version,
            "available": true,
            "id": d.id,
            "dpi": d.dpi,
            "infoRefreshRate": { "numerator": d.info_refresh_numerator, "denominator": d.info_refresh_denominator },
            "workArea": d.work_area,
            "name": d.name,
            "width": d.width,
            "height": d.height,
            "refreshRate": d.refresh_rate_millihertz.and_then(refresh_rate),
            "scaleFactor": d.scale_factor,
            "modes": d.modes.iter().map(|m| json!({
                "width": m.width,
                "height": m.height,
                "refreshRate": refresh_rate(m.refresh_rate_millihertz),
            })).collect::<Vec<_>>(),
        }),
    }
}

/// A refresh rate in millihertz as Unity's `RefreshRate` ratio, in lowest terms; None for 0
/// (unknown). The window system reports millihertz, so the ratio is exact to a millihertz.
pub fn refresh_rate(millihertz: u32) -> Option<Json> {
    if millihertz == 0 {
        return None;
    }
    let divisor = gcd(millihertz, 1000);
    Some(json!({ "numerator": millihertz / divisor, "denominator": 1000 / divisor }))
}

fn gcd(mut a: u32, mut b: u32) -> u32 {
    while b != 0 {
        (a, b) = (b, a % b);
    }
    a
}

#[cfg(test)]
mod tests {
    use super::*;

    fn monitor() -> DisplaySnapshot {
        DisplaySnapshot {
            id: 7,
            dpi: 0.0,
            info_refresh_numerator: 59_951,
            info_refresh_denominator: 1000,
            work_area: [0, 32, 2560, 1408],
            name: Some("DP-1".into()),
            width: 2560,
            height: 1440,
            refresh_rate_millihertz: Some(59_951),
            scale_factor: 1.5,
            modes: vec![
                DisplayMode {
                    width: 2560,
                    height: 1440,
                    refresh_rate_millihertz: 59_951,
                },
                DisplayMode {
                    width: 1920,
                    height: 1080,
                    refresh_rate_millihertz: 60_000,
                },
            ],
        }
    }

    #[test]
    fn known_display_encodes_every_field() {
        let display = Display::Known(monitor());
        assert_eq!(
            encode(3, Some(&display), None),
            json!({
                "version": 3,
                "available": true,
                "id": 7,
                "dpi": 0.0,
                "infoRefreshRate": { "numerator": 59_951, "denominator": 1000 },
                "workArea": [0, 32, 2560, 1408],
                "name": "DP-1",
                "width": 2560,
                "height": 1440,
                "refreshRate": { "numerator": 59_951, "denominator": 1000 },
                "scaleFactor": 1.5,
                "modes": [
                    { "width": 2560, "height": 1440,
                      "refreshRate": { "numerator": 59_951, "denominator": 1000 } },
                    { "width": 1920, "height": 1080,
                      "refreshRate": { "numerator": 60, "denominator": 1 } },
                ],
            })
        );
    }

    #[test]
    fn unknown_name_and_refresh_rate_are_null() {
        let mut d = monitor();
        d.name = None;
        d.refresh_rate_millihertz = None;
        d.modes[0].refresh_rate_millihertz = 0;
        let json = encode(1, Some(&Display::Known(d)), None);
        assert_eq!(json["name"], Json::Null);
        assert_eq!(json["refreshRate"], Json::Null);
        assert_eq!(json["modes"][0]["refreshRate"], Json::Null);
    }

    #[test]
    fn missing_display_says_why() {
        assert_eq!(
            encode(0, None, None),
            json!({ "version": 0, "available": false, "reason": UNPUBLISHED })
        );
        let remote = Display::Unavailable("remote".into());
        assert_eq!(
            encode(2, Some(&remote), Some(1)),
            json!({ "version": 2, "available": false, "reason": "remote" })
        );
    }

    #[test]
    fn current_version_is_unchanged() {
        let display = Display::Known(monitor());
        assert_eq!(
            encode(4, Some(&display), Some(4)),
            json!({ "version": 4, "unchanged": true })
        );
    }

    #[test]
    fn refresh_rates_are_reduced_ratios() {
        assert_eq!(
            refresh_rate(60_000),
            Some(json!({ "numerator": 60, "denominator": 1 }))
        );
        assert_eq!(
            refresh_rate(59_940),
            Some(json!({ "numerator": 2997, "denominator": 50 }))
        );
        assert_eq!(
            refresh_rate(143_500),
            Some(json!({ "numerator": 287, "denominator": 2 }))
        );
        assert_eq!(refresh_rate(0), None);
    }

    #[test]
    fn publishing_bumps_the_version_only_on_change() {
        // The only test that touches the shared state.
        let first: Json = serde_json::from_str(&query("").unwrap()).unwrap();
        let start = first["version"].as_u64().unwrap();
        publish(Display::Known(monitor()));
        publish(Display::Known(monitor()));
        let known: Json = serde_json::from_str(&query("{}").unwrap()).unwrap();
        assert_eq!(known["version"].as_u64(), Some(start + 1));
        assert_eq!(known["width"], 2560);
        let same: Json =
            serde_json::from_str(&query(&format!("{{\"since\":{}}}", start + 1)).unwrap()).unwrap();
        assert_eq!(same["unchanged"], true);
        publish(Display::Unavailable("gone".into()));
        let gone: Json = serde_json::from_str(&query("{}").unwrap()).unwrap();
        assert_eq!(gone["version"].as_u64(), Some(start + 2));
        assert_eq!(gone["available"], false);
    }
}
