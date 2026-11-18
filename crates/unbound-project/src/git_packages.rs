//! Git dependencies (`"source": "git"`) that no package cache holds, fetched into the Unbound
//! package store with the git command, as the Unity 6000.0 Manual's "Git dependencies" page
//! describes them: the URL forms (`git+` is only a marker and is removed), `?path=` naming the
//! package's folder in the repository and `#revision` a branch, tag or full commit hash. The lock
//! file's `hash` pins the commit (tests/projects/packagesources: the 6000.0.71f1 Editor installs
//! the locked commit although the branch has moved on); without one the revision is resolved
//! with `git ls-remote`.
//!
//! The commit is checked out (the repository's export attributes do not apply), with the
//! repository as `origin`, so the user's git-lfs, when installed, brings large files as for the
//! Editor; without it they stay pointer files, as the Manual says the Editor leaves them
//! (unmeasured).
//!
//! Authentication is the user's own git setup (credential helpers, ssh keys and agents). Git runs
//! without input and with `GIT_TERMINAL_PROMPT=0`, so a credential prompt fails instead of
//! waiting, as the Editor cannot prompt either; ssh itself may still ask on the terminal (an
//! unknown host key, a passphrase) unless the user's ssh setup has `BatchMode`.
//! `UNBOUND_OFFLINE` forbids the fetch. Each package is kept as `git/<name>@<key>`, the key a
//! hash of the store format, the repository URL, the commit and the folder, so another commit
//! or folder is another entry.
use anyhow::{Context, Result, bail};
use std::{
    path::{Path, PathBuf},
    process::Command,
};

/// A git dependency's version string, taken apart.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct GitSpec {
    /// The repository URL as git takes it (without `git+`, `?path=` and `#revision`).
    pub url: String,
    /// The package's folder in the repository, without leading or trailing slashes ("" for the
    /// root).
    pub path: String,
    pub revision: Option<String>,
}

/// `version` (`git+https://host/repo.git?path=/folder#v1.2.3`, an scp-like `git@host:repo.git`,
/// ...) as a [`GitSpec`].
pub fn parse_spec(version: &str) -> GitSpec {
    let (rest, revision) = match version.split_once('#') {
        Some((rest, revision)) if !revision.is_empty() => (rest, Some(revision.to_string())),
        Some((rest, _)) => (rest, None),
        None => (version, None),
    };
    let (url, query) = rest.split_once('?').unwrap_or((rest, ""));
    let path = query
        .split('&')
        .find_map(|pair| pair.strip_prefix("path="))
        .unwrap_or("")
        .trim_matches('/')
        .to_string();
    let url = url.strip_prefix("git+").unwrap_or(url).to_string();
    GitSpec {
        url,
        path,
        revision,
    }
}

/// `url` without the credentials it may carry (`https://user:token@host/...`), for messages.
pub fn redacted(url: &str) -> String {
    let Some((scheme, rest)) = url.split_once("://") else {
        return url.to_string();
    };
    let end = rest.find(['/', '?', '#']).unwrap_or(rest.len());
    match rest[..end].rfind('@') {
        Some(at) => format!("{scheme}://<credentials>@{}", &rest[at + 1..]),
        None => url.to_string(),
    }
}

fn is_commit(revision: &str) -> bool {
    revision.len() == 40 && revision.bytes().all(|b| b.is_ascii_hexdigit())
}

fn git(dir: Option<&Path>, args: &[&str]) -> Result<Vec<u8>> {
    let mut command = Command::new("git");
    if let Some(dir) = dir {
        command.current_dir(dir);
    }
    let output = command
        .args(args)
        .env("GIT_TERMINAL_PROMPT", "0")
        .stdin(std::process::Stdio::null())
        .output()
        .context("running git (git packages need the git command on PATH)")?;
    if !output.status.success() {
        let stderr = String::from_utf8_lossy(&output.stderr);
        let last = stderr
            .lines()
            .rev()
            .find(|l| !l.trim().is_empty())
            .unwrap_or("");
        // The subcommand: the first argument that is neither an option nor a `-c` setting.
        let subcommand = args
            .iter()
            .find(|a| !a.starts_with('-') && !a.contains('='))
            .unwrap_or(&"");
        bail!("git {subcommand} failed: {}", redacted(last.trim()));
    }
    Ok(output.stdout)
}

