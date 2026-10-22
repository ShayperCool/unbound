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
    }
}
