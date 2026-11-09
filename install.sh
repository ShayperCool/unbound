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
case "$install_dir" in /*) ;; *) install_dir="$PWD/$install_dir" ;; esac
case "$bin_dir" in /*) ;; *) bin_dir="$PWD/$bin_dir" ;; esac

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
trap 'rm -rf "$tmp"' EXIT
trap 'exit 130' INT TERM

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

# An earlier installation: the unpacked archive's layout. Only such a directory is ever replaced.
is_installation() { [ -f "$1/VERSION" ] && [ -x "$1/bin/unbound" ]; }
if [ -e "$install_dir" ] && ! is_installation "$install_dir" \
    && ! { [ -d "$install_dir" ] && [ -z "$(ls -A "$install_dir")" ]; }; then
  fail "$install_dir exists and is not an Unbound installation; remove it or set UNBOUND_INSTALL_DIR"
fi
if [ -e "$bin_dir/unbound" ] && [ ! -L "$bin_dir/unbound" ]; then
  fail "$bin_dir/unbound exists and is not a link to an installation; remove it or set UNBOUND_BIN_DIR"
fi

# Put the new tree beside the old one, then swap, so an interruption leaves one of them in place.
mkdir -p "$(dirname "$install_dir")"
for side in "$install_dir.new" "$install_dir.old"; do
  if [ -e "$side" ]; then
    is_installation "$side" || fail "$side is in the way; remove it"
    rm -rf "$side"
  fi
done
mv "$tmp/unbound" "$install_dir.new"
if [ -e "$install_dir" ]; then
  mv "$install_dir" "$install_dir.old"
fi
mv "$install_dir.new" "$install_dir"
rm -rf "$install_dir.old"
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

# Unbound hosts .NET 10 (runtime or SDK) from the first of these locations that has a .NET host,
# as crates/unbound-host does.
dotnet_root() {
  for root in "${DOTNET_ROOT:-}" "$HOME/.dotnet" /usr/share/dotnet /usr/lib/dotnet /usr/local/share/dotnet; do
    if [ -n "$root" ] && [ -d "$root/host/fxr" ]; then
      printf '%s\n' "$root"
      return 0
    fi
  done
  return 1
}
dotnet_install="curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0 --runtime dotnet"
if ! root="$(dotnet_root)"; then
  say ""
  say "Unbound needs the .NET 10 runtime, which was not found. Install it into ~/.dotnet with:"
  say "  $dotnet_install"
  say "or from your package manager / https://dotnet.microsoft.com/download/dotnet/10.0"
elif ! ls -d "$root"/shared/Microsoft.NETCore.App/10.* >/dev/null 2>&1; then
  say ""
  say "Unbound uses the .NET installation in $root, which has no .NET 10 runtime."
  say "Add .NET 10 there, or install it into ~/.dotnet and set DOTNET_ROOT=~/.dotnet:"
  say "  $dotnet_install"
fi
