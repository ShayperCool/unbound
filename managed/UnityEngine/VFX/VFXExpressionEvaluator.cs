using System;
using UnityEditor.VFX;
using UnityEngine;
using UnityEngine.VFX;

namespace Unbound.Runtime
{
    /// <summary>What an effect's expressions read of the effect while one step of it is evaluated.</summary>
    internal sealed class VisualEffectState
    {
        internal Matrix4x4 LocalToWorld = Matrix4x4.identity, WorldToLocal = Matrix4x4.identity;
        /// <summary>The step's time: with the play rate, and before it.</summary>
        internal float DeltaTime, UnscaledDeltaTime;
        internal float TotalTime, PlayRate = 1f;
        /// <summary>The manager's update count, and the effect's seed.</summary>
        internal uint FrameIndex, Seed;
        /// <summary>The effect's random sequence (spawner blocks and random expressions draw from it).</summary>
        internal UnityEngine.Random.State Random;
        /// <summary>Whether a step ran since the expressions were last evaluated.</summary>
        internal bool NeedsEvaluation = true;
        /// <summary>Whether the step is the last of its frame (of the steps simulated at once).</summary>
        internal bool LastStep = true;
        /// <summary>Where the effect's curves and gradients are baked for its programs; null
        /// for a graph that bakes none.</summary>
        internal VFXBakedTexture Baked;
    }

    /// <summary>
    /// Evaluates a compiled graph's CPU expressions into an effect's values: each expression of a
    /// range in order, from its parents' values (constants keep theirs). What the probe's graphs
    /// evaluate matches the Unity 6000.0.71f1 Editor (tests/projects/vfx, `sim/expressions*`:
    /// a spawn block's inputs fed by operators over exposed properties). The operations
    /// without a control, or not written yet, are listed by <see cref="Supported"/> and stop an
    /// effect that uses them from running.
    /// </summary>
    internal static class VFXExpressionEvaluator
    {
        const int Int32Type = (int)VFXValueType.Int32, Uint32Type = (int)VFXValueType.Uint32;

        /// <summary>What a graph reads of the main camera while there is none: a new camera's
        /// lens (Unity 6000.0.71f1, tests/projects/vfx <c>camera/values</c> from frame 8).</summary>
        const float NoCameraFieldOfView = 60f, NoCameraNearPlane = 0.3f, NoCameraFarPlane = 1000f, NoCameraOrthographicSize = 5f;

        /// <summary>The faces of a cubemap: an array of cubemaps' depth counts them.</summary>
        const uint CubeFaces = 6;

        /// <summary>Whether <see cref="Evaluate"/> computes the operation.</summary>
        internal static bool Supported(VFXExpressionOperation op)
        {
            switch (op)
            {
                // No operator of the package makes a texture's format.
                case VFXExpressionOperation.TextureFormat:
                // The graph compiler refuses the Voronoi noise outside a kernel ("not valid on
                // the CPU", tests/projects/vfx README, "Noise"): no compiled graph evaluates it.
                case VFXExpressionOperation.VoroNoise2D:
                // A skinned mesh renderer's mesh, vertices and root bone are not done yet.
                case VFXExpressionOperation.VertexBufferFromSkinnedMeshRenderer:
                case VFXExpressionOperation.MeshFromSkinnedMeshRenderer:
                case VFXExpressionOperation.RootBoneTransformFromSkinnedMeshRenderer:
                    return false;
                case VFXExpressionOperation.ReadEventAttribute:
                case VFXExpressionOperation.MeshVertexCount:
                case VFXExpressionOperation.MeshChannelOffset:
                case VFXExpressionOperation.MeshChannelInfos:
                case VFXExpressionOperation.MeshVertexStride:
                case VFXExpressionOperation.MeshIndexCount:
                case VFXExpressionOperation.MeshIndexFormat:
                case VFXExpressionOperation.BufferStride:
                case VFXExpressionOperation.BufferCount:
                case VFXExpressionOperation.TextureWidth:
                case VFXExpressionOperation.TextureHeight:
                case VFXExpressionOperation.TextureDepth:
                    return true;
            }
            return op >= VFXExpressionOperation.None && op <= VFXExpressionOperation.SpawnerStateLoopCount;
        }

        /// <summary>A texture's depth as a graph reads it: a volume's depth, an array's count of
        /// slices, an array of cubemaps' count of faces, a render texture's volume depth and zero
        /// for none or one destroyed (tests/projects/vfx <c>mesh operations/*/u06..u08</c>); one
        /// for another texture (not measured: the package reads the depth of volumes and arrays
        /// only).</summary>
        static uint TextureDepth(Texture texture) => texture == null ? 0u : texture switch
        {
            Texture3D volume => (uint)volume.depth,
            Texture2DArray slices => (uint)slices.depth,
            CubemapArray cubes => (uint)cubes.cubemapCount * CubeFaces,
            RenderTexture target => (uint)target.volumeDepth,
            _ => 1u,
        };

        /// <summary>Whether an event attribute of the type can be read in a spawn context (the
        /// types attributes have).</summary>
        internal static bool ReadableEventAttribute(VFXValueType type) =>
            type is >= VFXValueType.Float and <= VFXValueType.Uint32 or VFXValueType.Boolean;

