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
        /// Window (or headless screen) size, `WIDTHxHEIGHT`.
        #[arg(long, default_value = "540x960")]
        size: String,
        /// Turn the left mouse button into a touch.
        #[arg(long)]
        simulate_touch: bool,
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
        } => commands::run(&project, scene, headless, &size, simulate_touch),
        Command::Facades { project } => commands::facades(&project),
        Command::ApiGaps { project, top, json } => commands::api_gaps(&project, top, json),
    }
}
