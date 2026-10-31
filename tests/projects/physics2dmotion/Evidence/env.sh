export PATH="$(source ~/.cache/compat/env.sh; printf '%s' "$PATH")"
unset WT U
export MOTION_WORK=/mnt/storage-hdd/node-b-caches-20261023/physics2dmotion-t3-319e74d7
export CARGO_TARGET_DIR=$MOTION_WORK/cargo
export UNBOUND_MANAGED_DIR=$MOTION_WORK/managed-final
export UnboundTarget=$MOTION_WORK/dotnet/
export UnboundNativeDir=$MOTION_WORK/managed-final
export LD_LIBRARY_PATH=$UNBOUND_MANAGED_DIR
export SCENE_SETTINGS_CLI=$HOME/.cache/compat/parity-baseline-lifecycle2d-native8/unbound
export SCENE_WORK=$MOTION_WORK/scene
export SCENE_PARENT_WORK=$MOTION_WORK/scene-parent
export TRIGGER_WORK=$MOTION_WORK
export LIFECYCLE_WORK=$MOTION_WORK
export UseSharedCompilation=false MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0
export XDG_CACHE_HOME=$MOTION_WORK/cache
export TMPDIR=$MOTION_WORK/tmp
mkdir -p "$SCENE_WORK" "$SCENE_PARENT_WORK" "$XDG_CACHE_HOME" "$TMPDIR"
export DISPLAY=${DISPLAY:-:0}
export XAUTHORITY=$(ls -t /run/user/1000/.mutter-Xwaylandauth.* 2>/dev/null | head -1)
