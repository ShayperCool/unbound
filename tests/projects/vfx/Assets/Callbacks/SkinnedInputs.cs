using System.Reflection;
using UnityEngine;
using UnityEngine.VFX;

/// <summary>
/// A spawner block written as a script, for the probe's skinned mesh controls: its inputs are fed
/// by operators over the exposed skinned mesh renderer (the root bone's transform relative to the
/// renderer and in world space, of this frame and of the frame before), and it writes down what
/// they hold as <see cref="OpsInputs"/> does.
/// </summary>
public class SkinnedInputs : VFXSpawnerCallbacks
{
    public class InputProperties
    {
        /// <summary>The root bone's transform: local and world, this frame and the frame before.</summary>
        public Matrix4x4 m00, m01, m02, m03;
    }

    static readonly FieldInfo[] s_Inputs = typeof(InputProperties).GetFields();

    public override void OnPlay(VFXSpawnerState state, VFXExpressionValues vfxValues, VisualEffect vfxComponent) => OpsInputs.Played(vfxValues, vfxComponent, s_Inputs);

    public override void OnUpdate(VFXSpawnerState state, VFXExpressionValues vfxValues, VisualEffect vfxComponent) => OpsInputs.Updated(vfxValues, vfxComponent, s_Inputs);

    public override void OnStop(VFXSpawnerState state, VFXExpressionValues vfxValues, VisualEffect vfxComponent) { }
}
