using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.U2D;

/// <summary>A geometry modifier that changes nothing: it keeps the buffers the controller's
/// generator wrote into (the renderer's channels), so the probe can read the generated geometry
/// once the job completes.</summary>
public class GeometryRecorder : SpriteShapeGeometryModifier
{
    public NativeArray<ushort> indices;
    public NativeSlice<Vector3> positions;
    public NativeSlice<Vector2> texCoords;
    public NativeArray<SpriteShapeSegment> segments;

    public override JobHandle MakeModifierJob(JobHandle generator, SpriteShapeController controller, NativeArray<ushort> indices,
        NativeSlice<Vector3> positions, NativeSlice<Vector2> texCoords, NativeSlice<Vector4> tangents,
        NativeArray<SpriteShapeSegment> segments, NativeArray<float2> colliderData)
    {
        this.indices = indices;
        this.positions = positions;
        this.texCoords = texCoords;
        this.segments = segments;
        return generator;
    }
}
