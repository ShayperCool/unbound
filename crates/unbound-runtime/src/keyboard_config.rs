//! Host keyboard layout and key display names, queried by the managed Input System backend.
use std::sync::Mutex;

static KEYMAP: Mutex<Option<String>> = Mutex::new(None);

pub fn publish(layout: Option<String>, names: std::collections::BTreeMap<u8, String>) {
    *KEYMAP.lock().unwrap() =
        Some(serde_json::json!({ "layout": layout, "names": names }).to_string());
}

pub(crate) fn snapshot() -> String {
    KEYMAP
        .lock()
        .unwrap()
        .clone()
        .unwrap_or_else(|| "null".into())
}
