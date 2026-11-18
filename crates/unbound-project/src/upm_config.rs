//! Scoped registry authentication from the Package Manager's user configuration file
//! (`~/.upmconfig.toml`, or the file `UPM_USER_CONFIG_FILE` names): its `[npmAuth."<registry
//! url>"]` sections, as the Unity 6000.0 Manual's "Scoped registry authentication" and "User
//! configuration file" pages describe them. `token` is sent as a Bearer token, `_auth` as Basic
//! credentials, and `alwaysAuth = true` also sends them for tarballs on another server.
//!
//! Which requests carry the header matches the Unity 6000.0.71f1 Editor (tests/projects/
//! packagesources): a section applies to the scoped registry whose URL equals its own, a trailing
//! slash aside (a section for a parent path does not apply); the package document always gets it;
//! a tarball gets it when it is on the registry's own origin (scheme, host and port; any path), or
//! anywhere with `alwaysAuth`.
//!
//! Credentials never reach a log: the [`Credential`] type prints without them.
use anyhow::{Context, Result};
use std::{
    path::{Path, PathBuf},
    sync::OnceLock,
};

/// One `[npmAuth."<url>"]` section.
#[derive(Clone, PartialEq, Eq)]
pub struct Credential {
    /// The `Authorization` header's value.
    authorization: String,
    always_auth: bool,
}

impl std::fmt::Debug for Credential {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        let scheme = self.authorization.split(' ').next().unwrap_or("");
        write!(
            f,
            "Credential({scheme} <hidden>, alwaysAuth {})",
            self.always_auth
        )
    }
}

/// The user's registry credentials.
#[derive(Debug, Default)]
pub struct UpmConfig {
    /// (registry URL without a trailing slash, its credential)
    npm_auth: Vec<(String, Credential)>,
}

/// The configuration file the Package Manager reads for the user: `UPM_USER_CONFIG_FILE`, else
/// `~/.upmconfig.toml`.
pub fn user_config_path() -> PathBuf {
    match std::env::var_os("UPM_USER_CONFIG_FILE") {
        Some(path) if !path.is_empty() => PathBuf::from(path),
        _ => crate::install::home().join(".upmconfig.toml"),
    }
}

/// The user's configuration, read once; a missing file has no credentials, and an unreadable one
/// is reported (without its contents) and has none.
pub fn user() -> &'static UpmConfig {
    static CONFIG: OnceLock<UpmConfig> = OnceLock::new();
    CONFIG.get_or_init(|| {
        let path = user_config_path();
        match read(&path) {
            Ok(config) => config,
            Err(e) => {
                tracing::warn!("{e:#}; scoped registries are queried without credentials");
                UpmConfig::default()
            }
        }
    })
}

fn read(path: &Path) -> Result<UpmConfig> {
    let text = match std::fs::read_to_string(path) {
        Ok(text) => text,
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => return Ok(UpmConfig::default()),
        Err(e) => return Err(e).with_context(|| format!("reading {}", path.display())),
    };
    parse(&text).with_context(|| format!("reading {}", path.display()))
}

fn normalized(url: &str) -> &str {
    url.trim_end_matches('/')
}

/// `scheme://host[:port]` of `url` (lower-cased), None for a URL without one.
fn origin(url: &str) -> Option<String> {
    let (scheme, rest) = url.split_once("://")?;
    let authority = rest.split(['/', '?', '#']).next()?;
    let authority = authority.rsplit('@').next()?;
    Some(format!(
        "{}://{}",
        scheme.to_ascii_lowercase(),
        authority.to_ascii_lowercase()
    ))
}

/// The configuration's `npmAuth` sections. A section needs `token` or `_auth` (with both,
/// `token` is used: unmeasured); other keys (`email`, ...) are ignored.
pub fn parse(text: &str) -> Result<UpmConfig> {
    let document: toml::Table = text.parse().map_err(|e: toml::de::Error| {
        // The parser's message quotes the offending line, which may hold a token.
        anyhow::anyhow!("not valid TOML (at byte {:?})", e.span().map(|s| s.start))
    })?;
    let mut npm_auth = Vec::new();
    let Some(sections) = document.get("npmAuth") else {
        return Ok(UpmConfig { npm_auth });
    };
    let sections = sections.as_table().context("`npmAuth` is not a table")?;
    for (url, section) in sections {
        let Some(section) = section.as_table() else {
            tracing::warn!("npmAuth.\"{url}\" is not a table; ignored");
            continue;
        };
        let text = |key: &str| section.get(key).and_then(toml::Value::as_str);
        let authorization = match (text("token"), text("_auth")) {
            (Some(token), _) => format!("Bearer {token}"),
            (None, Some(auth)) => format!("Basic {auth}"),
            (None, None) => {
                tracing::warn!("npmAuth.\"{url}\" has neither `token` nor `_auth`; ignored");
                continue;
            }
        };
        let always_auth = section
            .get("alwaysAuth")
            .and_then(toml::Value::as_bool)
            .unwrap_or(false);
        npm_auth.push((
            normalized(url).to_string(),
            Credential {
                authorization,
                always_auth,
            },
        ));
    }
    Ok(UpmConfig { npm_auth })
}

