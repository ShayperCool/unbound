//! `unbound`: run a Unity project from its sources.
mod commands;

use clap::{Parser, Subcommand};
use std::path::PathBuf;

/// jemalloc instead of glibc's malloc: with the engine's many threads (Bevy's task pools, the
/// renderer, .NET) glibc's per-thread arenas kept ~300 MB of freed memory in the test game.
#[cfg(all(unix, not(target_os = "macos")))]
#[global_allocator]
static ALLOCATOR: tikv_jemallocator::Jemalloc = tikv_jemallocator::Jemalloc;

#[derive(Parser)]
#[command(
    name = "unbound",
    version,
    about = "Run Unity projects without the Unity Editor"
)]
struct Cli {
    #[command(subcommand)]
    command: Command,
    /// A Unity installation (or its `Editor/Data`) to take the release's files from instead of
    /// the Hub's installation or the downloaded Editor archive. Same as `UNBOUND_UNITY`.
    #[arg(long, global = true, value_name = "DIR")]
    unity: Option<PathBuf>,
    /// A directory of packages (`<name>` or `<name>@<version>` folders) searched before the
    /// project's package cache and the release's built-in packages; repeatable. Same as
    /// `UNBOUND_PACKAGES` (`:`-separated).
    #[arg(long = "packages", global = true, value_name = "DIR")]
    packages: Vec<PathBuf>,
}

#[derive(clap::Args, Clone)]
pub struct ProjectArgs {
    /// The Unity project directory.
    #[arg(long, short, default_value = ".")]
    pub project: PathBuf,
    /// Active build target group: Standalone, Android, iOS, WebGL.
    #[arg(long, default_value = "Standalone")]
    pub target: String,
    /// Compile as a Player (no UNITY_EDITOR) instead of as the Editor's Play mode.
    #[arg(long)]
    pub player: bool,
}

