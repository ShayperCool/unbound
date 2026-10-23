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
pub type Synthesizer = Box<dyn Fn(Guid, &std::path::Path) -> Option<Result<Vec<Document>>> + Send + Sync>;

/// Loads files and expands their prefab instances, keeping every expanded prefab for reuse.
pub struct DocumentLoader<F> {
    /// Finds the file of an asset GUID.
    locate: F,
    synthesize: Option<Synthesizer>,
    prefabs: Mutex<HashMap<Guid, Arc<Vec<Document>>>>,
}

impl<F> DocumentLoader<F>
where
    F: Fn(Guid) -> Option<PathBuf> + Send + Sync,
{
    pub fn new(locate: F) -> Self {
        Self {
            locate,
            synthesize: None,
            prefabs: Mutex::new(HashMap::new()),
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
            return Ok(cached.clone());
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