        /// <summary>Whether a read of an event attribute of the type at the element lies within
        /// attributes of that many words.</summary>
        internal static bool EventAttributeWithin(VFXValueType type, int element, int words) =>
            element >= 0 && element + VFXExpressionHelper.GetSizeOfType(type) <= words;

        /// <summary>Evaluates the expressions <paramref name="begin"/>..<paramref name="end"/>.
        /// <paramref name="spawner"/> is the spawner whose blocks are being evaluated, null
        /// outside one (the spawner's own operations then leave their value as it is).</summary>
        internal static void Evaluate(VisualEffectCompiledData data, VisualEffectValues values, int begin, int end, VisualEffectState state, VFXSpawnerSystem spawner)
        {
            var w = values.Words;
            var ops = data.Operations;
            var operands = data.Operands;
            var offsets = data.ValueOffsets;
            end = Math.Min(end, ops.Length);
            // A mesh vertex's channel as it is sampled.
            Span<float> vertex = stackalloc float[4];
            for (int e = begin; e < end; e++)
            {
                var op = ops[e];
                if (op <= VFXExpressionOperation.Value) continue;
                int at = offsets[e], d = e * 4;
                // The first two operands are parents for the operations below that read them
                // (another operation's operand there is a number, not an expression).
                int a0 = operands[d], a1 = operands[d + 1];
                int p0 = (uint)a0 < (uint)offsets.Length ? offsets[a0] : -1;
                int p1 = (uint)a1 < (uint)offsets.Length ? offsets[a1] : -1;
                switch (op)
                {
                    case VFXExpressionOperation.Combine2f:
                        (w[at], w[at + 1]) = (w[p0], w[p1]);
                        break;
                    case VFXExpressionOperation.Combine3f:
                        (w[at], w[at + 1], w[at + 2]) = (w[p0], w[p1], w[offsets[operands[d + 2]]]);
                        break;
                    case VFXExpressionOperation.Combine4f:
                        (w[at], w[at + 1], w[at + 2], w[at + 3]) = (w[p0], w[p1], w[offsets[operands[d + 2]]], w[offsets[operands[d + 3]]]);
                        break;
                    case VFXExpressionOperation.ExtractComponent:
                        w[at] = w[p0 + operands[d + 2]];
                        break;

                    case >= VFXExpressionOperation.ValueNoise1D and <= VFXExpressionOperation.CellularCurlNoise3D:
                        // A coordinate, (frequency, persistence, lacunarity) and a count of octaves.
                        VFXNoise.Evaluate(op, w, at, p0, p1, (int)w[offsets[operands[d + 2]]]);
                        break;

                    case VFXExpressionOperation.DeltaTime: w[at] = Bits(state.DeltaTime); break;
                    case VFXExpressionOperation.UnscaledDeltaTime: w[at] = Bits(state.UnscaledDeltaTime); break;
                    case VFXExpressionOperation.TotalTime: w[at] = Bits(state.TotalTime); break;
                    case VFXExpressionOperation.PlayRate: w[at] = Bits(state.PlayRate); break;
                    case VFXExpressionOperation.SystemSeed: w[at] = state.Seed; break;
                    case VFXExpressionOperation.FrameIndex: w[at] = state.FrameIndex; break;
                    case VFXExpressionOperation.LocalToWorld: Store(w, at, state.LocalToWorld); break;
                    case VFXExpressionOperation.WorldToLocal: Store(w, at, state.WorldToLocal); break;
                    case VFXExpressionOperation.ManagerMaxDeltaTime: w[at] = Bits(VFXManager.maxDeltaTime); break;
                    case VFXExpressionOperation.ManagerFixedTimeStep: w[at] = Bits(VFXManager.fixedTimeStep); break;
                    case VFXExpressionOperation.GameDeltaTime: w[at] = Bits(Time.deltaTime); break;
                    case VFXExpressionOperation.GameUnscaledDeltaTime: w[at] = Bits(Time.unscaledDeltaTime); break;
                    case VFXExpressionOperation.GameSmoothDeltaTime: w[at] = Bits(Time.smoothDeltaTime); break;
                    case VFXExpressionOperation.GameTotalTime: w[at] = Bits(Time.time); break;
                    case VFXExpressionOperation.GameUnscaledTotalTime: w[at] = Bits(Time.unscaledTime); break;
                    case VFXExpressionOperation.GameTotalTimeSinceSceneLoad: w[at] = Bits(Time.timeSinceLevelLoad); break;
                    case VFXExpressionOperation.GameTimeScale: w[at] = Bits(Time.timeScale); break;

                    case VFXExpressionOperation.Sin:
                    case VFXExpressionOperation.Cos:
                    case VFXExpressionOperation.Tan:
                    case VFXExpressionOperation.ASin:
                    case VFXExpressionOperation.ACos:
                    case VFXExpressionOperation.ATan:
                    case VFXExpressionOperation.Abs:
                    case VFXExpressionOperation.Sign:
                    case VFXExpressionOperation.Saturate:
                    case VFXExpressionOperation.Ceil:
                    case VFXExpressionOperation.Round:
                    case VFXExpressionOperation.Frac:
                    case VFXExpressionOperation.Floor:
                    case VFXExpressionOperation.Log2:
                    {
                        // Per component, by the value's type: an unsigned value has no unary
                        // operation (its value stays), a signed one only Abs and Sign.
                        int type = operands[d + 3];
                        if (type == Uint32Type) break;
                        int size = VFXExpressionHelper.GetSizeOfType((VFXValueType)type);
                        if (type == Int32Type)
                        {
                            if (op != VFXExpressionOperation.Abs && op != VFXExpressionOperation.Sign) break;
                            for (int i = 0; i < size; i++)
                            {
                                int v = (int)w[p0 + i];
                                w[at + i] = unchecked((uint)(op == VFXExpressionOperation.Abs ? (v < 0 ? -v : v) : (v > 0 ? 1 : 0) - (v < 0 ? 1 : 0)));
                            }
                            break;
                        }
                        for (int i = 0; i < size; i++) w[at + i] = Bits(Unary(op, Float(w[p0 + i])));
                        break;
                    }

                    case VFXExpressionOperation.Mul:
                    case VFXExpressionOperation.Divide:
                    case VFXExpressionOperation.Add:
                    case VFXExpressionOperation.Subtract:
                    case VFXExpressionOperation.Min:
                    case VFXExpressionOperation.Max:
                    case VFXExpressionOperation.Pow:
                    case VFXExpressionOperation.ATan2:
                    {
                        int type = operands[d + 3];
                        int size = VFXExpressionHelper.GetSizeOfType((VFXValueType)type);
                        if (type == Int32Type || type == Uint32Type)
                        {
                            // Integers have no Pow or ATan2: their value stays.
                            if (op > VFXExpressionOperation.Max) break;
                            for (int i = 0; i < size; i++)
                                w[at + i] = type == Int32Type ? unchecked((uint)Binary(op, (int)w[p0 + i], (int)w[p1 + i])) : Binary(op, w[p0 + i], w[p1 + i]);
                            break;
                        }
                        for (int i = 0; i < size; i++) w[at + i] = Bits(Binary(op, Float(w[p0 + i]), Float(w[p1 + i])));
                        break;
                    }

                    case VFXExpressionOperation.SampleCurve:
                        // A curve at a time; no curve gives zero.
                        w[at] = Bits(values.GetObject(a0) is AnimationCurve sampled ? sampled.EvaluateCubic(Float(w[p1])) : 0f);
                        break;
                    case VFXExpressionOperation.SampleGradient:
                    {
                        var color = values.GetObject(a0) is Gradient ramp ? ramp.Evaluate(Float(w[p1])) : default;
                        (w[at], w[at + 1], w[at + 2], w[at + 3]) = (Bits(color.r), Bits(color.g), Bits(color.b), Bits(color.a));
                        break;
                    }
                    // A curve or gradient at its place in the effect's texture, as what the
                    // generated code samples it there with.
                    case VFXExpressionOperation.BakeCurve:
                        state.Baked?.BakeCurve(data.BakePlaces[e], values.GetObject(a0) as AnimationCurve, w, at);
                        break;
                    case VFXExpressionOperation.BakeGradient:
                        state.Baked?.BakeGradient(data.BakePlaces[e], values.GetObject(a0) as Gradient, w, at);
                        break;

                    case VFXExpressionOperation.TRSToMatrix:
                    {
                        // A position, angles in degrees and a scale, as Matrix4x4.TRS takes them.
                        int scale = offsets[operands[d + 2]];
                        var angles = new Vector3(Float(w[p1]), Float(w[p1 + 1]), Float(w[p1 + 2]));
                        Store(w, at, Matrix4x4.TRS(new Vector3(Float(w[p0]), Float(w[p0 + 1]), Float(w[p0 + 2])), Quaternion.Euler(angles),
                            new Vector3(Float(w[scale]), Float(w[scale + 1]), Float(w[scale + 2]))));
                        break;
                    }
                    case VFXExpressionOperation.InverseTRSMatrix:
                    {
                        // The inverse of a matrix of a rotation, scale and translation; a matrix
                        // that has none (a scale of zero) gives a matrix of zeros.
                        Matrix4x4.InverseAffine(Load(w, p0), out var inverse);
                        Store(w, at, inverse);
                        break;
                    }
                    // The inverse of any matrix (a camera's projection's, for a point from its
                    // viewport): a singular one gives a matrix of zeros.
                    case VFXExpressionOperation.InverseMatrix: Store(w, at, Matrix4x4.Inverse(Load(w, p0))); break;
                    case VFXExpressionOperation.RowToMatrix:
                    case VFXExpressionOperation.ColumnToMatrix:
                    {
                        // Four vectors, the matrix's rows or its columns.
                        int v2 = offsets[operands[d + 2]], v3 = offsets[operands[d + 3]];
                        bool rows = op == VFXExpressionOperation.RowToMatrix;
                        for (int i = 0; i < 4; i++)
                            for (int k = 0; k < 4; k++)
                            {
                                uint word = (k == 0 ? w[p0 + i] : k == 1 ? w[p1 + i] : k == 2 ? w[v2 + i] : w[v3 + i]);
                                // Column-major storage: cell (row, column) at column * 4 + row.
                                if (rows) w[at + i * 4 + k] = word;
                                else w[at + k * 4 + i] = word;
                            }
                        break;
                    }
                    case VFXExpressionOperation.AxisToMatrix:
                    {
                        // Three axes and a position: the columns, with the last row (0, 0, 0, 1).
                        int v2 = offsets[operands[d + 2]], v3 = offsets[operands[d + 3]];
                        for (int k = 0; k < 3; k++)
                        {
                            w[at + k] = w[p0 + k];
                            w[at + 4 + k] = w[p1 + k];
                            w[at + 8 + k] = w[v2 + k];
                            w[at + 12 + k] = w[v3 + k];
                        }
                        (w[at + 3], w[at + 7], w[at + 11], w[at + 15]) = (0u, 0u, 0u, Bits(1f));
                        break;
                    }
                    case VFXExpressionOperation.MatrixToRow:
                    {
                        int index = unchecked((int)w[p1]);
                        for (int k = 0; k < 4; k++) w[at + k] = w[p0 + k * 4 + index];
                        break;
                    }
                    case VFXExpressionOperation.MatrixToColumn:
                    {
                        int index = unchecked((int)w[p1]);
                        for (int k = 0; k < 4; k++) w[at + k] = w[p0 + index * 4 + k];
                        break;
                    }
                    case VFXExpressionOperation.MatrixToAxis:
                    {
                        // A column of the matrix without its last row.
                        int index = unchecked((int)w[p1]);
                        for (int k = 0; k < 3; k++) w[at + k] = w[p0 + index * 4 + k];
                        break;
                    }
                    case VFXExpressionOperation.ExtractPositionFromMatrix:
                        (w[at], w[at + 1], w[at + 2]) = (w[p0 + 12], w[p0 + 13], w[p0 + 14]);
                        break;
                    case VFXExpressionOperation.ExtractAnglesFromMatrix:
                    {
                        // Of the matrix with its last row (0, 0, 0, 1): its rotation's Euler angles
                        // in degrees, of the rotation as it comes (not normalized, as a script's
                        // Quaternion.eulerAngles has it) and not made positive.
                        var m = AffinePart(Load(w, p0));
                        var angles = Quaternion.EulerRadOf(m.rotation) * Mathf.Rad2Deg;
                        (w[at], w[at + 1], w[at + 2]) = (Bits(angles.x), Bits(angles.y), Bits(angles.z));
                        break;
                    }
                    case VFXExpressionOperation.ExtractScaleFromMatrix:
                    {
                        var scale = AffinePart(Load(w, p0)).lossyScale;
                        (w[at], w[at + 1], w[at + 2]) = (Bits(scale.x), Bits(scale.y), Bits(scale.z));
                        break;
                    }
                    case VFXExpressionOperation.RGBtoHSV:
                    {
                        Color.RGBToHSV(new Color(Float(w[p0]), Float(w[p0 + 1]), Float(w[p0 + 2])), out float h, out float s, out float v);
                        (w[at], w[at + 1], w[at + 2]) = (Bits(h), Bits(s), Bits(v));
                        break;
                    }
                    case VFXExpressionOperation.HSVtoRGB:
                    {
                        // By sixths of the hue circle (a hue in degrees), the chroma and the second
                        // largest component from the hue's position in its sixth.
                        float hue = Float(w[p0]) * 360f, value = Float(w[p0 + 2]), chroma = Float(w[p0 + 1]) * value;
                        float second = (1f - MathF.Abs(hue / 60f % 2f + -1f)) * chroma;
                        float r = chroma, g = second, b = 0f;
                        if (hue >= 60f) { r = second; g = chroma; }
                        if (hue >= 120f) { r = 0f; g = chroma; b = second; }
                        if (hue >= 180f) { g = second; b = chroma; }
                        if (hue >= 240f) { r = second; g = 0f; b = chroma; }
                        if (hue >= 300f) { r = chroma; b = second; }
                        float rest = value - chroma;
                        (w[at], w[at + 1], w[at + 2]) = (Bits(r + rest), Bits(g + rest), Bits(rest + b));
                        break;
                    }
                    // A texture's size (none, or destroyed: zero) and a buffer's element count
                    // (none: zero; one released while it is set keeps its count).
                    case VFXExpressionOperation.TextureWidth: w[at] = values.GetObject(a0) is Texture wide && wide ? (uint)wide.width : 0u; break;
                    case VFXExpressionOperation.TextureHeight: w[at] = values.GetObject(a0) is Texture high && high ? (uint)high.height : 0u; break;
                    case VFXExpressionOperation.BufferCount: w[at] = values.GetObject(a0) is GraphicsBuffer counted ? (uint)counted.ElementCount : 0u; break;
                    case VFXExpressionOperation.BufferStride: w[at] = values.GetObject(a0) is GraphicsBuffer strided ? (uint)strided.ElementStride : 0u; break;
                    case VFXExpressionOperation.TextureDepth: w[at] = TextureDepth(values.GetObject(a0) as Texture); break;

                    case VFXExpressionOperation.MeshVertexCount: w[at] = VFXMeshFunctions.VertexCount(values.GetObject(a0) as Mesh); break;
                    case VFXExpressionOperation.MeshIndexCount: w[at] = VFXMeshFunctions.IndexCount(values.GetObject(a0) as Mesh); break;
                    case VFXExpressionOperation.MeshIndexFormat: w[at] = VFXMeshFunctions.IndexFormat(values.GetObject(a0) as Mesh); break;
                    case VFXExpressionOperation.MeshVertexStride: w[at] = VFXMeshFunctions.VertexStride(values.GetObject(a0) as Mesh, w[p1]); break;
                    case VFXExpressionOperation.MeshChannelOffset: w[at] = VFXMeshFunctions.ChannelOffset(values.GetObject(a0) as Mesh, w[p1]); break;
                    case VFXExpressionOperation.MeshChannelInfos: w[at] = VFXMeshFunctions.ChannelInfos(values.GetObject(a0) as Mesh, w[p1]); break;
                    case VFXExpressionOperation.SampleMeshIndex:
                        w[at] = VFXMeshFunctions.Index(values.GetObject(a0) as Mesh, w[p1], w[offsets[operands[d + 2]]]);
                        break;
                    case VFXExpressionOperation.SampleMeshVertexFloat:
                    case VFXExpressionOperation.SampleMeshVertexFloat2:
                    case VFXExpressionOperation.SampleMeshVertexFloat3:
                    case VFXExpressionOperation.SampleMeshVertexFloat4:
                    case VFXExpressionOperation.SampleMeshVertexColor:
                    {
                        int read = op == VFXExpressionOperation.SampleMeshVertexColor ? 4 : op - VFXExpressionOperation.SampleMeshVertexFloat + 1;
                        var sampledMesh = values.GetObject(a0) as Mesh;
                        uint infos = w[offsets[operands[d + 2]]];
                        if (op == VFXExpressionOperation.SampleMeshVertexColor) VFXMeshFunctions.SampleColor(sampledMesh, w[p1], infos, vertex);
                        else VFXMeshFunctions.Sample(sampledMesh, w[p1], infos, vertex.Slice(0, read));
                        for (int c = 0; c < read; c++) w[at + c] = Bits(vertex[c]);
                        break;
                    }
                    // The views of a mesh's buffers the programs read, kept while they still
                    // view them.
                    case VFXExpressionOperation.VertexBufferFromMesh:
                    case VFXExpressionOperation.IndexBufferFromMesh:
                    {
                        var kept = values.GetObject(e) as GraphicsBuffer;
                        var viewed = values.GetObject(a0) as Mesh;
                        var view = op == VFXExpressionOperation.VertexBufferFromMesh
                            ? VFXMeshFunctions.VertexBuffer(viewed, w[p1], kept)
                            : VFXMeshFunctions.IndexBuffer(viewed, kept);
                        if (ReferenceEquals(view, kept)) break;
                        if (!VFXMeshFunctions.IsNoBuffer(kept)) kept?.Release();
                        values.SetObject(e, view);
                        break;
                    }
                    case VFXExpressionOperation.TransposeMatrix: Store(w, at, Load(w, p0).transpose); break;
                    case VFXExpressionOperation.TransformMatrix: Store(w, at, Load(w, p0) * Load(w, p1)); break;
                    case VFXExpressionOperation.TransformPos:
                    case VFXExpressionOperation.TransformVec:
                    case VFXExpressionOperation.TransformDir:
                    {
                        // Column by column, in the columns' order: the order the sums round in.
                        float x = Float(w[p1]), y = Float(w[p1 + 1]), z = Float(w[p1 + 2]);
                        float rx = Float(w[p0]) * x + Float(w[p0 + 4]) * y + Float(w[p0 + 8]) * z;
                        float ry = Float(w[p0 + 1]) * x + Float(w[p0 + 5]) * y + Float(w[p0 + 9]) * z;
                        float rz = Float(w[p0 + 2]) * x + Float(w[p0 + 6]) * y + Float(w[p0 + 10]) * z;
                        if (op == VFXExpressionOperation.TransformPos)
                        {
                            rx += Float(w[p0 + 12]);
                            ry += Float(w[p0 + 13]);
                            rz += Float(w[p0 + 14]);
                        }
                        else if (op == VFXExpressionOperation.TransformDir)
                        {
                            // Each component divided by the length.
                            float length = MathF.Sqrt(rx * rx + ry * ry + rz * rz);
                            rx /= length;
                            ry /= length;
                            rz /= length;
                        }
                        (w[at], w[at + 1], w[at + 2]) = (Bits(rx), Bits(ry), Bits(rz));
                        break;
                    }
                    case VFXExpressionOperation.TransformVector4:
                    {
                        float x = Float(w[p1]), y = Float(w[p1 + 1]), z = Float(w[p1 + 2]), t = Float(w[p1 + 3]);
                        for (int row = 0; row < 4; row++)
                            w[at + row] = Bits(Float(w[p0 + row]) * x + Float(w[p0 + 4 + row]) * y + Float(w[p0 + 8 + row]) * z + Float(w[p0 + 12 + row]) * t);
                        break;
                    }

                    case VFXExpressionOperation.BitwiseLeftShift:
                    case VFXExpressionOperation.BitwiseRightShift:
                    case VFXExpressionOperation.BitwiseOr:
                    case VFXExpressionOperation.BitwiseAnd:
                    case VFXExpressionOperation.BitwiseXor:
                    {
                        int size = VFXExpressionHelper.GetSizeOfType(data.ExpressionTypes[e]);
                        for (int i = 0; i < size; i++)
                        {
                            uint a = w[p0 + i], b = w[p1 + i];
                            w[at + i] = op switch
                            {
                                VFXExpressionOperation.BitwiseLeftShift => a << (int)(b & 31),
                                VFXExpressionOperation.BitwiseRightShift => a >> (int)(b & 31),
                                VFXExpressionOperation.BitwiseOr => a | b,
                                VFXExpressionOperation.BitwiseAnd => a & b,
                                _ => a ^ b,
                            };
                        }
                        break;
                    }
                    case VFXExpressionOperation.BitwiseComplement:
                    {
                        int size = VFXExpressionHelper.GetSizeOfType(data.ExpressionTypes[e]);
                        for (int i = 0; i < size; i++) w[at + i] = ~w[p0 + i];
                        break;
                    }

                    case VFXExpressionOperation.CastUintToFloat: w[at] = Bits((float)w[p0]); break;
                    case VFXExpressionOperation.CastIntToFloat: w[at] = Bits((float)(int)w[p0]); break;
                    case VFXExpressionOperation.CastFloatToUint: w[at] = ToUint(Float(w[p0])); break;
                    case VFXExpressionOperation.CastFloatToInt: w[at] = unchecked((uint)ToInt(Float(w[p0]))); break;
                    case VFXExpressionOperation.CastIntToUint:
                    case VFXExpressionOperation.CastUintToInt:
                        w[at] = w[p0];
                        break;
                    case VFXExpressionOperation.CastIntToBool:
                    case VFXExpressionOperation.CastUintToBool:
                        w[at] = w[p0] != 0 ? 1u : 0u;
                        break;
                    case VFXExpressionOperation.CastFloatToBool: w[at] = Float(w[p0]) != 0f ? 1u : 0u; break;
                    // A boolean is its low byte.
                    case VFXExpressionOperation.CastBoolToInt:
                    case VFXExpressionOperation.CastBoolToUint:
                        w[at] = w[p0] & 0xff;
                        break;
                    case VFXExpressionOperation.CastBoolToFloat: w[at] = Bits((w[p0] & 0xff) != 0 ? 1f : 0f); break;

                    case VFXExpressionOperation.Condition:
                    {
                        int type = operands[d + 2];
                        var mode = (Comparison)operands[d + 3];
                        bool result = type == Int32Type ? Compare(mode, ((int)w[p0]).CompareTo((int)w[p1]), false)
                            : type == Uint32Type ? Compare(mode, w[p0].CompareTo(w[p1]), false)
                            : Compare(mode, Float(w[p0]), Float(w[p1]));
                        w[at] = result ? 1u : 0u;
                        break;
                    }
                    case VFXExpressionOperation.Branch:
                    {
                        int from = offsets[operands[d + ((w[p0] & 0xff) != 0 ? 1 : 2)]];
                        int size = VFXExpressionHelper.GetSizeOfType((VFXValueType)operands[d + 3]);
                        for (int i = 0; i < size; i++) w[at + i] = w[from + i];
                        break;
                    }
                    case VFXExpressionOperation.LogicalAnd: w[at] = (w[p0] & 0xff) != 0 && (w[p1] & 0xff) != 0 ? 1u : 0u; break;
                    case VFXExpressionOperation.LogicalOr: w[at] = (w[p0] & 0xff) != 0 || (w[p1] & 0xff) != 0 ? 1u : 0u; break;
                    case VFXExpressionOperation.LogicalNot: w[at] = (w[p0] & 0xff) != 0 ? 0u : 1u; break;

                    // The main camera as the manager's update took it (VFXManager.MainCamera), and
                    // what a graph reads while there is none.
                    case VFXExpressionOperation.ExtractMatrixFromMainCamera: Store(w, at, VFXManager.MainCameraMatrix); break;
                    case VFXExpressionOperation.ExtractFOVFromMainCamera:
                        w[at] = Bits((VFXManager.MainCamera is Camera fovCamera ? fovCamera.GetGateFittedFieldOfView() : NoCameraFieldOfView) * Mathf.Deg2Rad);
                        break;
                    case VFXExpressionOperation.ExtractNearPlaneFromMainCamera: w[at] = Bits(VFXManager.MainCamera is Camera near ? near.nearClipPlane : NoCameraNearPlane); break;
                    case VFXExpressionOperation.ExtractFarPlaneFromMainCamera: w[at] = Bits(VFXManager.MainCamera is Camera far ? far.farClipPlane : NoCameraFarPlane); break;
                    case VFXExpressionOperation.ExtractAspectRatioFromMainCamera: w[at] = Bits(VFXManager.MainCamera is Camera aspect ? aspect.aspect : 1f); break;
                    case VFXExpressionOperation.ExtractPixelDimensionsFromMainCamera:
                        (w[at], w[at + 1]) = (Bits(VFXManager.MainCameraViewport.width), Bits(VFXManager.MainCameraViewport.height));
                        break;
                    case VFXExpressionOperation.ExtractScaledPixelDimensionsFromMainCamera:
                        (w[at], w[at + 1]) = (Bits(VFXManager.MainCameraScaledSize.x), Bits(VFXManager.MainCameraScaledSize.y));
                        break;
                    case VFXExpressionOperation.ExtractLensShiftFromMainCamera:
                    {
                        var shift = VFXManager.MainCamera is Camera shifted ? shifted.GetGateFittedLensShift() : Vector2.zero;
                        (w[at], w[at + 1]) = (Bits(shift.x), Bits(shift.y));
                        break;
                    }
                    // The buffer of the kind the fourth operand names (a graph that reads one does
                    // not run yet: VisualEffect.Unsupported).
                    case VFXExpressionOperation.GetBufferFromMainCamera: values.SetObject(e, VFXManager.MainCameraBuffer((uint)operands[d + 3])); break;
                    case VFXExpressionOperation.IsMainCameraOrthographic: w[at] = VFXManager.MainCamera is Camera ortho && ortho.orthographic ? 1u : 0u; break;
                    case VFXExpressionOperation.GetOrthographicSizeFromMainCamera: w[at] = Bits(VFXManager.MainCamera is Camera sized ? sized.orthographicSize : NoCameraOrthographicSize); break;

                    case VFXExpressionOperation.GenerateRandom: w[at] = Bits(state.Random.NextValue()); break;
                    case VFXExpressionOperation.GenerateFixedRandom: w[at] = Bits(FixedRandom(w[p0])); break;

                    // An attribute of the spawn context's own event as its blocks left it, at
                    // the element the third operand names (the fourth is its type); a bool is
                    // its word's low byte (measured for false and true only). Outside a spawn
                    // context the value stays.
                    case VFXExpressionOperation.ReadEventAttribute:
                    {
                        if (spawner == null) break;
                        var source = spawner.EventAttribute.Words;
                        int element = operands[d + 2];
                        var type = (VFXValueType)operands[d + 3];
                        // An effect reading otherwise is refused (VisualEffect.Unsupported).
                        if (!ReadableEventAttribute(type) || !EventAttributeWithin(type, element, source.Length)) break;
                        if (type == VFXValueType.Boolean) w[at] = source[element] & 0xffu;
                        else Array.Copy(source, element, w, at, VFXExpressionHelper.GetSizeOfType(type));
                        break;
                    }
                    case VFXExpressionOperation.SpawnerStateNewLoop: if (spawner != null) w[at] = spawner.NewLoop ? 1u : 0u; break;
                    case VFXExpressionOperation.SpawnerStateLoopState: if (spawner != null) w[at] = (uint)spawner.LoopState; break;
                    case VFXExpressionOperation.SpawnerStateSpawnCount: if (spawner != null) w[at] = Bits(spawner.SpawnCount); break;
                    case VFXExpressionOperation.SpawnerStateDeltaTime: if (spawner != null) w[at] = Bits(spawner.DeltaTime); break;
                    case VFXExpressionOperation.SpawnerStateTotalTime: if (spawner != null) w[at] = Bits(spawner.TotalTime); break;
                    case VFXExpressionOperation.SpawnerStateDelayBeforeLoop: if (spawner != null) w[at] = Bits(spawner.DelayBeforeLoop); break;
                    case VFXExpressionOperation.SpawnerStateLoopDuration: if (spawner != null) w[at] = Bits(spawner.LoopDuration); break;
                    case VFXExpressionOperation.SpawnerStateDelayAfterLoop: if (spawner != null) w[at] = Bits(spawner.DelayAfterLoop); break;
                    case VFXExpressionOperation.SpawnerStateLoopIndex: if (spawner != null) w[at] = unchecked((uint)spawner.LoopIndex); break;
                    case VFXExpressionOperation.SpawnerStateLoopCount: if (spawner != null) w[at] = unchecked((uint)spawner.LoopCount); break;
                }
            }
        }