impl UpmConfig {
    fn credential(&self, registry: &str) -> Option<&Credential> {
        let registry = normalized(registry);
        self.npm_auth
            .iter()
            .find(|(url, _)| url == registry)
            .map(|(_, credential)| credential)
    }

    /// The `Authorization` header for `registry`'s package documents.
    pub fn document_authorization(&self, registry: &str) -> Option<&str> {
        self.credential(registry).map(|c| c.authorization.as_str())
    }

    /// The `Authorization` header for a tarball of `registry` at `tarball`.
    pub fn tarball_authorization(&self, registry: &str, tarball: &str) -> Option<&str> {
        let credential = self.credential(registry)?;
        let same_origin = origin(registry).is_some_and(|o| origin(tarball) == Some(o));
        (same_origin || credential.always_auth).then_some(credential.authorization.as_str())
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    const CONFIG: &str = r#"
[npmAuth."http://127.0.0.1:4873/r1"]
token = "token-one"
email = "user@example.invalid"

[npmAuth."http://127.0.0.1:4873/r2/"]
_auth = "dHdvOnNlY3JldA=="
alwaysAuth = true

[npmAuth."http://127.0.0.1:4873/r3"]
token = "short"

[npmAuth."http://127.0.0.1:4873/r3/deep"]
token = "deep"

[npmAuth."http://127.0.0.1:4873/none"]
email = "user@example.invalid"
"#;

    #[test]
    fn sections_apply_to_their_own_registry_url() {
        let config = parse(CONFIG).unwrap();
        let doc = |r| config.document_authorization(r);
        assert_eq!(doc("http://127.0.0.1:4873/r1"), Some("Bearer token-one"));
        assert_eq!(doc("http://127.0.0.1:4873/r1/"), Some("Bearer token-one"));
        assert_eq!(
            doc("http://127.0.0.1:4873/r2"),
            Some("Basic dHdvOnNlY3JldA==")
        );
        assert_eq!(doc("http://127.0.0.1:4873/r3/deep"), Some("Bearer deep"));
        // A section for a parent path, or another port, is not the registry's.
        assert_eq!(doc("http://127.0.0.1:4873/r3/other"), None);
        assert_eq!(doc("http://127.0.0.1:4874/r1"), None);
        // A section without credentials is ignored.
        assert_eq!(doc("http://127.0.0.1:4873/none"), None);
    }

    #[test]
    fn tarballs_off_the_registry_origin_need_always_auth() {
        let config = parse(CONFIG).unwrap();
        let tar = |r, t| config.tarball_authorization(r, t);
        let r1 = "http://127.0.0.1:4873/r1";
        assert_eq!(
            tar(r1, "http://127.0.0.1:4873/r1/-/a.tgz"),
            Some("Bearer token-one")
        );
        assert_eq!(
            tar(r1, "http://127.0.0.1:4873/elsewhere/a.tgz"),
            Some("Bearer token-one")
        );
        assert_eq!(tar(r1, "http://localhost:4873/r1/-/a.tgz"), None);
        assert_eq!(tar(r1, "http://127.0.0.1:9999/r1/-/a.tgz"), None);
        assert_eq!(tar(r1, "https://127.0.0.1:4873/r1/-/a.tgz"), None);
        let r2 = "http://127.0.0.1:4873/r2";
        assert_eq!(
            tar(r2, "https://cdn.example.invalid/a.tgz"),
            Some("Basic dHdvOnNlY3JldA==")
        );
        assert_eq!(
            tar("http://127.0.0.1:4873/r8", "http://127.0.0.1:4873/r8/a.tgz"),
            None
        );
    }

    #[test]
    fn credentials_stay_out_of_messages() {
        let config = parse(CONFIG).unwrap();
        assert!(!format!("{config:?}").contains("token-one"));
        let error = parse("[npmAuth.\"http://x\"]\ntoken = \"secret-value\n").unwrap_err();
        assert!(!format!("{error:#}").contains("secret-value"), "{error:#}");
    }

    #[test]
    fn empty_and_unrelated_configurations_have_no_credentials() {
        assert!(parse("").unwrap().npm_auth.is_empty());
        assert!(
            parse("enableLockFile = true\n")
                .unwrap()
                .npm_auth
                .is_empty()
        );
        assert!(
            read(Path::new("/nonexistent/.upmconfig.toml"))
                .unwrap()
                .npm_auth
                .is_empty()
        );
    }
}
