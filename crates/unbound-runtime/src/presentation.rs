//! Whether the window's frames wait for the display's vertical sync: the managed engine sets it
//! through the `vsync` control call from `QualitySettings.vSyncCount` (0: frames are presented
//! unsynchronized, as Unity documents for vSyncCount 0; above 0: synchronized), for the window to
//! apply. Until the game says, the window keeps vertical sync.
use anyhow::{Result, bail};
use std::sync::atomic::{AtomicU8, Ordering};

const UNSET: u8 = 0;
const SYNCED: u8 = 1;
const UNSYNCED: u8 = 2;

static VSYNC: AtomicU8 = AtomicU8::new(UNSET);

/// The `vsync` control call: `"1"` to wait for the vertical sync, `"0"` not to.
pub fn set(request: &str) -> Result<String> {
    let value = match request.trim() {
        "1" => SYNCED,
        "0" => UNSYNCED,
        other => bail!("vsync: expected 0 or 1, got `{other}`"),
    };
    VSYNC.store(value, Ordering::Relaxed);
    Ok("{}".into())
}

/// Whether frames should wait for the vertical sync; None until the game said.
pub fn vsync() -> Option<bool> {
    match VSYNC.load(Ordering::Relaxed) {
        SYNCED => Some(true),
        UNSYNCED => Some(false),
        _ => None,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn the_request_sets_the_sync_and_rejects_anything_else() {
        assert!(set("2").is_err());
        set("0").unwrap();
        assert_eq!(vsync(), Some(false));
        set("1").unwrap();
        assert_eq!(vsync(), Some(true));
    }
}
