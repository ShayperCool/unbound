//! `unbound`: run a Unity project from its sources.
mod commands;

use clap::{Parser, Subcommand};
use std::path::PathBuf;

#[derive(Parser)]
#[command(
    name = "unbound",
    version,
    about = "Run Unity projects without the Unity Editor"
)]
struct Cli {
    #[command(subcommand)]
    command: Command,
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

fn main() -> anyhow::Result<()> {
    tracing_subscriber::fmt()
        .with_env_filter(
            tracing_subscriber::EnvFilter::try_from_env("UNBOUND_LOG")
                .unwrap_or_else(|_| "info".into()),
        )
        .with_writer(std::io::stderr)
        .init();
    let cli = Cli::parse();
    match cli.command {
        Command::Info { project } => commands::info(&project),
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
            screenshot,
            frames,
            snapshots,
            fixed_fps,
            watch,
            no_watch,
            taps,
            typing,
        } => commands::run(
            &project,
            commands::RunArgs {
                scene,
                headless,
                size,
                simulate_touch,
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
            },
        ),
        Command::Facades { project } => commands::facades(&project),
        Command::GenStubs { project } => commands::gen_stubs(&project),
        Command::ApiCheck { project } => commands::api_check(&project),
        Command::ApiGaps { project, top, json } => commands::api_gaps(&project, top, json),
    }
}