        enum Comparison { Equal, NotEqual, Less, LessOrEqual, Greater, GreaterOrEqual }

        static bool Compare(Comparison mode, int order, bool unordered) => !unordered && mode switch
        {
            Comparison.Equal => order == 0,
            Comparison.NotEqual => order != 0,
            Comparison.Less => order < 0,
            Comparison.LessOrEqual => order <= 0,
            Comparison.Greater => order > 0,
            Comparison.GreaterOrEqual => order >= 0,
            _ => false,
        };

        /// <summary>A comparison of floats: with a NaN only "not equal" holds.</summary>
        static bool Compare(Comparison mode, float left, float right) => mode switch
        {
            Comparison.Equal => left == right,
            Comparison.NotEqual => left != right,
            Comparison.Less => left < right,
            Comparison.LessOrEqual => left <= right,
            Comparison.Greater => left > right,
            Comparison.GreaterOrEqual => left >= right,
            _ => false,
        };

        static float Unary(VFXExpressionOperation op, float x)
        {
            switch (op)
            {
                case VFXExpressionOperation.Sin: return MathF.Sin(x);
                case VFXExpressionOperation.Cos: return MathF.Cos(x);
                case VFXExpressionOperation.Tan: return MathF.Tan(x);
                case VFXExpressionOperation.ASin: return MathF.Asin(x);
                case VFXExpressionOperation.ACos: return MathF.Acos(x);
                case VFXExpressionOperation.ATan: return MathF.Atan(x);
                case VFXExpressionOperation.Abs: return MathF.Abs(x);
                case VFXExpressionOperation.Sign: return (x > 0f ? 1 : 0) - (x < 0f ? 1 : 0);
                // A NaN stays one: it loses neither comparison.
                case VFXExpressionOperation.Saturate: { float low = 0f > x ? 0f : x; return 1f < low ? 1f : low; }
                case VFXExpressionOperation.Ceil: return MathF.Ceiling(x);
                // Halves round to the even neighbour.
                case VFXExpressionOperation.Round: return MathF.Round(x, MidpointRounding.ToEven);
                case VFXExpressionOperation.Frac: return x - MathF.Floor(x);
                case VFXExpressionOperation.Floor: return MathF.Floor(x);
                default: return MathF.Log2(x);
            }
        }

