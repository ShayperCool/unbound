use super::*;
use std::{
    fs,
    io::Write,
    process::{Command, Stdio},
    sync::atomic::{AtomicU64, Ordering},
    time::{Duration, Instant},
};

fn fixture() -> PathBuf {
    static NEXT: AtomicU64 = AtomicU64::new(0);
    let path = crate::cache_root()
        .join("validation/shader-publication")
        .join(format!(
            "{}-{}",
            std::process::id(),
            NEXT.fetch_add(1, Ordering::Relaxed)
        ));
    fs::create_dir_all(&path).unwrap();
    path
}

fn archive() -> Vec<u8> {
    let mut zip = zip::ZipWriter::new(std::io::Cursor::new(Vec::new()));
    zip.start_file(
        "CGIncludes/UnityCG.cginc",
        zip::write::SimpleFileOptions::default(),
    )
    .unwrap();
    zip.write_all(b"complete include").unwrap();
    for dir in ["DefaultResources", "DefaultResourcesExtra", "CGIncludes"] {
        zip.add_directory(format!("{dir}/"), zip::write::SimpleFileOptions::default())
            .unwrap();
        for i in 0..80 {
            zip.start_file(
                format!("{dir}/{i}.shader"),
                zip::write::SimpleFileOptions::default()
                    .compression_method(zip::CompressionMethod::Stored),
            )
            .unwrap();
            zip.write_all(b"complete shader source").unwrap();
        }
    }
    zip.finish().unwrap().into_inner()
}

fn check_tree(root: &Path) {
    assert_eq!(
        fs::read(root.join("CGIncludes/UnityCG.cginc")).unwrap(),
        b"complete include"
    );
    for dir in ["DefaultResources", "DefaultResourcesExtra", "CGIncludes"] {
        for i in 0..80 {
            assert_eq!(
                fs::read(root.join(format!("{dir}/{i}.shader"))).unwrap(),
                b"complete shader source"
            );
        }
    }
}

fn wait_for(path: &Path) {
    let deadline = Instant::now() + Duration::from_secs(30);
    while !path.exists() {
        assert!(
            Instant::now() < deadline,
            "timed out waiting for {}",
            path.display()
        );
        std::thread::sleep(Duration::from_millis(2));
    }
}

#[test]
fn publication_child() {
    let Some(base) = std::env::var_os("UNBOUND_SHADER_PUBLICATION_CHILD") else {
        return;
    };
    let base = PathBuf::from(base);
    let mode = std::env::var("UNBOUND_SHADER_PUBLICATION_MODE").unwrap();
    wait_for(&base.join("start"));
    if mode == "failed-dependency" || mode == "recovered-dependency" {
        let install = UnityInstall {
            data: base.join("unused-install/Editor/Data"),
            version: "6000.0.71f1".parse().unwrap(),
        };
        if mode == "failed-dependency" {
            let error = format!("{:#}", BuiltinShaders::find(&install, None).unwrap_err());
            assert!(error.contains("UNBOUND_BUILTIN_SHADERS="));
            fs::write(base.join("entered"), &error).unwrap();
            wait_for(&base.join("release"));
            assert_eq!(
                format!("{:#}", BuiltinShaders::find(&install, None).unwrap_err()),
                error
            );
        } else {
            check_tree(&BuiltinShaders::find(&install, None).unwrap().root);
        }
        return;
    }
    let archive = fs::read(base.join("fixture.zip")).unwrap();
    fs::write(base.join(format!("ready-{}", std::process::id())), b"").unwrap();
    for _ in 0..8 {
        let found = acquire(
            &base.join("entry"),
            "6000.0.71f1".parse().unwrap(),
            mode == "offline",
            || {
                if mode != "interrupted" {
                    fs::File::options()
                        .write(true)
                        .create_new(true)
                        .open(base.join("acquired"))
                        .unwrap();
                }
                if mode == "interrupted" {
                    let partial = base.join("entry/.unpacking-interrupted");
                    fs::create_dir_all(&partial).unwrap();
                    fs::write(partial.join("partial.shader"), b"partial").unwrap();
                }
                if mode == "slow" || mode == "interrupted" {
                    fs::write(base.join("entered"), b"").unwrap();
                    wait_for(&base.join("release"));
                }
                Ok(archive.clone())
            },
        )
        .unwrap();
        check_tree(&found.root);
    }
}

