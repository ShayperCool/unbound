//! End-to-end checks on the fixture project (tests/projects/hello): the game runs, and a code
//! edit made while it runs is patched in. They start the managed runtime, so they run only with
//! `UNBOUND_INTEGRATION=1`.
use std::{
    path::{Path, PathBuf},
    process::Command,
    time::Duration,
};

fn enabled() -> bool {
    std::env::var_os("UNBOUND_INTEGRATION").is_some()
}

/// A fresh copy of the fixture (tests never edit the repository's).
fn fixture_copy(name: &str) -> PathBuf {
    let source = Path::new(env!("CARGO_MANIFEST_DIR")).join("../../tests/projects/hello");
    let target = std::env::temp_dir().join(format!("unbound-fixture-{name}-{}", std::process::id()));
    let _ = std::fs::remove_dir_all(&target);
    copy_dir(&source, &target);
    target
}

fn copy_dir(from: &Path, to: &Path) {
    std::fs::create_dir_all(to).unwrap();
    for entry in std::fs::read_dir(from).unwrap().flatten() {
        let path = entry.path();
        if path.file_name().is_some_and(|n| n == "Library") {
            continue;
        }
        let dest = to.join(entry.file_name());
        if path.is_dir() {
            copy_dir(&path, &dest);
        } else {
            std::fs::copy(&path, &dest).unwrap();
        }
    }
}

fn unbound() -> Command {
    Command::new(env!("CARGO_BIN_EXE_unbound"))
}

#[test]
fn the_fixture_runs_its_script() {
    if !enabled() {
        return;
    }
    let project = fixture_copy("run");
    let output = unbound()
        .args(["run", "--headless", "100", "-p"])
        .arg(&project)
        .output()
        .unwrap();
    let log = String::from_utf8_lossy(&output.stderr).to_string() + &String::from_utf8_lossy(&output.stdout);
    assert!(output.status.success(), "{log}");
    assert_eq!(log.matches("hello one").count(), 3, "{log}");
}

#[test]
fn a_code_edit_is_patched_into_the_running_game() {
    if !enabled() {
        return;
    }
    let project = fixture_copy("hot");
    let mut child = unbound()
        .args(["run", "--headless", "420", "--watch", "-p"])
        .arg(&project)
        .stdout(std::process::Stdio::piped())
        .stderr(std::process::Stdio::piped())
        .spawn()
        .unwrap();
    // Both streams, line by line, into one channel.
    let (lines, received) = std::sync::mpsc::channel::<String>();
    let readers: Vec<_> = [
        Box::new(child.stdout.take().unwrap()) as Box<dyn std::io::Read + Send>,
        Box::new(child.stderr.take().unwrap()),
    ]
    .into_iter()
    .map(|stream| {
        let lines = lines.clone();
        std::thread::spawn(move || {
            for line in std::io::BufRead::lines(std::io::BufReader::new(stream)).map_while(Result::ok) {
                let _ = lines.send(line);
            }
        })
    })
    .collect();
    drop(lines);
    // Edit once the game runs the original code (compiling may take a while on a cold cache).
    let mut log = String::new();
    let deadline = std::time::Instant::now() + Duration::from_secs(120);
    while !log.contains("hello one") {
        let wait = deadline.saturating_duration_since(std::time::Instant::now());
        match received.recv_timeout(wait) {
            Ok(line) => {
                log.push_str(&line);
                log.push('\n');
            }
            Err(_) => panic!("the game never ran the original code:\n{log}"),
        }
    }
    let script = project.join("Assets/Scripts/Hello.cs");
    let text = std::fs::read_to_string(&script).unwrap().replace("=> \"one\"", "=> \"two\"");
    std::fs::write(&script, text).unwrap();
    for line in received {
        log.push_str(&line);
        log.push('\n');
    }
    for reader in readers {
        reader.join().unwrap();
    }
    let status = child.wait().unwrap();
    assert!(status.success(), "{log}");
    assert!(log.contains("hello two"), "the edit was not applied:\n{log}");
}
