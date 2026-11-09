#!/bin/sh
# Installs Unbound from the GitHub release archives.
#
#   curl -fsSL https://raw.githubusercontent.com/ShayperCool/unbound/main/install.sh | sh
#
# Environment:
#   UNBOUND_VERSION      release tag to install (e.g. v0.1.0); the latest release by default
#   UNBOUND_INSTALL_DIR  where to unpack (default ~/.unbound); replaced on reinstall
#   UNBOUND_BIN_DIR      where to link the `unbound` command (default ~/.local/bin)
#   UNBOUND_DOWNLOAD_URL where the archives are instead of the release (testing; file:// works)
set -eu

repo="ShayperCool/unbound"
install_dir="${UNBOUND_INSTALL_DIR:-$HOME/.unbound}"
bin_dir="${UNBOUND_BIN_DIR:-$HOME/.local/bin}"

say() { printf '%s\n' "$*"; }
fail() { printf 'unbound install: %s\n' "$*" >&2; exit 1; }

case "$(uname -s)-$(uname -m)" in
  Linux-x86_64 | Linux-amd64) platform="linux-x86_64" ;;
  Darwin-arm64 | Darwin-aarch64) platform="macos-arm64" ;;
  *) fail "no release for $(uname -s) $(uname -m); supported: Linux x86_64, macOS arm64" ;;
esac

if command -v curl >/dev/null 2>&1; then
  fetch() { curl -fsSL --retry 3 -o "$2" "$1"; }
elif command -v wget >/dev/null 2>&1; then
  fetch() { wget -q -O "$2" "$1"; }
else
  fail "curl or wget is required"
fi

if command -v sha256sum >/dev/null 2>&1; then
  sha256() { sha256sum "$1" | cut -d' ' -f1; }
else
  sha256() { shasum -a 256 "$1" | cut -d' ' -f1; }
fi

asset="unbound-$platform.tar.gz"
if [ -n "${UNBOUND_DOWNLOAD_URL:-}" ]; then
  base="$UNBOUND_DOWNLOAD_URL"
elif [ -n "${UNBOUND_VERSION:-}" ]; then
  base="https://github.com/$repo/releases/download/$UNBOUND_VERSION"
else
  base="https://github.com/$repo/releases/latest/download"
fi

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT INT TERM

say "Downloading $asset (${UNBOUND_VERSION:-latest release})..."
fetch "$base/$asset" "$tmp/$asset" || fail "could not download $base/$asset"
fetch "$base/$asset.sha256" "$tmp/$asset.sha256" || fail "could not download $base/$asset.sha256"
expected="$(cut -d' ' -f1 "$tmp/$asset.sha256")"
actual="$(sha256 "$tmp/$asset")"
[ "$expected" = "$actual" ] || fail "checksum mismatch for $asset (expected $expected, got $actual)"

tar -xzf "$tmp/$asset" -C "$tmp"
[ -x "$tmp/unbound/bin/unbound" ] || fail "the archive does not contain unbound/bin/unbound"
if [ "$platform" = "macos-arm64" ]; then
  # Archives fetched by a browser are quarantined; curl's are not, but be safe.
  xattr -dr com.apple.quarantine "$tmp/unbound" 2>/dev/null || true
fi

# Only ever replace an earlier installation, never a directory that holds anything else.
if [ -e "$install_dir" ]; then
  if [ -f "$install_dir/VERSION" ] && [ -x "$install_dir/bin/unbound" ]; then
    rm -rf "$install_dir"
  elif [ -d "$install_dir" ] && [ -z "$(ls -A "$install_dir")" ]; then
    rmdir "$install_dir"
  else
    fail "$install_dir exists and is not an Unbound installation; remove it or set UNBOUND_INSTALL_DIR"
  fi
fi
mkdir -p "$(dirname "$install_dir")"
mv "$tmp/unbound" "$install_dir"
mkdir -p "$bin_dir"
ln -sf "$install_dir/bin/unbound" "$bin_dir/unbound"
say "Installed $("$install_dir/bin/unbound" --version) to $install_dir"
say "Linked $bin_dir/unbound"

case ":$PATH:" in
  *":$bin_dir:"*) ;;
  *) say ""
     say "Add $bin_dir to your PATH, e.g.:"
     say "  echo 'export PATH=\"$bin_dir:\$PATH\"' >> ~/.profile" ;;
esac

# Unbound hosts .NET 10 (runtime or SDK). It looks in DOTNET_ROOT, ~/.dotnet and the system
# locations below.
has_dotnet10() {
  for root in "${DOTNET_ROOT:-}" "$HOME/.dotnet" /usr/share/dotnet /usr/lib/dotnet /usr/local/share/dotnet; do
    [ -n "$root" ] && [ -d "$root/shared/Microsoft.NETCore.App" ] || continue
    for version in "$root"/shared/Microsoft.NETCore.App/10.*; do
      [ -d "$version" ] && return 0
    done
  done
  return 1
}
if ! has_dotnet10; then
  say ""
  say "Unbound needs the .NET 10 runtime, which was not found. Install it with:"
  say "  curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0 --runtime dotnet"
  say "(into ~/.dotnet), or from your package manager / https://dotnet.microsoft.com/download/dotnet/10.0"
fi