        static float Binary(VFXExpressionOperation op, float a, float b)
        {
            switch (op)
            {
                case VFXExpressionOperation.Mul: return a * b;
                case VFXExpressionOperation.Divide: return a / b;
                case VFXExpressionOperation.Add: return a + b;
                case VFXExpressionOperation.Subtract: return a - b;
                // With a NaN on either side the first operand is the result.
                case VFXExpressionOperation.Min: return b < a ? b : a;
                case VFXExpressionOperation.Max: return b > a ? b : a;
                case VFXExpressionOperation.Pow: return MathF.Pow(a, b);
                default: return MathF.Atan2(a, b);
            }
        }

        static int Binary(VFXExpressionOperation op, int a, int b)
        {
            switch (op)
            {
                case VFXExpressionOperation.Mul: return unchecked(a * b);
                // A division by zero is the largest value of the dividend's sign; the one
                // quotient an int cannot hold wraps.
                case VFXExpressionOperation.Divide: return b == 0 ? (a < 0 ? int.MinValue : int.MaxValue) : b == -1 ? unchecked(-a) : a / b;
                case VFXExpressionOperation.Add: return unchecked(a + b);
                case VFXExpressionOperation.Subtract: return unchecked(a - b);
                case VFXExpressionOperation.Min: return b <= a ? b : a;
                default: return a < b ? b : a;
            }
        }

