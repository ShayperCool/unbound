//! Builds the managed engine (`managed/`) into `target/managed` whenever its sources change, so
//! that `cargo build` alone produces a runnable tool.
use std::{path::PathBuf, process::Command};

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
    let status = Command::new(&dotnet)
        .args([
            "build",
            "-c",
            "Debug",
            "-v",
            "q",
            "-nologo",
            "-clp:ErrorsOnly",
            "-o",
        ])
        .arg(target.join("managed"))
        .arg(managed.join("Unbound.Host/Unbound.Host.csproj"))
        .status()
        .unwrap_or_else(|e| panic!("running {}: {e}", dotnet.display()));
    if !status.success() {
        panic!("building the managed engine failed");
    }
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
