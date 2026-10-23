//! Viewer input: the compact events a browser sends, checked and turned into [`RemoteEvent`]s.
//! Anything unknown or out of range is dropped.
use serde_json::Value;

/// Events accepted from one viewer message.
pub const MAXIMUM_EVENTS: usize = 256;

/// One input event of a viewer. Positions are fractions of the picture, from its top-left corner.
#[derive(Debug, Clone, PartialEq)]
pub enum RemoteEvent {
    /// The pointer moved.
    Move { x: f32, y: f32 },
    /// A button was pressed or released (DOM numbering: 0 left, 1 middle, 2 right, 3 back,
    /// 4 forward), where it happened when known.
    Button {
        button: u8,
        down: bool,
        at: Option<(f32, f32)>,
    },
    /// The wheel turned, in notches (y grows upwards, as Unity's scroll delta).
    Scroll { x: f32, y: f32 },
    /// A key, by its physical position (DOM `KeyboardEvent.code`, such as `KeyA` or `ArrowUp`).
    Key { code: String, down: bool },
    /// The viewer let go of everything it held (it lost focus or left).
    Release,
}

fn number(event: &Value, key: &str) -> Option<f64> {
    event.get(key)?.as_f64().filter(|value| value.is_finite())
}

fn point(event: &Value) -> Option<(f32, f32)> {
    let (x, y) = (number(event, "x")?, number(event, "y")?);
    Some((x.clamp(0.0, 1.0) as f32, y.clamp(0.0, 1.0) as f32))
}

fn translate_one(event: &Value) -> Option<RemoteEvent> {
    match event.get("t")?.as_str()? {
        "k" => {
            let code = event.get("c")?.as_str()?;
            if code.is_empty()
                || code.len() > 32
                || !code.chars().all(|c| c.is_ascii_alphanumeric())
            {
                return None;
            }
            Some(RemoteEvent::Key {
                code: code.to_string(),
                down: event.get("down")?.as_bool()?,
            })
        }
        "m" => {
            let (x, y) = point(event)?;
            Some(RemoteEvent::Move { x, y })
        }
        "b" => {
            let button = event.get("b")?.as_u64()?;
            if button > 4 {
                return None;
            }
            Some(RemoteEvent::Button {
                button: button as u8,
                down: event.get("down")?.as_bool()?,
                at: point(event),
            })
        }
        "w" => {
            // DOM wheel pixels grow downwards; one notch is about 100 of them.
            let notches =
                |key| (number(event, key).unwrap_or(0.0).clamp(-2000.0, 2000.0) / 100.0) as f32;
            Some(RemoteEvent::Scroll {
                x: notches("dx"),
                y: -notches("dy"),
            })
        }
        _ => None,
    }
}

/// The events of a viewer's message, in order.
pub fn translate(events: &[Value]) -> Vec<RemoteEvent> {
    events
        .iter()
        .take(MAXIMUM_EVENTS)
        .filter_map(translate_one)
        .collect()
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    #[test]
    fn events_are_checked() {
        let events = translate(&[
            json!({"t": "m", "x": 0.5, "y": 2.0}),
            json!({"t": "b", "b": 0, "down": true, "x": 0.25, "y": 0.75}),
            json!({"t": "k", "c": "KeyA", "down": true}),
            json!({"t": "k", "c": "bad code!", "down": true}),
            json!({"t": "w", "dx": 0, "dy": 200}),
            json!({"t": "x"}),
        ]);
        assert_eq!(
            events,
            vec![
                RemoteEvent::Move { x: 0.5, y: 1.0 },
                RemoteEvent::Button {
                    button: 0,
                    down: true,
                    at: Some((0.25, 0.75))
                },
                RemoteEvent::Key {
                    code: "KeyA".into(),
                    down: true
                },
                RemoteEvent::Scroll { x: 0.0, y: -2.0 },
            ]
        );
    }
}
