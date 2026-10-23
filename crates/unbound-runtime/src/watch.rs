//! Watching the project's sources while the game runs: changed scripts are recompiled and patched
//! into the running game, changed content is reloaded.
use anyhow::Result;
use notify::{RecursiveMode, Watcher as _};
use std::{
    collections::BTreeSet,
    path::{Path, PathBuf},
    sync::mpsc::{Receiver, channel},
    time::{Duration, Instant},
};

/// What changed since the last poll, by kind.
#[derive(Debug, Default)]
pub struct Changes {
    pub scripts: BTreeSet<PathBuf>,
    /// Script files added or removed, or assembly definitions changed: the graph changes.
    pub graph: bool,
    pub content: BTreeSet<PathBuf>,
}

impl Changes {
    pub fn is_empty(&self) -> bool {
        self.scripts.is_empty() && !self.graph && self.content.is_empty()
    }
}

pub struct ProjectWatcher {
    roots: Vec<PathBuf>,
    _watcher: notify::RecommendedWatcher,
    events: Receiver<notify::Result<notify::Event>>,
    pending: Changes,
    last_event: Option<Instant>,
}

/// Saves arrive as several events (write, rename, chmod); wait this long after the last one.
const SETTLE: Duration = Duration::from_millis(60);

impl ProjectWatcher {
    pub fn new(roots: &[PathBuf]) -> Result<Self> {
        let (sender, events) = channel();
        let mut watcher = notify::recommended_watcher(move |event| {
            let _ = sender.send(event);
        })?;
        for root in roots {
            if root.exists() {
                watcher.watch(root, RecursiveMode::Recursive)?;
            }
        }
        Ok(Self {
            roots: roots.to_vec(),
            _watcher: watcher,
            events,
            pending: Changes::default(),
            last_event: None,
        })
    }

    /// The changes that settled since the last call (empty while files are still being written).
    pub fn poll(&mut self) -> Changes {
        while let Ok(event) = self.events.try_recv() {
            let Ok(event) = event else { continue };
            if matches!(event.kind, notify::EventKind::Access(_)) {
                continue;
            }
            for path in event.paths {
                // Only what lies below a watched root decides whether a path is hidden.
                let relative = self
                    .roots
                    .iter()
                    .find_map(|root| path.strip_prefix(root).ok())
                    .unwrap_or(&path)
                    .to_path_buf();
                if hidden(&relative) {
                    continue;
                }
                if classify(&path, &event.kind, &mut self.pending) {
                    self.last_event = Some(Instant::now());
                }
            }
        }
        match self.last_event {
            Some(at) if at.elapsed() >= SETTLE => {
                self.last_event = None;
                std::mem::take(&mut self.pending)
            }
            _ => Changes::default(),
        }
    }
}

fn hidden(path: &Path) -> bool {
    path.components().any(|c| {
        let name = c.as_os_str().to_string_lossy();
        name.starts_with('.') || name.ends_with('~') || name == "Library" || name == "Temp"
    })
}

fn classify(path: &Path, kind: &notify::EventKind, into: &mut Changes) -> bool {
    let ext = path
        .extension()
        .and_then(|e| e.to_str())
        .unwrap_or("")
        .to_ascii_lowercase();
    let created_or_removed = matches!(
        kind,
        notify::EventKind::Create(_) | notify::EventKind::Remove(_)
    );
    match ext.as_str() {
        "cs" => {
            into.scripts.insert(path.to_path_buf());
            if created_or_removed {
                into.graph = true;
            }
            true
        }
        "asmdef" | "asmref" | "rsp" => {
            into.graph = true;
            true
        }
        "meta" | "tmp" | "swp" => false,
        "" => false,
        _ => {
            into.content.insert(path.to_path_buf());
            true
        }
    }
}
