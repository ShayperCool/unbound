export PATH="$(source ~/.cache/compat/env.sh; printf '%s' "$PATH")"
unset WT U
export LIFECYCLE_WORK=/mnt/storage-hdd/node-b-caches-20261023/trigger2dlifecycle-bd5ab077
export CARGO_TARGET_DIR=$LIFECYCLE_WORK/cargo
export UNBOUND_MANAGED_DIR=$LIFECYCLE_WORK/managed-final
export UnboundTarget=$LIFECYCLE_WORK/dotnet/
export UnboundNativeDir=$LIFECYCLE_WORK/managed-final
export LD_LIBRARY_PATH=$UNBOUND_MANAGED_DIR
export SCENE_SETTINGS_CLI=$HOME/.cache/compat/parity-baseline-inactive3d-native8/unbound
export SCENE_WORK=$LIFECYCLE_WORK/scene
export SCENE_PARENT_WORK=$LIFECYCLE_WORK/scene-parent
export TRIGGER_WORK=$LIFECYCLE_WORK
export UseSharedCompilation=false MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0
export XDG_CACHE_HOME=$LIFECYCLE_WORK/cache
export TMPDIR=$LIFECYCLE_WORK/tmp
mkdir -p "$SCENE_WORK" "$SCENE_PARENT_WORK" "$XDG_CACHE_HOME" "$TMPDIR"

export DISPLAY=${DISPLAY:-:0}
export XAUTHORITY=$(ls -t /run/user/1000/.mutter-Xwaylandauth.* 2>/dev/null | head -1)
