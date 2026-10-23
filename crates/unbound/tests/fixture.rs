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
        // GuiChecks: a click on its button, a click into its text field, then typing.
        .args([
            "--tap",
            "20:30,30",
            "--tap",
            "40:50,75",
            "--type",
            "50:hi\\bx!",
            "--fixed-fps",
            "20",
        ])
        .output()
        .unwrap();
    let log = String::from_utf8_lossy(&output.stderr).to_string()
        + &String::from_utf8_lossy(&output.stdout);
    assert!(output.status.success(), "{log}");
    assert_eq!(log.matches("hello one").count(), 3, "{log}");
    // UnityGUI: the click, the typed text (Backspace took the "i") and the automatic layout of
    // a 200-pixel area: a stretching button across it, and a flexible space pushing the last
    // button to its right edge.
    assert!(
        log.contains("gui clicks 1 text 'hx!' wide 0 200 right 200 50"),
        "{log}"
    );
    assert!(!log.contains("GUI Error"), "{log}");
    // --fixed-fps 20: 79 frames of 1/20 s each before frame 80 (the first frame has no delta).
    assert!(log.contains("fixed time 3.9500 delta 0.0500"), "{log}");
    // RuntimeChecks: a texture through PNG bytes and back (the native image codec), and an OBJ
    // model loaded as its prefab with its mesh.
    assert!(
        log.contains("image True 3x2 RGBA(1.000, 0.000, 0.000, 1.000) RGBA(0.000, 0.000, 1.000, 1.000) RGBA32"),
        "{log}"
    );
    assert!(
        log.contains("model Quad 4 vertices 2 triangles (1.00, 1.00, 0.00)"),
        "{log}"
    );
    // TextCore's font engine on the built-in font (Liberation Sans): FreeType's metrics, the
    // glyph packed after padding plus gutter, the SDF clear at the padding's corner, and kerning
    // in font units.
    assert!(
        log.contains("fontengine Success Liberation Sans Regular 90 ascent 81.475 cap 62 glyph 36 advance 60.03125 rect 60x62"),
        "{log}"
    );
    assert!(
        log.contains("atlas True at 10,10 used 1 corner 0.00"),
        "{log}"
    );
    assert!(log.contains("kern AV -152"), "{log}");
    // Floats as Unity's Mono prints them, whatever the call: 7 or 15 significant digits, and
    // other formats rounding those digits again.
    assert!(
        log.contains("format 1850 1850 1.01 0.3333333 1.01 0.3 0.3 0.3333333 0.30000000000000004"),
        "{log}"
    );
}

/// A watched headless run whose output lines arrive on a channel; `edit` runs once `ready`
/// shows up in the log. Returns the whole log and whether the run succeeded.
fn run_with_edit(name: &str, ready: &str, edit: impl FnOnce(&Path)) -> (String, bool) {
    let project = fixture_copy(name);
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
            for line in
                std::io::BufRead::lines(std::io::BufReader::new(stream)).map_while(Result::ok)
            {
                let _ = lines.send(line);
            }
        })
    })
    .collect();
    drop(lines);
    // Edit once the game runs the original code (compiling may take a while on a cold cache).
    let mut log = String::new();
    let deadline = std::time::Instant::now() + Duration::from_secs(120);
    while !log.contains(ready) {
        let wait = deadline.saturating_duration_since(std::time::Instant::now());
        match received.recv_timeout(wait) {
            Ok(line) => {
                log.push_str(&line);
                log.push('\n');
            }
            Err(_) => panic!("the game never logged {ready:?}:\n{log}"),
        }
    }
    edit(&project);
    for line in received {
        log.push_str(&line);
        log.push('\n');
    }
    for reader in readers {
        reader.join().unwrap();
    }
    let status = child.wait().unwrap();
    (log, status.success())
}

fn edit_script(project: &Path, edit: impl FnOnce(String) -> String) {
    let script = project.join("Assets/Scripts/Hello.cs");
    let text = edit(std::fs::read_to_string(&script).unwrap());
    std::fs::write(&script, text).unwrap();
}

#[test]
fn a_code_edit_is_patched_into_the_running_game() {
    if !enabled() {
        return;
    }
    // The edited method formats floats: its new IL is redirected like the loaded code's.
    let (log, ok) = run_with_edit("hot", "hello one", |project| {
        edit_script(project, |text| {
            text.replace(
                "=> \"one\"",
                "=> \"two \" + 1849.9999f.ToString() + \" \" + string.Format(\"{0:F2}\", 1.005)",
            )
        })
    });
    assert!(ok, "{log}");
    assert!(
        log.contains("hello two 1850 1.01"),
        "the edit was not applied (or formats floats as .NET does):\n{log}"
    );
    assert!(
        !log.contains("restarting"),
        "a patchable edit restarted the game:\n{log}"
    );
}

#[test]
fn an_edit_that_cannot_be_patched_restarts_the_game() {
    if !enabled() {
        return;
    }
    // A field's type changes: Edit and Continue cannot apply that to live objects.
    let (log, ok) = run_with_edit("restart", "hello one", |project| {
        edit_script(project, |text| {
            text.replace("public int every = 30;", "public long every = 30;")
                .replace("=> \"one\"", "=> \"three\"")
        })
    });
    assert!(ok, "{log}");
    assert!(log.contains("restarting it"), "{log}");
    assert!(
        log.contains("hello three"),
        "the restarted game did not run the edit:\n{log}"
    );
}
