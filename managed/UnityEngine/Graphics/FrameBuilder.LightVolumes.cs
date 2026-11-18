using UnityEngine;
using UnityEngine.Rendering;

namespace Unbound.Runtime
{
    // Where the built-in Deferred Shading path draws a point or spot light (matches the Unity
    // 6000.0.71f1 Editor, tests/projects/builtinfx deferred_volume_* and settings_deferred_custom):
    // over the light's volume, a built-in mesh (point lights: Unity's icosahedron, its icosphere when
    // the light's box covers more than a quarter of the view's height; spot lights: Unity's pyramid
    // over the cone), unless the volume crosses both the near and the far plane, when it draws over
    // the view as directional lights do. A volume clear of both planes first marks, in stencil bit
    // 4, the pixels whose scene point lies inside it (each face that fails the depth test inverts
    // the bit), then draws its front faces over the marked pixels and clears the bit again; one
    // crossing the near plane draws its back faces where they are behind the scene; one crossing the
    // far plane only draws its front faces in front of the scene.
    internal static partial class FrameBuilder
    {
        /// <summary>Margins of the plane tests: the near plane moved out by 0.1%, the far plane in by
        /// 0.1%, and a point light's sphere grown to the icosahedron around it (its vertices are
        /// 1.27 times the radius out).</summary>
        const float VolumeNearMargin = 1.001f, VolumeFarMargin = 0.999f, PointVolumeReach = 1.27f;

        /// <summary>The share of the view's height above which a point light's box takes the finer
        /// icosphere.</summary>
        const float FinePointVolumeHeight = 0.25f;

        /// <summary>Stencil bits the light passes use: the G-buffer's mark (bit 7, bit 6 for lights
        /// whose light is in the lightmaps of a Subtractive bake) and the volume mark (bit 4).</summary>
        const byte LightMarkBit = 0x80, NonLightmappedBit = 0x40, VolumeMarkBit = 0x10;

        static Mesh s_Icosahedron, s_Icosphere, s_Pyramid;

        enum VolumeKind { View, Marked, Back, Front }

        /// <summary>The volume a light draws over (<see cref="VolumeKind.View"/>: the whole view, as
        /// a directional light), with its mesh and matrix.</summary>
        static VolumeKind LightVolume(Light light, Camera camera, in CameraContext context, out Mesh mesh, out Matrix4x4 model)
        {
            mesh = null;
            model = Matrix4x4.identity;
            if (light.type is not (LightType.Point or LightType.Spot)) return VolumeKind.View;
            var t = light.transform;
            var position = t.position;
            float range = light.range;
            var view = context.View;
            // Depths along the view direction (camera space looks down -z).
            float nearest, farthest;
            if (light.type == LightType.Point)
            {
                float depth = -view.MultiplyPoint3x4(position).z;
                nearest = depth - PointVolumeReach * range;
                farthest = depth + PointVolumeReach * range;
            }
            else
            {
                float half = range * Mathf.Tan(light.spotAngle * 0.5f * Mathf.Deg2Rad);
                var rotation = t.rotation;
                nearest = farthest = -view.MultiplyPoint3x4(position).z;
                for (int corner = 0; corner < 4; corner++)
                {
                    var local = new Vector3((corner & 1) != 0 ? half : -half, (corner & 2) != 0 ? half : -half, range);
                    float depth = -view.MultiplyPoint3x4(position + rotation * local).z;
                    nearest = Mathf.Min(nearest, depth);
                    farthest = Mathf.Max(farthest, depth);
                }
            }
            bool crossesNear = nearest <= camera.nearClipPlane * VolumeNearMargin;
            bool crossesFar = camera.farClipPlane * VolumeFarMargin <= farthest;
            if (crossesNear && crossesFar) return VolumeKind.View;
            if (light.type == LightType.Point)
            {
                bool fine = PointBoxHeight(position, range, context.Projection * view) > FinePointVolumeHeight;
                mesh = fine ? BuiltinVolume(ref s_Icosphere, "icosphere.fbx") : BuiltinVolume(ref s_Icosahedron, "icosahedron.fbx");
                model = Matrix4x4.TRS(position, Quaternion.identity, new Vector3(range, range, range));
            }
            else
            {
                float half = range * Mathf.Tan(light.spotAngle * 0.5f * Mathf.Deg2Rad);
                mesh = BuiltinVolume(ref s_Pyramid, "pyramid.fbx");
                model = Matrix4x4.TRS(position, t.rotation, new Vector3(half, half, range));
            }
            if (mesh == null) return VolumeKind.View;
            if (crossesNear) return VolumeKind.Back;
            if (crossesFar || LightmapModeForRender(light) != 0) return VolumeKind.Front;
            return VolumeKind.Marked;
        }

        static Mesh BuiltinVolume(ref Mesh mesh, string name)
        {
            if (mesh == null)
            {
                mesh = BuiltinResources.Get(typeof(Mesh), name) as Mesh;
                if (mesh == null)
                    Debug.LogWarning($"Unbound: the built-in light volume mesh {name} is missing; deferred lights that need it draw over the whole view");
            }
            return mesh;
        }