fn child(base: &Path, name: &str, mode: &str) -> std::process::Child {
    let log = fs::File::create(base.join(format!("{name}.log"))).unwrap();
    Command::new(std::env::current_exe().unwrap())
        .args([
            "--exact",
            "builtin_shaders::publication_tests::publication_child",
            "--nocapture",
        ])
        .env("UNBOUND_SHADER_PUBLICATION_CHILD", base)
        .env("UNBOUND_SHADER_PUBLICATION_MODE", mode)
        .env("UNBOUND_BUILTIN_SHADERS", base.join("override"))
        .stdout(Stdio::from(log.try_clone().unwrap()))
        .stderr(Stdio::from(log))
        .spawn()
        .unwrap()
}

#[test]
fn competing_processes_keep_published_sources_readable() {
    let base = fixture();
    fs::write(base.join("fixture.zip"), archive()).unwrap();
    let mut children = Vec::new();
    for i in 0..4 {
        children.push(child(&base, &format!("child-{i}"), "normal"));
    }
    fs::write(base.join("start"), b"").unwrap();
    let statuses: Vec<_> = children
        .iter_mut()
        .map(|child| child.wait().unwrap())
        .collect();
    assert!(
        statuses.iter().all(|s| s.success()),
        "{statuses:?}; diagnostics: {}",
        base.display()
    );
    check_tree(&cached(&base.join("entry")).unwrap().unwrap().root);
    assert!(base.join("acquired").is_file());
}

#[test]
fn offline_reader_waits_for_the_active_publisher() {
    let base = fixture();
    fs::write(base.join("fixture.zip"), archive()).unwrap();
    fs::write(base.join("start"), b"").unwrap();
    let mut publisher = child(&base, "publisher", "slow");
    wait_for(&base.join("entered"));
    assert!(cached(&base.join("entry")).unwrap().is_none());
    let mut reader = child(&base, "reader", "offline");
    wait_for(&base.join(format!("ready-{}", reader.id())));
    std::thread::sleep(Duration::from_millis(100));
    assert!(reader.try_wait().unwrap().is_none());
    fs::write(base.join("release"), b"").unwrap();
    assert!(publisher.wait().unwrap().success(), "{}", base.display());
    assert!(reader.wait().unwrap().success(), "{}", base.display());
}

#[test]
fn interrupted_publisher_releases_lock_and_never_exposes_staging() {
    let base = fixture();
    fs::write(base.join("fixture.zip"), archive()).unwrap();
    fs::write(base.join("start"), b"").unwrap();
    let mut publisher = child(&base, "interrupted", "interrupted");
    wait_for(&base.join("entered"));
    assert!(cached(&base.join("entry")).unwrap().is_none());
    publisher.kill().unwrap();
    publisher.wait().unwrap();
    let mut next = child(&base, "recovery", "normal");
    assert!(next.wait().unwrap().success(), "{}", base.display());
    assert!(
        base.join("entry/.unpacking-interrupted/partial.shader")
            .is_file()
    );
    check_tree(&cached(&base.join("entry")).unwrap().unwrap().root);
}

