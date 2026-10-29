//! Loaded AssetBundles (AssetBundle.LoadFromFile / LoadFromMemory): the archives decompressed in
//! memory by id until the managed side unloads them; their serialized files' documents load through
//! [`crate::state::load_documents`] with the key `bundle:<id>/<file>`, their clips' audio through
//! [`crate::state::import_audio`] with the key `bundle:<id>/<stream>:<offset>:<size>:<sound>`.

use std::collections::HashMap;
use std::sync::{Arc, Mutex, OnceLock};

use anyhow::{Context, Result};
use serde_json::json;
use unbound_assets::bundle::Bundle;

fn bundles() -> &'static Mutex<HashMap<u64, Arc<Bundle>>> {
    static BUNDLES: OnceLock<Mutex<HashMap<u64, Arc<Bundle>>>> = OnceLock::new();
    BUNDLES.get_or_init(|| Mutex::new(HashMap::new()))
}

fn next_id() -> u64 {
    static NEXT: std::sync::atomic::AtomicU64 = std::sync::atomic::AtomicU64::new(1);
    NEXT.fetch_add(1, std::sync::atomic::Ordering::Relaxed)
}

/// Opens an archive from its bytes: its id and files (path, whether serialized).
pub fn open(data: &[u8]) -> Result<String> {
    let bundle = Bundle::parse(data)?;
    let files: Vec<_> = bundle
        .files
        .iter()
        .map(|f| json!({ "path": f.path, "serialized": f.flags & unbound_assets::bundle::NODE_SERIALIZED != 0 }))
        .collect();
    let id = next_id();
    let version = bundle.unity_version.clone();
    bundles().lock().unwrap().insert(id, Arc::new(bundle));
    Ok(json!({ "id": id, "files": files, "unityVersion": version }).to_string())
}

pub fn open_file(path: &str, offset: u64) -> Result<String> {
    let data =
        std::fs::read(path).with_context(|| format!("Unable to open archive file: {path}"))?;
    let data = data
        .get(offset as usize..)
        .context("the offset lies past the file's end")?;
    open(data)
}

pub fn close(id: u64) {
    bundles().lock().unwrap().remove(&id);
}

/// The documents of a serialized file of a loaded archive, encoded for the managed side.
pub fn documents(key: &str) -> Result<Vec<u8>> {
    let (id, file) = key
        .split_once('/')
        .context("a bundle document key is bundle:<id>/<file>")?;
    let id: u64 = id.parse().context("a bundle id is a number")?;
    let bundle = bundles()
        .lock()
        .unwrap()
        .get(&id)
        .cloned()
        .with_context(|| format!("bundle {id} is not loaded"))?;
    let docs = bundle.documents(file)?;
    Ok(unbound_scene::encode_documents(&docs))
}

/// The audio of a clip in a loaded archive: the FSB5 bank at `offset` (`size` bytes) of the stream
/// file, its sound `sound`, decoded.
pub fn audio(key: &str) -> Result<unbound_assets::fsb5::Decoded> {
    let key = key.strip_prefix("bundle:").unwrap_or(key);
    let (id, rest) = key
        .split_once('/')
        .context("a bundle audio key is bundle:<id>/<stream>:<offset>:<size>:<sound>")?;
    let mut parts = rest.rsplitn(4, ':');
    let (sound, size, offset, stream) = (parts.next(), parts.next(), parts.next(), parts.next());
    let (Some(sound), Some(size), Some(offset), Some(stream)) = (sound, size, offset, stream)
    else {
        anyhow::bail!("a bundle audio key is bundle:<id>/<stream>:<offset>:<size>:<sound>");
    };
    let id: u64 = id.parse().context("a bundle id is a number")?;
    let bundle = bundles()
        .lock()
        .unwrap()
        .get(&id)
        .cloned()
        .with_context(|| format!("bundle {id} is not loaded"))?;
    let bytes = bundle
        .stream(stream, offset.parse()?, size.parse()?)
        .with_context(|| {
            format!("the bundle has no audio data at {stream}:{offset} ({size} bytes)")
        })?;
    unbound_assets::fsb5::decode(bytes, sound.parse()?)
}
