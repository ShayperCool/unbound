//! IME text input for the legacy Input Manager (`Input.compositionString`, `Input.imeIsSelected`,
//! `Input.imeCompositionMode`, `Input.compositionCursorPos`). The window feeds the input method's
//! events into a snapshot that the managed engine polls once per frame through the `ime-state`
//! control call; text the input method commits joins the frame's text (`Input.inputString`). The
//! managed engine asks for the input method on or off, and where its windows go, through the
//! `ime-request` control call, which the window applies after the frame.
//!
//! Without a window that applies the requests (headless runs, a remote session's viewers) the
//! snapshot says it is not available, and the managed side keeps its fallback answers.
use anyhow::{Result, bail};
use serde::Deserialize;
use serde_json::{Value, json};
use std::sync::{
    Mutex,
    atomic::{AtomicU64, Ordering},
};

/// An input method event, as the window system reports it.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum ImeEvent<'a> {
    /// The text being composed (empty: none), with the composition's cursor as byte offsets into
    /// it (None: hidden).
    Preedit {
        value: &'a str,
        cursor: Option<(usize, usize)>,
    },
    /// Text the input method hands to the game.
    Commit(&'a str),
    /// The input method was enabled for the window.
    Enabled,
    /// The input method was disabled for the window.
    Disabled,
}

/// What the managed engine reads each frame.
#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub struct ImeSnapshot {
    /// Whether a window reads the input method and applies the game's requests.
    pub available: bool,
    /// The text being composed (`Input.compositionString`).
    pub composition: String,
    /// The composition's cursor, byte offsets into `composition` (None: hidden or no composition).
    pub cursor: Option<(usize, usize)>,
    /// Whether the window system reports the input method enabled (`Input.imeIsSelected`).
    pub selected: bool,
}

impl ImeSnapshot {
    const fn new() -> Self {
        Self {
            available: false,
            composition: String::new(),
            cursor: None,
            selected: false,
        }
    }

    /// Applies a window event; answers the byte range of `text` it added (empty unless it
    /// committed text).
    pub fn apply(&mut self, event: ImeEvent<'_>, text: &mut String) -> std::ops::Range<usize> {
        let start = text.len();
        match event {
            ImeEvent::Preedit { value, cursor } => {
                self.composition.clear();
                self.composition.push_str(value);
                self.cursor = if value.is_empty() { None } else { cursor };
            }
            ImeEvent::Commit(value) => {
                // The committed text replaces the composition it came from.
                self.clear_composition();
                text.push_str(value);
            }
            ImeEvent::Enabled => self.selected = true,
            ImeEvent::Disabled => {
                self.selected = false;
                self.clear_composition();
            }
        }
        start..text.len()
    }

    /// Whether a window reads the input method; losing it drops what it had reported.
    pub fn set_available(&mut self, available: bool) {
        if self.available == available {
            return;
        }
        self.available = available;
        if !available {
            self.selected = false;
            self.clear_composition();
        }
    }

    fn clear_composition(&mut self) {
        self.composition.clear();
        self.cursor = None;
    }

    /// The `ime-state` answer:
    /// `{"available":true,"composition":"...","cursor":[start,end]|null,"selected":true}`.
    pub fn to_json(&self) -> Value {
        json!({
            "available": self.available,
            "composition": self.composition,
            "cursor": self.cursor.map(|(start, end)| [start, end]),
            "selected": self.selected,
        })
    }
}

/// What the game asked for: the input method on or off, and where its windows open, in window
/// pixels from the top left.
#[derive(Clone, Copy, Debug, PartialEq, Deserialize)]
#[serde(deny_unknown_fields)]
pub struct ImeRequest {
    pub enabled: bool,
    pub position: [f32; 2],
}

impl ImeRequest {
    /// Parses an `ime-request` call: `{"enabled":true,"position":[x,y]}`.
    pub fn parse(request: &str) -> Result<Self> {
        let r: ImeRequest = serde_json::from_str(request)?;
        if !r.position.iter().all(|v| v.is_finite()) {
            bail!("ime-request: the position {:?} is not finite", r.position);
        }
        Ok(r)
    }
}

static STATE: Mutex<ImeSnapshot> = Mutex::new(ImeSnapshot::new());
static REQUEST: Mutex<Option<ImeRequest>> = Mutex::new(None);
static VERSION: AtomicU64 = AtomicU64::new(0);

/// Cached IME changes travel with frame input; getters do not poll JSON every frame.
pub fn version() -> u64 {
    VERSION.load(Ordering::Acquire)
}

/// The window says whether it reads the input method (false for a remote session).
pub fn attach(available: bool) {
    let mut state = STATE.lock().unwrap();
    if state.available != available {
        state.set_available(available);
        VERSION.fetch_add(1, Ordering::Release);
    }
}

/// An input method event from the window; committed text joins the frame's `text`. Answers the
/// byte range of `text` the event added (empty unless it committed text), so the window can
/// deliver it as a character event like typed text.
pub fn event(event: ImeEvent<'_>, text: &mut String) -> std::ops::Range<usize> {
    let mut state = STATE.lock().unwrap();
    let range = state.apply(event, text);
    VERSION.fetch_add(1, Ordering::Release);
    range
}

/// The `ime-state` control call.
pub fn state(_request: &str) -> Result<String> {
    Ok(STATE.lock().unwrap().to_json().to_string())
}

/// The `ime-request` control call: the latest request replaces one not yet applied.
pub fn request(request: &str) -> Result<String> {
    let r = ImeRequest::parse(request)?;
    *REQUEST.lock().unwrap() = Some(r);
    Ok("{}".into())
}

/// The game's request not yet applied, for the window.
pub fn take_request() -> Option<ImeRequest> {
    REQUEST.lock().unwrap().take()
}

