//! Scenes, prefabs and YAML assets as the engine loads them: parsed, with prefab instances
//! expanded, encoded for the managed side.
mod encode;
mod flatten;
pub mod path;

pub use encode::{encode_documents, encode_value};
pub use flatten::{PrefabSource, flatten};

use anyhow::{Context, Result};
use std::{
    collections::HashMap,
    path::PathBuf,
    sync::{Arc, Mutex},
};
use unbound_yaml::{Document, Guid};

/// Makes the documents of an asset that is not in Unity's text serialization but acts as a prefab
/// (an imported model): `None` when the file is not one.
pub type Synthesizer =
    Box<dyn Fn(Guid, &std::path::Path) -> Option<Result<Vec<Document>>> + Send + Sync>;

/// Loads files and expands their prefab instances, keeping recently expanded prefabs for reuse.
pub struct DocumentLoader<F> {
    /// Finds the file of an asset GUID.
    locate: F,
    synthesize: Option<Synthesizer>,
    prefabs: Mutex<PrefabCache>,
}

/// The expanded prefabs kept for reuse, least recently used first out once their documents pass
/// [`PREFAB_CACHE_BYTES`]: a nested prefab is expanded once per burst of loads, while a project's
/// hundreds of prefabs do not all stay in memory (the DIY project kept 500 MB).
#[derive(Default)]
struct PrefabCache {
    entries: HashMap<Guid, (Arc<Vec<Document>>, usize, u64)>,
    bytes: usize,
    clock: u64,
}

/// The budget of [`PrefabCache`], in heap bytes of documents.
pub const PREFAB_CACHE_BYTES: usize = 64 << 20;

impl PrefabCache {
    fn get(&mut self, guid: &Guid) -> Option<Arc<Vec<Document>>> {
        self.clock += 1;
        let clock = self.clock;
        self.entries.get_mut(guid).map(|(docs, _, used)| {
            *used = clock;
            docs.clone()
        })
    }

    fn insert(&mut self, guid: Guid, docs: Arc<Vec<Document>>) {
        let size = documents_size(&docs);
        self.clock += 1;
        if let Some((_, old, _)) = self.entries.insert(guid, (docs, size, self.clock)) {
            self.bytes -= old;
        }
        self.bytes += size;
        while self.bytes > PREFAB_CACHE_BYTES && self.entries.len() > 1 {
            let oldest = *self
                .entries
                .iter()
                .filter(|(g, _)| **g != guid)
                .min_by_key(|(_, (_, _, used))| *used)
                .map(|(g, _)| g)
                .unwrap();
            let (_, size, _) = self.entries.remove(&oldest).unwrap();
            self.bytes -= size;
        }
    }

    fn clear(&mut self) {
        self.entries.clear();
        self.bytes = 0;
    }
}

/// The heap bytes of documents, roughly.
fn documents_size(docs: &[Document]) -> usize {
    docs.iter()
        .map(|d| d.body.heap_size() + d.type_name.capacity() + std::mem::size_of::<Document>())
        .sum()
}

impl<F> DocumentLoader<F>
where
    F: Fn(Guid) -> Option<PathBuf> + Send + Sync,
{
    pub fn new(locate: F) -> Self {
        Self {
            locate,
            synthesize: None,
            prefabs: Mutex::new(PrefabCache::default()),
        }
    }

    /// Prefab sources that are not YAML files (models) come from `synthesize`.
    pub fn with_synthesizer(mut self, synthesize: Synthesizer) -> Self {
        self.synthesize = Some(synthesize);
        self
    }

    /// The expanded documents of a scene, prefab or asset file.
    pub fn load(&self, file: &std::path::Path) -> Result<Vec<Document>> {
        let docs = read_documents(file)?;
        let mut stack = Vec::new();
        flatten(&docs, self, &mut stack).with_context(|| format!("expanding {}", file.display()))
    }

    /// The expanded prefabs kept: how many, and their documents' heap bytes (roughly).
    pub fn cached_prefabs(&self) -> (usize, usize) {
        let prefabs = self.prefabs.lock().unwrap();
        (prefabs.entries.len(), prefabs.bytes)
    }

    /// Forgets expanded prefabs (after a content change).
    pub fn invalidate(&self, guid: Option<Guid>) {
        let mut prefabs = self.prefabs.lock().unwrap();
        match guid {
            // A prefab change can affect every prefab nesting it; drop all to stay correct.
            Some(_) | None => prefabs.clear(),
        }
    }
}

impl<F> PrefabSource for DocumentLoader<F>
where
    F: Fn(Guid) -> Option<PathBuf> + Send + Sync,
{
    fn prefab(&self, guid: Guid, stack: &mut Vec<Guid>) -> Result<Arc<Vec<Document>>> {
        if let Some(cached) = self.prefabs.lock().unwrap().get(&guid) {
            return Ok(cached);
        }
        let file = (self.locate)(guid).with_context(|| format!("no asset has guid {guid}"))?;
        let synthesized = self.synthesize.as_ref().and_then(|s| s(guid, &file));
        let docs = match synthesized {
            Some(docs) => docs.with_context(|| format!("importing {}", file.display()))?,
            None => read_documents(&file)?,
        };
        stack.push(guid);
        let expanded = flatten(&docs, self, stack);
        stack.pop();
        let expanded = Arc::new(expanded.with_context(|| format!("expanding {}", file.display()))?);
        self.prefabs.lock().unwrap().insert(guid, expanded.clone());
        Ok(expanded)
    }
}

pub fn read_documents(file: &std::path::Path) -> Result<Vec<Document>> {
    let text =
        std::fs::read_to_string(file).with_context(|| format!("reading {}", file.display()))?;
    if !text.starts_with("%YAML") {
        anyhow::bail!(
            "{} is not in Unity's text serialization (binary assets are not supported)",
            file.display()
        );
    }
    unbound_yaml::parse_documents(&text).with_context(|| format!("parsing {}", file.display()))
}

#[cfg(test)]
mod tests;

#[cfg(test)]
mod cache_tests {
    use super::*;
    use unbound_yaml::Value;

    fn docs(bytes: usize) -> Arc<Vec<Document>> {
        Arc::new(vec![Document {
            class_id: 1,
            file_id: 1,
            stripped: false,
            type_name: String::new(),
            body: Value::Scalar("x".repeat(bytes)),
        }])
    }

    #[test]
    fn the_prefab_cache_drops_the_least_recently_used_past_its_budget() {
        let mut cache = PrefabCache::default();
        let guid = |n: u8| Guid::parse(&format!("{n:032x}")).unwrap();
        cache.insert(guid(1), docs(PREFAB_CACHE_BYTES / 2));
        cache.insert(guid(2), docs(PREFAB_CACHE_BYTES / 3));
        assert!(cache.get(&guid(1)).is_some()); // 1 is now the most recent
        cache.insert(guid(3), docs(PREFAB_CACHE_BYTES / 3));
        assert!(cache.get(&guid(2)).is_none());
        assert!(cache.get(&guid(1)).is_some() && cache.get(&guid(3)).is_some());
        assert!(cache.bytes <= PREFAB_CACHE_BYTES);
        // One prefab over the budget alone still stays (it is being used).
        cache.insert(guid(4), docs(PREFAB_CACHE_BYTES * 2));
        assert!(cache.get(&guid(4)).is_some());
        assert_eq!(cache.entries.len(), 1);
    }
}
