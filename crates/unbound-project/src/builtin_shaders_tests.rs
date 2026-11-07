use super::*;
use std::{fs, io::Write, process::{Command, Stdio}, sync::atomic::{AtomicU64, Ordering}, time::{Duration, Instant}};

fn fixture() -> PathBuf {
    static NEXT: AtomicU64 = AtomicU64::new(0);
    let path = crate::cache_root().join("validation/shader-publication")
        .join(format!("{}-{}", std::process::id(), NEXT.fetch_add(1, Ordering::Relaxed)));
    fs::create_dir_all(&path).unwrap();
    path
}

fn archive() -> Vec<u8> {
    let mut zip = zip::ZipWriter::new(std::io::Cursor::new(Vec::new()));
    for dir in ["DefaultResources", "DefaultResourcesExtra", "CGIncludes"] {
        zip.add_directory(format!("{dir}/"), zip::write::SimpleFileOptions::default()).unwrap();
        for i in 0..80 {
            zip.start_file(format!("{dir}/{i}.shader"), zip::write::SimpleFileOptions::default()).unwrap();
            zip.write_all(b"complete shader source").unwrap();
        }
    }
    zip.finish().unwrap().into_inner()
}

fn check_tree(root: &Path) {
    for dir in ["DefaultResources", "DefaultResourcesExtra", "CGIncludes"] {
        for i in 0..80 {
            assert_eq!(fs::read(root.join(format!("{dir}/{i}.shader"))).unwrap(), b"complete shader source");
        }
    }
}

fn wait_for(path: &Path) {
    let deadline = Instant::now() + Duration::from_secs(30);
    while !path.exists() {
        assert!(Instant::now() < deadline, "timed out waiting for {}", path.display());
        std::thread::sleep(Duration::from_millis(2));
    }
}

#[test]
fn publication_child() {
    let Some(base) = std::env::var_os("UNBOUND_SHADER_PUBLICATION_CHILD") else { return };
    let base = PathBuf::from(base);
    wait_for(&base.join("start"));
    let archive = fs::read(base.join("fixture.zip")).unwrap();
    for _ in 0..8 {
        unpack(&archive, &base.join("entry")).unwrap();
        check_tree(&base.join("entry"));
    }
}

#[test]
fn competing_processes_keep_published_sources_readable() {
    let base = fixture();
    fs::write(base.join("fixture.zip"), archive()).unwrap();
    let mut children = Vec::new();
    for i in 0..4 {
        let log = fs::File::create(base.join(format!("child-{i}.log"))).unwrap();
        children.push(Command::new(std::env::current_exe().unwrap())
            .args(["--exact", "builtin_shaders::publication_tests::publication_child", "--nocapture"])
            .env("UNBOUND_SHADER_PUBLICATION_CHILD", &base)
            .stdout(Stdio::from(log.try_clone().unwrap())).stderr(Stdio::from(log)).spawn().unwrap());
    }
    fs::write(base.join("start"), b"").unwrap();
    let statuses: Vec<_> = children.iter_mut().map(|child| child.wait().unwrap()).collect();
    assert!(statuses.iter().all(|s| s.success()), "{statuses:?}; diagnostics: {}", base.display());
    check_tree(&base.join("entry"));
}
