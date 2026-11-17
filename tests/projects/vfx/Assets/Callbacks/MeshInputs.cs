using System.Reflection;
using UnityEngine;
using UnityEngine.VFX;

/// <summary>
/// A spawner block written as a script, for the probe's mesh and texture operation controls:
/// its inputs are fed by operators over exposed properties (a mesh's vertex, index and triangle
/// counts, an index of it, its vertices sampled with each addressing mode and on a triangle, and
/// the depth or count of a volume, an array of textures and an array of cubemaps), and it writes
/// down what they hold as <see cref="OpsInputs"/> does.
/// </summary>
public class MeshInputs : VFXSpawnerCallbacks
{
    public class InputProperties
    {
        /// <summary>The mesh's vertex, index and triangle counts and an index of it; a volume's
        /// width, height and depth; the counts of an array of textures and of cubemaps.</summary>
        public uint u00, u01, u02, u03, u04, u05, u06, u07, u08;
        /// <summary>A vertex's position, normal and tangent (addresses clamped); the position of
        /// a vertex whose address wraps and of one whose address is mirrored; a point on a
        /// triangle.</summary>
        public Vector3 v00, v01, v02, v03, v04, v05;
        /// <summary>A vertex's colour and first two texture coordinates; a point's colour on a
        /// triangle.</summary>
        public Vector4 q00, q01, q02, q03;
    }

    static readonly FieldInfo[] s_Inputs = typeof(InputProperties).GetFields();

    public override void OnPlay(VFXSpawnerState state, VFXExpressionValues vfxValues, VisualEffect vfxComponent) => OpsInputs.Played(vfxValues, vfxComponent, s_Inputs);

    public override void OnUpdate(VFXSpawnerState state, VFXExpressionValues vfxValues, VisualEffect vfxComponent) => OpsInputs.Updated(vfxValues, vfxComponent, s_Inputs);

    public override void OnStop(VFXSpawnerState state, VFXExpressionValues vfxValues, VisualEffect vfxComponent) { }
}