#[test]
fn failed_extraction_keeps_diagnostics_and_can_be_retried() {
    let base = fixture();
    let mut broken = archive();
    let mut zip = zip::ZipArchive::new(std::io::Cursor::new(&broken)).unwrap();
    let at = zip
        .by_name("CGIncludes/79.shader")
        .unwrap()
        .data_start()
        .unwrap() as usize;
    drop(zip);
    broken[at] ^= 1; // Preserve the directory; fail CRC after extracting earlier files.
    fs::write(base.join("corrupt-fixture.zip"), &broken).unwrap();
    let version = "6000.0.71f1".parse().unwrap();
    let error = acquire(&base, version, false, || Ok(broken)).unwrap_err();
    fs::write(base.join("failure.log"), format!("{error:#}")).unwrap();
    assert!(format!("{error:#}").contains("diagnostics retained at"));
    assert!(cached(&base).unwrap().is_none());
    let failed = fs::read_dir(&base)
        .unwrap()
        .map(|e| e.unwrap().path())
        .find(|p| p.join(".failure.txt").is_file())
        .unwrap();
    assert!(failed.join("DefaultResourcesExtra/0.shader").is_file());
    let error = acquire(&base, version, true, || panic!("offline must not acquire")).unwrap_err();
    assert!(format!("{error:#}").contains("UNBOUND_OFFLINE forbids downloading"));
    let found = acquire(&base, version, false, || Ok(archive())).unwrap();
    check_tree(&found.root);
    assert_eq!(
        found.root,
        acquire(&base, version, true, || panic!(
            "complete entry must be reused"
        ))
        .unwrap()
        .root
    );
    assert!(failed.join(".failure.txt").is_file());
}

#[test]
fn session_consumers_share_success_and_failure() {
    let base = fixture();
    let failed = base.join("failed");
    assert!(session_sources(&failed, || anyhow::bail!("fixture extraction failure")).is_err());
    let error = session_sources(&failed, || {
        panic!("a renderer must not retry a managed startup failure")
    })
    .unwrap_err();
    assert!(format!("{error:#}").contains("restart to retry: fixture extraction failure"));
    let complete = base.join("complete");
    std::thread::scope(|scope| {
        for _ in 0..8 {
            let complete = &complete;
            scope.spawn(move || {
                let sources = session_sources(complete, || {
                    acquire(complete, "6000.0.71f1".parse().unwrap(), false, || {
                        Ok(archive())
                    })
                })
                .unwrap();
                check_tree(&sources.root);
            });
        }
    });
    let found =
        session_sources(&complete, || panic!("successful dependency must be reused")).unwrap();
    check_tree(&found.root);
}

#[test]
fn find_keeps_a_failed_dependency_consistent_until_process_restart() {
    let base = fixture();
    fs::create_dir_all(base.join("override/DefaultResourcesExtra")).unwrap();
    fs::write(base.join("start"), b"").unwrap();
    let mut failed = child(&base, "failed-dependency", "failed-dependency");
    wait_for(&base.join("entered"));
    unpack(&archive(), &base.join("override")).unwrap();
    fs::write(base.join("release"), b"").unwrap();
    assert!(failed.wait().unwrap().success(), "{}", base.display());
    let mut recovered = child(&base, "recovered-dependency", "recovered-dependency");
    assert!(recovered.wait().unwrap().success(), "{}", base.display());
}

#[test]
fn nested_archives_are_content_keyed_and_invalid_sources_are_refused() {
    let base = fixture();
    let mut outer = zip::ZipWriter::new(std::io::Cursor::new(Vec::new()));
    outer
        .start_file(
            "build/BuiltinShaders/builtin_shaders.zip",
            zip::write::SimpleFileOptions::default(),
        )
        .unwrap();
    outer.write_all(&archive()).unwrap();
    let bytes = outer.finish().unwrap().into_inner();
    let hash = blake3::hash(&bytes).to_hex().to_string();
    let found = acquire(&base, "6000.0.71f1".parse().unwrap(), false, || Ok(bytes)).unwrap();
    assert_eq!(found.root, base.join(hash));
    check_tree(&found.root);
    let invalid = fixture();
    fs::create_dir(invalid.join("DefaultResourcesExtra")).unwrap();
    assert!(validate_sources(&invalid).is_err());
    fs::write(invalid.join(".complete"), "../bad").unwrap();
    assert!(cached(&invalid).is_err());
}