        static uint Binary(VFXExpressionOperation op, uint a, uint b)
        {
            switch (op)
            {
                case VFXExpressionOperation.Mul: return unchecked(a * b);
                case VFXExpressionOperation.Divide: return b == 0 ? uint.MaxValue : a / b;
                case VFXExpressionOperation.Add: return unchecked(a + b);
                case VFXExpressionOperation.Subtract: return unchecked(a - b);
                case VFXExpressionOperation.Min: return b < a ? b : a;
                default: return a < b ? b : a;
            }
        }

        /// <summary>A float as an unsigned value: truncated as a 64-bit integer, its low word kept
        /// (a negative value wraps; one out of the 64-bit range, or a NaN, gives zero).</summary>
        internal static uint ToUint(float f) => float.IsNaN(f) || f >= 9.223372e18f || f < -9.223372e18f ? 0u : unchecked((uint)(long)f);

        /// <summary>A float truncated to an int; one out of range, or a NaN, is the smallest int.</summary>
        static int ToInt(float f) => float.IsNaN(f) || f >= 2147483648f || f < -2147483648f ? int.MinValue : (int)f;

        /// <summary>The number in [0, 1) a hash gives: its bits mixed, 23 of them a mantissa.</summary>
        static float FixedRandom(uint hash)
        {
            const uint Mix = 0x45d9f3b;
            hash = ((hash >> 16) ^ hash) * Mix;
            hash = ((hash >> 16) ^ hash) * Mix;
            hash = (hash >> 16) ^ hash;
            return BitConverter.UInt32BitsToSingle((hash >> 9) | 0x3f800000) - 1f;
        }

        static uint Bits(float f) => BitConverter.SingleToUInt32Bits(f);

        static float Float(uint bits) => BitConverter.UInt32BitsToSingle(bits);

        /// <summary>A matrix with its last row (0, 0, 0, 1): what a frame's angles and scale are
        /// taken from.</summary>
        static Matrix4x4 AffinePart(Matrix4x4 m)
        {
            m.m30 = 0f;
            m.m31 = 0f;
            m.m32 = 0f;
            m.m33 = 1f;
            return m;
        }

        static Matrix4x4 Load(uint[] w, int at)
        {
            var m = default(Matrix4x4);
            for (int i = 0; i < 16; i++) m[i] = Float(w[at + i]);
            return m;
        }

        static void Store(uint[] w, int at, Matrix4x4 m)
        {
            for (int i = 0; i < 16; i++) w[at + i] = Bits(m[i]);
        }
    }
}