// Parsed once: the `run` variant's size does not matter.
#[allow(clippy::large_enum_variant)]
#[derive(Subcommand)]
enum Command {
    /// Show the project: release, profile, packages, assets.
    Info {
        #[command(flatten)]
        project: ProjectArgs,
    },
    /// Print the script assembly graph.
    Graph {
        #[command(flatten)]
        project: ProjectArgs,
        /// Print every assembly's sources, defines and references as JSON.
        #[arg(long)]
        json: bool,
        /// Compare with the Editor's record, `Library/Bee/*-inputdata.json`.
        #[arg(long)]
        compare_bee: Option<PathBuf>,
    },
    /// Play the project: compile, load the first scene and run frames.
    Run {
        #[command(flatten)]
        project: ProjectArgs,
        /// The scene to start with (`Assets/...`); the first build scene by default.
        #[arg(long)]
        scene: Option<String>,
        /// Run without a window for this many frames.
        #[arg(long)]
        headless: Option<u32>,
        /// Window (or headless screen) size, `WIDTHxHEIGHT`; by default the target's screen in the
        /// project's orientation (phones 540x960 or 960x540, standalone and web their default
        /// resolution).
        #[arg(long)]
        size: Option<String>,
        /// Turn the left mouse button into a touch.
        #[arg(long)]
        simulate_touch: bool,
        /// Keep PlayerPrefs in this file (Unity's Linux XML format) instead of the Editor's own
        /// (`~/.config/unity3d/<company>/<product>/prefs`), so a run does not share a game's state
        /// with an Editor playing the same project.
        #[arg(long, value_name = "FILE")]
        player_prefs: Option<PathBuf>,
        /// Application.persistentDataPath (saves, caches, the default PlayerPrefs file) in this
        /// directory instead of the Editor's.
        #[arg(long, value_name = "DIR")]
        persistent_data_path: Option<PathBuf>,
        /// Write the window's image after the last frame to this PNG file, then exit.
        #[arg(long)]
        screenshot: Option<PathBuf>,
        /// Run this many frames in the window, then exit (5 with --screenshot by default).
        #[arg(long)]
        frames: Option<u32>,
        /// Write the window's image after a frame to a PNG file and keep running:
        /// `FRAME:PATH` (repeatable).
        #[arg(long = "snapshot", value_name = "FRAME:PATH")]
        snapshots: Vec<String>,
        /// Advance game time by exactly 1/FPS per frame, whatever the wall clock says, so frame-
        /// scripted runs (`--tap`, `--type`, `--snapshot`) repeat.
        #[arg(long, value_name = "FPS")]
        fixed_fps: Option<f64>,
        /// Watch the project and hot-reload edits (the default in a window; off when headless).
        #[arg(long, overrides_with = "no_watch")]
        watch: bool,
        /// Do not watch the project for edits.
        #[arg(long)]
        no_watch: bool,
        /// Tap at a frame: `FRAME:X,Y`, in window pixels from the top left (repeatable).
        #[arg(long = "tap", value_name = "FRAME:X,Y")]
        taps: Vec<String>,
        /// Type text at a frame: `FRAME:TEXT`, where `\n` presses Return and `\b` Backspace
        /// (repeatable).
        #[arg(long = "type", value_name = "FRAME:TEXT")]
        typing: Vec<String>,
        /// Move the pointer at a frame: `FRAME:X,Y`, in window pixels from the top left; it stays
        /// there (repeatable).
        #[arg(long = "move", value_name = "FRAME:X,Y")]
        moves: Vec<String>,
        /// Press a key or mouse button at a frame and hold it: `FRAME:KEY`, a Unity KeyCode name
        /// (`W`, `Space`, `LeftArrow`, `Return`, `Mouse0` for the left button; repeatable).
        #[arg(long = "press", value_name = "FRAME:KEY")]
        presses: Vec<String>,
        /// Release a key or mouse button at a frame: `FRAME:KEY` (repeatable).
        #[arg(long = "release", value_name = "FRAME:KEY")]
        releases: Vec<String>,
        /// Press (`+KEY`) and release (`-KEY`) keys and mouse buttons in this order within a
        /// frame: `FRAME:+KEY,-KEY,...`, as when they all happen between two frames (repeatable).
        #[arg(long = "keys", value_name = "FRAME:+KEY,-KEY")]
        keys: Vec<String>,
        /// Turn the mouse wheel at a frame: `FRAME:DY` or `FRAME:DX,DY`, in notches (positive:
        /// up, right; repeatable).
        #[arg(long = "scroll", value_name = "FRAME:DY")]
        scrolls: Vec<String>,
        /// Move the pointer by an offset at a frame: `FRAME:DX,DY`, in window pixels (right,
        /// down; repeatable), as a relative mouse motion would.
        #[arg(long = "motion", value_name = "FRAME:DX,DY")]
        motions: Vec<String>,
        /// The window gains or loses the focus between frames: `FRAME:on` or `FRAME:off`, before
        /// frame FRAME (repeatable). Once given, the script decides the focus for the whole run.
        #[arg(long = "focus", value_name = "FRAME:on|off")]
        focus: Vec<String>,
        /// Play the game from a browser too: serve its picture, sound and input at this address
        /// (`ADDRESS:PORT`; `0.0.0.0:8790` by default, the local network). The page's address is
        /// printed. Needs an NVIDIA GPU (NVENC); sound needs libopus.
        #[arg(long, value_name = "ADDRESS:PORT", num_args = 0..=1, default_missing_value = "0.0.0.0:8790")]
        remote: Option<String>,
        /// How the remote page gets the media: `webrtc` (works on a plain-HTTP LAN address) or
        /// `websocket` (one TCP port; the page needs localhost or HTTPS).
        #[arg(long, value_name = "TRANSPORT", default_value = "webrtc")]
        remote_transport: String,
    },
    /// Compile the project's script assemblies.
    Compile {
        #[command(flatten)]
        project: ProjectArgs,
        /// Also compile Editor-only assemblies.
        #[arg(long)]
        editor_assemblies: bool,
        /// Compile this many times in one process (measures warm recompiles).
        #[arg(long, default_value_t = 1)]
        repeat: u32,
    },
    /// Generate (or reuse) the runtime facades of the project's Unity release: one assembly per
    /// Unity module, forwarding its types to Unbound's UnityEngine.CoreModule.
    Facades {
        #[command(flatten)]
        project: ProjectArgs,
    },
    /// Generate the compatibility stubs of the project's Unity release into
    /// managed/UnityEngine/Generated/<family>/: every type and member Unity has that the
    /// hand-written implementation lacks. Rebuild (cargo build) afterwards.
    GenStubs {
        #[command(flatten)]
        project: ProjectArgs,
    },
    /// Check the hand-written engine API against the project's Unity release: members named
    /// like Unity's with other signatures, misplaced types, enum values, accessibility, struct
    /// layouts. Exits non-zero when there are errors.
    ApiCheck {
        #[command(flatten)]
        project: ProjectArgs,
    },
    /// Check the project's shaders: whether each pass compiles (or why not).
    Shaders {
        #[command(flatten)]
        project: ProjectArgs,
        /// Check Unity's built-in shaders too.
        #[arg(long)]
        builtin: bool,
    },
    /// Run the project's tests with the Unity Test Framework (com.unity.test-framework), as the
    /// Editor's `-runTests` does: Edit Mode tests in Edit mode, Play Mode tests in Play mode,
    /// results written as NUnit XML. Exits with the framework's code (0 passed, 2 failures, 3 run
    /// error).
    Test {
        #[command(flatten)]
        project: ProjectArgs,
        /// `EditMode` or `PlayMode`.
        #[arg(long, default_value = "EditMode")]
        platform: String,
        /// The NUnit XML results file (Library/Unbound/TestResults/<platform>.xml by default).
        #[arg(long)]
        results: Option<PathBuf>,
        /// Run only tests whose full names match (semicolon-separated names or regexes).
        #[arg(long)]
        filter: Option<String>,
        /// Run only tests in these categories (semicolon-separated).
        #[arg(long)]
        category: Option<String>,
        /// Run only tests in these assemblies (semicolon-separated).
        #[arg(long)]
        assembly_names: Option<String>,
        /// Further Editor command-line options for the framework (after `--`).
        #[arg(last = true)]
        unity_args: Vec<String>,
    },
    /// List the Unity API the project's runtime assemblies use that Unbound does not implement.
    ApiGaps {
        #[command(flatten)]
        project: ProjectArgs,
        /// How many missing types and members to print.
        #[arg(long, default_value_t = 40)]
        top: usize,
        /// Print the whole report as JSON.
        #[arg(long)]
        json: bool,
    },
}

