//! Builds the managed engine (`managed/`) into `target/managed` whenever its sources change, so
//! that `cargo build` alone produces a runnable tool.
use std::{
    path::{Path, PathBuf},
    process::Command,
};

fn main() {
    let manifest = PathBuf::from(std::env::var("CARGO_MANIFEST_DIR").unwrap());
    let root = manifest.parent().unwrap().parent().unwrap();
    let managed = root.join("managed");
    println!("cargo:rerun-if-changed={}", managed.display());
    println!("cargo:rerun-if-env-changed=UNBOUND_SKIP_MANAGED_BUILD");
    if std::env::var_os("UNBOUND_SKIP_MANAGED_BUILD").is_some() {
        return;
    }
    let target = std::env::var_os("CARGO_TARGET_DIR")
        .map(PathBuf::from)
        .unwrap_or_else(|| root.join("target"));
    let dotnet = dotnet_executable();
    if build(&dotnet, &target, &managed, false) {
        return;
    }
    // Hand-written code that changed under its generated stubs (a signature now implemented by
    // hand) stops the build until `unbound gen-stubs` runs, which needs this build: without stubs
    // the tools still work, the game misses the stubbed members.
    if build(&dotnet, &target, &managed, true) {
        println!(
            "cargo:warning=the managed engine was built WITHOUT compatibility stubs because they no \
             longer fit the hand-written code; run `unbound gen-stubs -p <project>` and rebuild"
        );
        return;
    }
    panic!("building the managed engine failed");
}

fn build(dotnet: &PathBuf, target: &Path, managed: &Path, no_stubs: bool) -> bool {
    let mut command = Command::new(dotnet);
    command.args([
        "build",
        "-c",
        "Debug",
        "-v",
        "q",
        "-nologo",
        "-clp:ErrorsOnly",
        "-o",
    ]);
    command.arg(target.join("managed"));
    if no_stubs {
        command.arg("-p:UnboundNoStubs=true");
    }
    command
        .arg(managed.join("Unbound.Host/Unbound.Host.csproj"))
        .status()
        .unwrap_or_else(|e| panic!("running {}: {e}", dotnet.display()))
        .success()
}

fn dotnet_executable() -> PathBuf {
    if let Some(root) = std::env::var_os("DOTNET_ROOT") {
        return PathBuf::from(root).join("dotnet");
    }
    let home = PathBuf::from(std::env::var_os("HOME").unwrap_or_default());
    let user = home.join(".dotnet/dotnet");
    if user.exists() {
        return user;
    }
    PathBuf::from("dotnet")
}