/// The commit `revision` (or the default branch) names in `url`: a full hash as it is, else the
/// peeled tag, branch or tag `git ls-remote` lists.
fn resolve_revision(url: &str, revision: Option<&str>) -> Result<String> {
    if let Some(revision) = revision.filter(|r| is_commit(r)) {
        return Ok(revision.to_ascii_lowercase());
    }
    let wanted = revision.unwrap_or("HEAD");
    // The peeled pattern lists an annotated tag's commit beside the tag object.
    let peeled = format!("{wanted}^{{}}");
    let listing = String::from_utf8(git(None, &["ls-remote", url, wanted, &peeled])?)?;
    let refs: Vec<(&str, &str)> = listing
        .lines()
        .filter_map(|line| line.split_once('\t'))
        .collect();
    let find = |name: String| {
        refs.iter()
            .find(|(_, r)| *r == name)
            .map(|(h, _)| h.to_string())
    };
    find(format!("refs/tags/{wanted}^{{}}"))
        .or_else(|| find(format!("refs/heads/{wanted}")))
        .or_else(|| find(format!("refs/tags/{wanted}")))
        .or_else(|| find(wanted.to_string()))
        .with_context(|| format!("{} has no revision `{wanted}`", redacted(url)))
}

/// The package `name` of `spec` at `hash` (the lock's; resolved from the revision when None) in
/// the store's `git/` folder, fetched when it is not there yet; `offline` (`UNBOUND_OFFLINE`)
/// forbids reaching the repository.
pub fn fetch(
    store: &Path,
    name: &str,
    spec: &GitSpec,
    hash: Option<&str>,
    offline: bool,
) -> Result<PathBuf> {
    // Neither may reach git as an option or leave the checkout.
    anyhow::ensure!(
        !spec.url.is_empty() && !spec.url.starts_with('-'),
        "`{}` is not a repository URL",
        redacted(&spec.url)
    );
    anyhow::ensure!(
        Path::new(&spec.path)
            .components()
            .all(|c| matches!(c, std::path::Component::Normal(_))),
        "`?path=/{}` is not a folder of the repository",
        spec.path
    );
    let hash = match hash {
        Some(hash) => hash.to_ascii_lowercase(),
        None if offline => bail!(
            "no package cache has it, the lock file pins no commit, and UNBOUND_OFFLINE forbids \
             asking {} for one",
            redacted(&spec.url)
        ),
        None => resolve_revision(&spec.url, spec.revision.as_deref())?,
    };
    anyhow::ensure!(
        is_commit(&hash),
        "the lock file's hash `{hash}` is not a full commit hash"
    );
    let key = blake3::hash(
        format!(
            "{}\0{}\0{hash}\0{}",
            crate::packages::STORE_FORMAT,
            spec.url,
            spec.path
        )
        .as_bytes(),
    )
    .to_hex();
    let folder = store.join("git");
    let root = folder.join(format!("{name}@{}", &key[..16]));
    if crate::packages::package_name(&root).as_deref() == Some(name) {
        return Ok(root);
    }
    if offline {
        bail!(
            "no package cache has it, and UNBOUND_OFFLINE forbids fetching {} at {hash}",
            redacted(&spec.url)
        );
    }
    tracing::info!("package {name}: fetching {} at {hash}", redacted(&spec.url));
    std::fs::create_dir_all(&folder)?;
    let scratch = folder.join(format!(".{name}@{}.{}", &key[..16], std::process::id()));
    let _ = std::fs::remove_dir_all(&scratch);
    let repository = scratch.join("repository");
    let tree = scratch.join("tree");
    std::fs::create_dir_all(&repository)?;
    std::fs::create_dir_all(&tree)?;
    let result = (|| {
        let repo = Some(repository.as_path());
        git(repo, &["init", "--bare", "-q"])?;
        git(repo, &["remote", "add", "origin", &spec.url])?;
        // A shallow fetch of the commit, as the Editor's; a server that refuses to serve an
        // unadvertised commit gets a full fetch of its branches and tags.
        if let Err(e) = git(repo, &["fetch", "-q", "--depth", "1", "origin", &hash]) {
            tracing::debug!("package {name}: {e:#}; fetching every branch and tag");
            git(
                repo,
                &[
                    "fetch",
                    "-q",
                    "origin",
                    "+refs/heads/*:refs/heads/*",
                    "+refs/tags/*:refs/tags/*",
                ],
            )?;
        }
        let commit = format!("{hash}^{{commit}}");
        git(repo, &["cat-file", "-e", &commit])
            .with_context(|| format!("{} has no commit {hash}", redacted(&spec.url)))?;
        let work_tree = format!("--work-tree={}", tree.display());
        let folder = if spec.path.is_empty() {
            "."
        } else {
            spec.path.as_str()
        };
        // The whole commit in the index first, so the attributes above the folder (the root
        // `.gitattributes` with git-lfs's rules, line endings) apply to its files.
        let checkout = |args: &[&str]| {
            let mut all = vec![
                "--literal-pathspecs",
                work_tree.as_str(),
                "-c",
                "core.bare=false",
            ];
            all.extend_from_slice(args);
            git(repo, &all)
        };
        checkout(&["read-tree", &hash])?;
        checkout(&["checkout", "-q", &hash, "--", folder])
            .with_context(|| format!("checking out `{folder}` at {hash}"))?;
        let package = tree.join(&spec.path);
        // A folder of the checkout, not a link out of it.
        anyhow::ensure!(
            std::fs::symlink_metadata(&package).is_ok_and(|m| m.is_dir()),
            "`/{}` at {hash} is not a folder",
            spec.path
        );
        anyhow::ensure!(
            crate::packages::package_name(&package).as_deref() == Some(name),
            "`/{}` at {hash} holds no package.json naming {name}",
            spec.path
        );
        let _ = std::fs::remove_dir_all(&root);
        std::fs::rename(&package, &root)?;
        Ok(())
    })();
    let _ = std::fs::remove_dir_all(&scratch);
    result?;
    tracing::info!("package {name}: unpacked into {}", root.display());
    Ok(root)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn version_strings_split_into_url_folder_and_revision() {
        assert_eq!(
            parse_spec("git+https://host.invalid/user/repo.git?path=/example/folder#v1.2.3"),
            GitSpec {
                url: "https://host.invalid/user/repo.git".into(),
                path: "example/folder".into(),
                revision: Some("v1.2.3".into())
            }
        );
        assert_eq!(
            parse_spec("git@host.invalid:user/repo.git"),
            GitSpec {
                url: "git@host.invalid:user/repo.git".into(),
                path: "".into(),
                revision: None
            }
        );
        let spec = parse_spec("ssh://git@host.invalid/user/repo.git#");
        assert_eq!(
            (spec.url.as_str(), spec.revision),
            ("ssh://git@host.invalid/user/repo.git", None)
        );
    }

    #[test]
    fn credentials_in_urls_are_redacted() {
        assert_eq!(
            redacted("https://user:secret@host.invalid/repo.git"),
            "https://<credentials>@host.invalid/repo.git"
        );
        assert_eq!(
            redacted("git@host.invalid:repo.git"),
            "git@host.invalid:repo.git"
        );
        assert_eq!(
            redacted("https://host.invalid/a@b"),
            "https://host.invalid/a@b"
        );
    }

    /// A scratch folder of this test process.
    fn scratch(name: &str) -> PathBuf {
        let dir = std::env::temp_dir().join(format!("unbound-git-{name}-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        dir
    }

    fn run(dir: &Path, args: &[&str]) -> String {
        let output = Command::new("git")
            .current_dir(dir)
            .args(args)
            .env("GIT_CONFIG_GLOBAL", "/dev/null")
            .env("GIT_CONFIG_NOSYSTEM", "1")
            .env("GIT_AUTHOR_NAME", "test")
            .env("GIT_AUTHOR_EMAIL", "test@example.invalid")
            .env("GIT_COMMITTER_NAME", "test")
            .env("GIT_COMMITTER_EMAIL", "test@example.invalid")
            .output()
            .unwrap();
        assert!(
            output.status.success(),
            "git {args:?}: {}",
            String::from_utf8_lossy(&output.stderr)
        );
        String::from_utf8(output.stdout).unwrap().trim().to_string()
    }

    /// A bare repository whose tag v1 has `pkgs/thing` at 1.0.0 and whose main has moved on to
    /// 1.0.1; returns (its file URL, the v1 commit, the main commit).
    fn repository(dir: &Path) -> (String, String, String) {
        let work = dir.join("work");
        std::fs::create_dir_all(work.join("pkgs/thing")).unwrap();
        run(&work, &["init", "-q", "-b", "main"]);
        let mut commits = Vec::new();
        for version in ["1.0.0", "1.0.1"] {
            std::fs::write(
                work.join("pkgs/thing/package.json"),
                format!(r#"{{"name": "com.test.thing", "version": "{version}"}}"#),
            )
            .unwrap();
            std::fs::write(work.join("README.md"), version).unwrap();
            // Export attributes apply to archives, not to the checkout the Editor makes.
            std::fs::create_dir_all(work.join("pkgs/thing/Tests")).unwrap();
            std::fs::write(work.join("pkgs/thing/Tests/T.cs"), "class T {}").unwrap();
            // The root's attributes apply inside the package folder.
            std::fs::write(
                work.join(".gitattributes"),
                "/pkgs/thing/Tests export-ignore\n*.txt text eol=crlf\n",
            )
            .unwrap();
            std::fs::write(work.join("pkgs/thing/Notes.txt"), "a\nb\n").unwrap();
            run(&work, &["add", "-A"]);
            run(&work, &["commit", "-q", "-m", version]);
            commits.push(run(&work, &["rev-parse", "HEAD"]));
            if version == "1.0.0" {
                run(&work, &["tag", "-a", "v1", "-m", "v1"]);
            }
        }
        let bare = dir.join("repo.git");
        run(
            dir,
            &[
                "clone",
                "-q",
                "--bare",
                work.to_str().unwrap(),
                bare.to_str().unwrap(),
            ],
        );
        (
            format!("file://{}", bare.display()),
            commits.remove(0),
            commits.remove(0),
        )
    }

    fn version(root: &Path) -> String {
        let text = std::fs::read_to_string(root.join("package.json")).unwrap();
        let json: serde_json::Value = serde_json::from_str(&text).unwrap();
        json["version"].as_str().unwrap().to_string()
    }

    #[test]
    fn the_locked_commit_and_folder_are_fetched_into_the_store() {
        let dir = scratch("locked");
        let (url, v1, main) = repository(&dir);
        let store = dir.join("store");
        let spec = parse_spec(&format!("git+{url}?path=/pkgs/thing#main"));
        // The lock's commit wins over the branch, which has moved on.
        let root = fetch(&store, "com.test.thing", &spec, Some(&v1), false).unwrap();
        assert_eq!(version(&root), "1.0.0");
        assert!(root.starts_with(store.join("git")));
        assert!(
            !root.join("README.md").exists(),
            "only the package folder is kept"
        );
        assert!(
            root.join("Tests/T.cs").is_file(),
            "export-ignore does not apply"
        );
        assert_eq!(
            std::fs::read(root.join("Notes.txt")).unwrap(),
            b"a\r\nb\r\n"
        );
        // Without a lock the revision is resolved: the branch's head, an annotated tag's commit.
        let head = fetch(&store, "com.test.thing", &spec, None, false).unwrap();
        assert_eq!(version(&head), "1.0.1");
        assert_ne!(head, root);
        let tagged = parse_spec(&format!("{url}?path=pkgs/thing/#v1"));
        assert_eq!(
            fetch(&store, "com.test.thing", &tagged, None, false).unwrap(),
            root
        );
        assert_eq!(resolve_revision(&url, Some(&main)).unwrap(), main);
        // Fetched entries are found again offline; nothing else is.
        assert_eq!(
            fetch(&store, "com.test.thing", &spec, Some(&v1), true).unwrap(),
            root
        );
        let other = parse_spec(&format!("{url}?path=/pkgs/other"));
        let error = fetch(&store, "com.test.thing", &other, Some(&v1), true).unwrap_err();
        assert!(
            format!("{error:#}").contains("UNBOUND_OFFLINE"),
            "{error:#}"
        );
        assert!(fetch(&store, "com.test.thing", &spec, None, true).is_err());
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn a_missing_commit_or_folder_fails_and_leaves_nothing_behind() {
        let dir = scratch("missing");
        let (url, v1, _) = repository(&dir);
        let store = dir.join("store");
        let missing = "0123456789abcdef0123456789abcdef01234567";
        let spec = parse_spec(&format!("{url}?path=/pkgs/thing"));
        assert!(fetch(&store, "com.test.thing", &spec, Some(missing), false).is_err());
        let wrong_folder = parse_spec(&format!("{url}?path=/pkgs"));
        assert!(fetch(&store, "com.test.thing", &wrong_folder, Some(&v1), false).is_err());
        let wrong_name = parse_spec(&format!("{url}?path=/pkgs/thing"));
        assert!(fetch(&store, "com.test.other", &wrong_name, Some(&v1), false).is_err());
        let glob = parse_spec(&format!("{url}?path=/pkgs/*"));
        assert!(fetch(&store, "com.test.thing", &glob, Some(&v1), false).is_err());
        let escaping = parse_spec(&format!("{url}?path=/../pkgs/thing"));
        assert!(fetch(&store, "com.test.thing", &escaping, Some(&v1), false).is_err());
        let option = parse_spec("--upload-pack=touch /tmp/x");
        assert!(fetch(&store, "com.test.thing", &option, Some(&v1), false).is_err());
        let left: Vec<_> = std::fs::read_dir(store.join("git"))
            .unwrap()
            .flatten()
            .collect();
        assert!(left.is_empty(), "{left:?}");
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn a_package_at_the_repository_root() {
        let dir = scratch("root");
        let work = dir.join("work");
        std::fs::create_dir_all(work.join("Runtime")).unwrap();
        std::fs::write(
            work.join("package.json"),
            r#"{"name": "com.test.root", "version": "2.0.0"}"#,
        )
        .unwrap();
        std::fs::write(work.join("Runtime/A.cs"), "class A {}").unwrap();
        run(&work, &["init", "-q", "-b", "main"]);
        run(&work, &["add", "-A"]);
        run(&work, &["commit", "-q", "-m", "root"]);
        let commit = run(&work, &["rev-parse", "HEAD"]);
        let url = format!("file://{}", work.display());
        let spec = parse_spec(&format!("{url}#main"));
        let root = fetch(
            &dir.join("store"),
            "com.test.root",
            &spec,
            Some(&commit),
            false,
        )
        .unwrap();
        assert_eq!(version(&root), "2.0.0");
        assert!(root.join("Runtime/A.cs").is_file());
        assert!(!root.join(".git").exists());
        let _ = std::fs::remove_dir_all(&dir);
    }
}