/// Ends the process after a game ran, without the C library's exit handlers: NVIDIA's driver
/// crashes in its own (status 139) while its threads still hold the device the renderer
/// leaves to the operating system. Output is flushed first.
fn exit_after(result: anyhow::Result<()>) -> anyhow::Result<()> {
    use std::io::Write;
    let code = match &result {
        Ok(()) => 0,
        Err(e) => {
            eprintln!("Error: {e:?}");
            1
        }
    };
    let _ = std::io::stdout().flush();
    let _ = std::io::stderr().flush();
    #[cfg(unix)]
    unsafe {
        libc::_exit(code)
    }
    #[cfg(not(unix))]
    result
}

fn main() -> anyhow::Result<()> {
    tracing_subscriber::fmt()
        .with_env_filter(
            tracing_subscriber::EnvFilter::try_from_env("UNBOUND_LOG")
                .unwrap_or_else(|_| "info".into()),
        )
        .with_writer(std::io::stderr)
        .init();
    let cli = Cli::parse();
    // Before any thread starts: the project is opened again in-process (by the runtime), and
    // reads these.
    if let Some(unity) = &cli.unity {
        unsafe { std::env::set_var("UNBOUND_UNITY", unity) };
    }
    if !cli.packages.is_empty() {
        let mut dirs: Vec<PathBuf> = cli.packages.clone();
        if let Some(existing) = std::env::var_os("UNBOUND_PACKAGES") {
            dirs.extend(std::env::split_paths(&existing));
        }
        let joined = std::env::join_paths(dirs)?;
        unsafe { std::env::set_var("UNBOUND_PACKAGES", joined) };
    }
    match cli.command {
        Command::Info { project } => commands::info(&project),
        Command::Shaders { project, builtin } => commands::shaders(&project, builtin),
        Command::Graph {
            project,
            json,
            compare_bee,
        } => commands::graph(&project, json, compare_bee.as_deref()),
        Command::Compile {
            project,
            editor_assemblies,
            repeat,
        } => commands::compile(&project, editor_assemblies, repeat),
        Command::Run {
            project,
            scene,
            headless,
            size,
            simulate_touch,
            player_prefs,
            persistent_data_path,
            screenshot,
            frames,
            snapshots,
            fixed_fps,
            watch,
            no_watch,
            taps,
            typing,
            moves,
            presses,
            releases,
            keys,
            scrolls,
            motions,
            focus,
            remote,
            remote_transport,
        } => exit_after(commands::run(
            &project,
            commands::RunArgs {
                scene,
                headless,
                size,
                simulate_touch,
                player_prefs,
                persistent_data_path,
                screenshot,
                frames,
                snapshots,
                fixed_fps,
                watch: if no_watch {
                    false
                } else {
                    watch || headless.is_none()
                },
                taps,
                typing,
                moves,
                presses,
                releases,
                keys,
                scrolls,
                motions,
                focus,
                remote,
                remote_transport,
            },
        )),
        Command::Facades { project } => commands::facades(&project),
        Command::GenStubs { project } => commands::gen_stubs(&project),
        Command::ApiCheck { project } => commands::api_check(&project),
        Command::ApiGaps { project, top, json } => commands::api_gaps(&project, top, json),
        Command::Test {
            project,
            platform,
            results,
            filter,
            category,
            assembly_names,
            unity_args,
        } => commands::test(
            &project,
            commands::TestArgs {
                platform,
                results,
                filter,
                category,
                assembly_names,
                unity_args,
            },
        ),
    }
}
