//! What shaders declare (SubShaders and their tags, pass names and tags, keyword spaces), for the
//! managed `Shader`/`ComputeShader` metadata API. A provider the host installs answers, with or
//! without a renderer; the managed side asks through the native API's string `call`
//! (`shader-declarations`, `compute-declarations`), so the typed native API is unchanged.
//!
//! Answers are JSON. A shader (`{"name": ...}`):
//!
//! ```text
//! { "found": bool,             // a shader by the name exists
//!   "builtin": bool,           // answered from Unity's serialized built-in shader
//!   "subshaders_known": bool,  // `subshaders` is complete (own, then the Fallback chain's)
//!   "keywords_known": bool,    // `keywords` is the complete keyword space
//!   "transient": bool,         // something unknown may become known (an unreadable file):
//!                              // ask again later instead of keeping this answer
//!   "reason": string,          // why something is unknown (empty when everything is known)
//!   "subshaders": [{ "tags": [[name, value]], "passes": [{ "name", "tags": [[name, value]] }] }],
//!   "keywords": [{ "name", "local", "dynamic" }] }
//! ```
//!
//! A compute shader file (`{"file": ...}`): `{ found, keywords_known, transient, reason,
//! keywords }`. A known keyword list may be empty (a file that declares nothing); an unknown one
//! is never presented as empty. Without a provider both methods fail, and the managed side keeps
//! its name-based behaviour.
use std::sync::{Arc, RwLock};

pub trait Provider: Send + Sync {
    /// A shader by name (see the module documentation).
    fn shader(&self, name: &str) -> String;
    /// A compute shader file's keywords (see the module documentation).
    fn compute(&self, file: &str) -> String;
}

static PROVIDER: RwLock<Option<Arc<dyn Provider>>> = RwLock::new(None);

/// Installs the provider (None removes it). Installing must not read anything: the provider
/// does its work when asked.
pub fn set_provider(provider: Option<Arc<dyn Provider>>) {
    *PROVIDER.write().unwrap_or_else(|e| e.into_inner()) = provider;
}

pub(crate) fn provider() -> Option<Arc<dyn Provider>> {
    PROVIDER.read().unwrap_or_else(|e| e.into_inner()).clone()
}

/// The message of the error both methods give without a provider.
pub const UNAVAILABLE: &str = "shader declarations are not available in this host";

/// `shader-declarations`: `{"name": ...}`.
pub(crate) fn shader_request(request: &str) -> anyhow::Result<String> {
    #[derive(serde::Deserialize)]
    struct Request {
        name: String,
    }
    let r: Request = serde_json::from_str(request)?;
    let provider = provider().ok_or_else(|| anyhow::anyhow!(UNAVAILABLE))?;
    Ok(provider.shader(&r.name))
}

/// `compute-declarations`: `{"file": ...}`.
pub(crate) fn compute_request(request: &str) -> anyhow::Result<String> {
    #[derive(serde::Deserialize)]
    struct Request {
        file: String,
    }
    let r: Request = serde_json::from_str(request)?;
    let provider = provider().ok_or_else(|| anyhow::anyhow!(UNAVAILABLE))?;
    Ok(provider.compute(&r.file))
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::sync::Mutex;

    /// The provider is process-wide: tests that install one take turns.
    static SERIAL: Mutex<()> = Mutex::new(());

    struct Fixed;

    impl Provider for Fixed {
        fn shader(&self, name: &str) -> String {
            format!("{{\"found\":true,\"asked\":\"{name}\"}}")
        }
        fn compute(&self, file: &str) -> String {
            format!("{{\"found\":false,\"asked\":\"{file}\"}}")
        }
    }

    #[test]
    fn requests_reach_the_installed_provider_or_fail_without_one() {
        let _turn = SERIAL.lock().unwrap_or_else(|e| e.into_inner());
        set_provider(None);
        let err = shader_request(r#"{"name":"Standard"}"#)
            .unwrap_err();
        assert!(format!("{err:#}").contains(UNAVAILABLE));
        let err = compute_request(r#"{"file":"a.compute"}"#)
            .unwrap_err();
        assert!(format!("{err:#}").contains(UNAVAILABLE));

        set_provider(Some(Arc::new(Fixed)));
        assert_eq!(
            shader_request(r#"{"name":"Standard"}"#).unwrap(),
            r#"{"found":true,"asked":"Standard"}"#
        );
        assert_eq!(
            compute_request(r#"{"file":"a.compute"}"#).unwrap(),
            r#"{"found":false,"asked":"a.compute"}"#
        );
        // A malformed request is an error, not an answer.
        assert!(shader_request("{}").is_err());
        set_provider(None);
    }
}