#[cfg(test)]
mod tests {
    use super::*;

    fn preedit(value: &str, cursor: Option<(usize, usize)>) -> ImeEvent<'_> {
        ImeEvent::Preedit { value, cursor }
    }

    #[test]
    fn json_encodes_every_field() {
        let snapshot = ImeSnapshot {
            available: true,
            composition: "にほ\"ん".into(),
            cursor: Some((3, 6)),
            selected: true,
        };
        let text = snapshot.to_json().to_string();
        let back: Value = serde_json::from_str(&text).unwrap();
        assert_eq!(back["available"], json!(true));
        assert_eq!(back["composition"], json!("にほ\"ん"));
        assert_eq!(back["cursor"], json!([3, 6]));
        assert_eq!(back["selected"], json!(true));
        let empty = ImeSnapshot::default().to_json();
        assert_eq!(
            empty,
            json!({"available": false, "composition": "", "cursor": null, "selected": false})
        );
    }

    #[test]
    fn preedit_sets_and_clears_the_composition() {
        let mut s = ImeSnapshot::default();
        let mut text = String::new();
        s.apply(preedit("か", Some((3, 3))), &mut text);
        assert_eq!(s.composition, "か");
        assert_eq!(s.cursor, Some((3, 3)));
        s.apply(preedit("かな", None), &mut text);
        assert_eq!(s.composition, "かな");
        assert_eq!(s.cursor, None);
        s.apply(preedit("", Some((0, 0))), &mut text);
        assert_eq!(s.composition, "");
        assert_eq!(s.cursor, None);
        assert_eq!(text, "", "a composition is not input");
    }

    #[test]
    fn commit_appends_to_the_frame_text_and_ends_the_composition() {
        let mut s = ImeSnapshot::default();
        let mut text = String::from("a");
        s.apply(preedit("かんじ", Some((9, 9))), &mut text);
        s.apply(ImeEvent::Commit("漢字"), &mut text);
        assert_eq!(text, "a漢字");
        assert_eq!(s.composition, "");
        assert_eq!(s.cursor, None);
        s.apply(ImeEvent::Commit("b"), &mut text);
        assert_eq!(text, "a漢字b", "commits keep their order");
    }

    #[test]
    fn enabled_and_disabled_drive_selected() {
        let mut s = ImeSnapshot::default();
        let mut text = String::new();
        assert!(!s.selected);
        s.apply(ImeEvent::Enabled, &mut text);
        assert!(s.selected);
        s.apply(preedit("ㅎ", Some((0, 3))), &mut text);
        s.apply(ImeEvent::Disabled, &mut text);
        assert!(!s.selected);
        assert_eq!(s.composition, "", "disabling drops the composition");
        assert_eq!(s.cursor, None);
        assert_eq!(text, "");
    }

    #[test]
    fn losing_the_window_drops_the_state() {
        let mut s = ImeSnapshot::default();
        let mut text = String::new();
        s.set_available(true);
        s.apply(ImeEvent::Enabled, &mut text);
        s.apply(preedit("zh", None), &mut text);
        s.set_available(true);
        assert!(s.selected, "re-attaching keeps the state");
        s.set_available(false);
        assert_eq!(s, ImeSnapshot::default());
    }

    #[test]
    fn requests_parse_strictly() {
        assert_eq!(
            ImeRequest::parse(r#"{"enabled":true,"position":[12.5,40]}"#).unwrap(),
            ImeRequest {
                enabled: true,
                position: [12.5, 40.0]
            }
        );
        assert!(ImeRequest::parse(r#"{"enabled":true}"#).is_err());
        assert!(ImeRequest::parse(r#"{"enabled":1,"position":[0,0]}"#).is_err());
        assert!(ImeRequest::parse(r#"{"enabled":true,"position":[0,0],"x":1}"#).is_err());
        assert!(ImeRequest::parse(r#"{"enabled":true,"position":[1e39,0]}"#).is_err());
    }

    // The only test that touches the shared request slot.
    #[test]
    fn the_latest_request_is_taken_once() {
        request(r#"{"enabled":true,"position":[1,2]}"#).unwrap();
        request(r#"{"enabled":false,"position":[3,4]}"#).unwrap();
        assert!(request("{}").is_err());
        assert_eq!(
            take_request(),
            Some(ImeRequest {
                enabled: false,
                position: [3.0, 4.0]
            })
        );
        assert_eq!(take_request(), None);
    }

    // The only test that touches the shared snapshot.
    #[test]
    fn the_state_call_reports_the_window_events() {
        attach(true);
        let mut text = String::new();
        event(ImeEvent::Enabled, &mut text);
        event(preedit("pin", Some((3, 3))), &mut text);
        let v: Value = serde_json::from_str(&state("{}").unwrap()).unwrap();
        assert_eq!(
            v,
            json!({"available": true, "composition": "pin", "cursor": [3, 3], "selected": true})
        );
        event(ImeEvent::Commit("拼"), &mut text);
        assert_eq!(text, "拼");
        attach(false);
        let v: Value = serde_json::from_str(&state("{}").unwrap()).unwrap();
        assert_eq!(
            v,
            json!({"available": false, "composition": "", "cursor": null, "selected": false})
        );
    }

    #[test]
    fn apply_answers_the_committed_range_only() {
        let mut s = ImeSnapshot::new();
        let mut text = String::from("a");
        assert!(s.apply(ImeEvent::Enabled, &mut text).is_empty());
        assert!(s.apply(preedit("に", Some((0, 3))), &mut text).is_empty());
        let range = s.apply(ImeEvent::Commit("日本"), &mut text);
        assert_eq!(range, 1..7);
        assert_eq!(&text[range], "日本");
        assert!(s.apply(ImeEvent::Disabled, &mut text).is_empty());
    }
}