        /// <summary>A light's mode for the light passes: 0 for a realtime light, 1 for a mixed one
        /// of a Subtractive bake, 2 for a baked one (as the lightmaps say).</summary>
        static int LightmapModeForRender(Light light)
        {
            var baking = light.bakingOutput;
            if (!baking.isBaked) return 0;
            return baking.lightmapBakeType switch
            {
                LightmapBakeType.Baked => 2,
                LightmapBakeType.Mixed => baking.mixedLightingMode == MixedLightingMode.Subtractive ? 1 : 0,
                _ => 0,
            };
        }

        static readonly Vector4[] s_BoxCorners = new Vector4[8];
        static readonly Vector4[] s_FaceClip = new Vector4[8];
        static readonly int[] s_BoxFaces = { 0, 1, 3, 2, 4, 6, 7, 5, 0, 4, 5, 1, 2, 3, 7, 6, 0, 2, 6, 4, 1, 5, 7, 3 };

        /// <summary>The share of the view's height a point light's box (its position plus and minus
        /// its range on each axis) covers: each face clipped by the near plane in clip space, the
        /// bounds of what is left after the perspective divide, clamped to the view (none when they
        /// miss it).</summary>
        static float PointBoxHeight(Vector3 center, float range, Matrix4x4 viewProjection)
        {
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) != 0 ? range : -range, (i & 2) != 0 ? range : -range, (i & 4) != 0 ? range : -range);
                var p = center + corner;
                s_BoxCorners[i] = viewProjection * new Vector4(p.x, p.y, p.z, 1f);
            }
            var min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            bool any = false;
            for (int face = 0; face < 6; face++)
            {
                int count = 0;
                for (int k = 0; k < 4; k++)
                {
                    var a = s_BoxCorners[s_BoxFaces[face * 4 + k]];
                    var b = s_BoxCorners[s_BoxFaces[face * 4 + (k + 1) % 4]];
                    float da = a.z + a.w, db = b.z + b.w;
                    if (da >= 0f) s_FaceClip[count++] = a;
                    if (da * db < 0f) s_FaceClip[count++] = a + (b - a) * (da / (da - db));
                }
                for (int k = 0; k < count; k++)
                {
                    var c = s_FaceClip[k];
                    var ndc = new Vector3(c.x, c.y, c.z) / c.w;
                    min = Vector3.Min(min, ndc);
                    max = Vector3.Max(max, ndc);
                    any = true;
                }
            }
            if (!any || min.x > 1f || min.y > 1f || min.z > 1f || max.x < -1f || max.y < -1f || max.z < -1f) return 0f;
            return (Mathf.Min(max.y, 1f) - Mathf.Max(min.y, -1f)) * 0.5f;
        }

        /// <summary>The volume mark: no colour, both faces, the depth test without writes; a face
        /// behind the scene inverts bit 4 (the G-buffer's marks as reference).</summary>
        static readonly RenderStateBlock s_VolumeMark = new RenderStateBlock(RenderStateMask.Blend | RenderStateMask.Raster | RenderStateMask.Depth | RenderStateMask.Stencil)
        {
            blendState = new BlendState { blendState0 = new RenderTargetBlendState((ColorWriteMask)0) },
            rasterState = new RasterState(CullMode.Off),
            depthState = new DepthState(false, CompareFunction.LessEqual),
            stencilReference = 0xC0,
            stencilState = new StencilState(true, 0xFF, VolumeMarkBit, CompareFunction.Always, StencilOp.Keep, StencilOp.Keep, StencilOp.Invert),
        };

        /// <summary>The light over the marked pixels: front faces in front of the scene where both
        /// the G-buffer's and the volume's marks are set, clearing the volume's mark.</summary>
        static readonly RenderStateBlock s_VolumeMarked = new RenderStateBlock(RenderStateMask.Raster | RenderStateMask.Depth | RenderStateMask.Stencil)
        {
            rasterState = new RasterState(CullMode.Back),
            depthState = new DepthState(false, CompareFunction.LessEqual),
            stencilReference = LightMarkBit | VolumeMarkBit,
            stencilState = new StencilState(true, LightMarkBit | VolumeMarkBit, VolumeMarkBit, CompareFunction.Equal, StencilOp.Zero, StencilOp.Zero, StencilOp.Zero),
        };

        static RenderStateBlock VolumeState(VolumeKind kind, int lightmapMode)
        {
            byte bits = (byte)(LightMarkBit | (lightmapMode == 1 ? NonLightmappedBit : 0));
            return new RenderStateBlock(RenderStateMask.Raster | RenderStateMask.Depth | RenderStateMask.Stencil)
            {
                rasterState = new RasterState(kind == VolumeKind.Back ? CullMode.Front : CullMode.Back),
                depthState = new DepthState(false, kind == VolumeKind.Back ? CompareFunction.Greater : CompareFunction.LessEqual),
                stencilReference = bits,
                stencilState = new StencilState(true, bits, 0xFF, CompareFunction.Equal, StencilOp.Keep, StencilOp.Keep, StencilOp.Keep),
            };
        }
    }
}
