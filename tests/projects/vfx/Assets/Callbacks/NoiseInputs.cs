using System.Reflection;
using UnityEngine;
using UnityEngine.VFX;

/// <summary>
/// A spawner block written as a script, for the probe's noise controls: its inputs are fed by
/// the noise operators over exposed properties (value, Perlin and cellular noise over one, two
/// and three coordinates with their derivatives and the curl of each over two and three: the
/// Voronoi noise is not valid in a spawn context), and it writes down what they hold as <see cref="OpsInputs"/> does.
/// </summary>
public class NoiseInputs : VFXSpawnerCallbacks
{
    public class InputProperties
    {
        /// <summary>Per noise (0 value, 1 Perlin, 2 cellular, the tens digit): the noise over
        /// one coordinate and its derivative, the noise over two and over three coordinates.</summary>
        public float f00, f01, f02, f03, f10, f11, f12, f13, f20, f21, f22, f23;
        /// <summary>Per noise: the derivatives over two coordinates and the curl over two.</summary>
        public Vector2 w00, w01, w10, w11, w20, w21;
        /// <summary>Per noise: the derivatives over three coordinates and the curl over three.</summary>
        public Vector3 v00, v01, v10, v11, v20, v21;
    }

    static readonly FieldInfo[] s_Inputs = typeof(InputProperties).GetFields();

    public override void OnPlay(VFXSpawnerState state, VFXExpressionValues vfxValues, VisualEffect vfxComponent) => OpsInputs.Played(vfxValues, vfxComponent, s_Inputs);

    public override void OnUpdate(VFXSpawnerState state, VFXExpressionValues vfxValues, VisualEffect vfxComponent) => OpsInputs.Updated(vfxValues, vfxComponent, s_Inputs);

    public override void OnStop(VFXSpawnerState state, VFXExpressionValues vfxValues, VisualEffect vfxComponent) { }
}
