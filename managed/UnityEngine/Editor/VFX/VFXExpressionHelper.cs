using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.VFX;

namespace UnityEditor.VFX
{
    /// <summary>
    /// What the graph compiler asks about expressions: the type an operation produces, the size
    /// of a value type in 32-bit words and a texture type's dimension. Matches the Unity
    /// 6000.0.71f1 Editor for every operation and type (tests/projects/vfx, expression tables).
    /// </summary>
    internal static partial class VFXExpressionHelper
    {
        /// <summary>An operation whose result has the type its fourth data word names.</summary>
        const VFXValueType TypeInData = (VFXValueType)(-1);

        /// <summary>The words holding an operation's type when it is <see cref="TypeInData"/>.</summary>
        const int TypeDataIndex = 3;

        readonly struct Operation
        {
            internal readonly VFXValueType Type;
            internal readonly int Parents;

            internal Operation(VFXValueType type, int parents)
            {
                Type = type;
                Parents = parents;
            }
        }

        /// <summary>Per operation, in enum order: the type produced and how many of its data
        /// words are parent expression indices.</summary>
        static readonly Operation[] s_Operations = BuildOperations();

        static Operation[] BuildOperations()
        {
            var table = new Operation[(int)VFXExpressionOperation.SpawnerStateLoopCount + 1];
            void Set(VFXExpressionOperation op, VFXValueType type, int parents) => table[(int)op] = new Operation(type, parents);

            Set(VFXExpressionOperation.None, VFXValueType.None, 0);
            Set(VFXExpressionOperation.Value, TypeInData, 0);
            Set(VFXExpressionOperation.Combine2f, VFXValueType.Float2, 2);
            Set(VFXExpressionOperation.Combine3f, VFXValueType.Float3, 3);
            Set(VFXExpressionOperation.Combine4f, VFXValueType.Float4, 4);
            Set(VFXExpressionOperation.ExtractComponent, VFXValueType.Float, 1);
            Set(VFXExpressionOperation.DeltaTime, VFXValueType.Float, 0);
            Set(VFXExpressionOperation.TotalTime, VFXValueType.Float, 0);
            Set(VFXExpressionOperation.SystemSeed, VFXValueType.Uint32, 0);
            Set(VFXExpressionOperation.LocalToWorld, VFXValueType.Matrix4x4, 0);
            Set(VFXExpressionOperation.WorldToLocal, VFXValueType.Matrix4x4, 0);
            Set(VFXExpressionOperation.FrameIndex, VFXValueType.Uint32, 0);
            Set(VFXExpressionOperation.PlayRate, VFXValueType.Float, 0);
            Set(VFXExpressionOperation.UnscaledDeltaTime, VFXValueType.Float, 0);
            Set(VFXExpressionOperation.ManagerMaxDeltaTime, VFXValueType.Float, 0);
            Set(VFXExpressionOperation.ManagerFixedTimeStep, VFXValueType.Float, 0);
            Set(VFXExpressionOperation.GameDeltaTime, VFXValueType.Float, 0);
            Set(VFXExpressionOperation.GameUnscaledDeltaTime, VFXValueType.Float, 0);
            Set(VFXExpressionOperation.GameSmoothDeltaTime, VFXValueType.Float, 0);
            Set(VFXExpressionOperation.GameTotalTime, VFXValueType.Float, 0);
            Set(VFXExpressionOperation.GameUnscaledTotalTime, VFXValueType.Float, 0);
            Set(VFXExpressionOperation.GameTotalTimeSinceSceneLoad, VFXValueType.Float, 0);
            Set(VFXExpressionOperation.GameTimeScale, VFXValueType.Float, 0);
            Set(VFXExpressionOperation.Sin, TypeInData, 1);
            Set(VFXExpressionOperation.Cos, TypeInData, 1);
            Set(VFXExpressionOperation.Tan, TypeInData, 1);
            Set(VFXExpressionOperation.ASin, TypeInData, 1);
            Set(VFXExpressionOperation.ACos, TypeInData, 1);
            Set(VFXExpressionOperation.ATan, TypeInData, 1);
            Set(VFXExpressionOperation.Abs, TypeInData, 1);
            Set(VFXExpressionOperation.Sign, TypeInData, 1);
            Set(VFXExpressionOperation.Saturate, TypeInData, 1);
            Set(VFXExpressionOperation.Ceil, TypeInData, 1);
            Set(VFXExpressionOperation.Round, TypeInData, 1);
            Set(VFXExpressionOperation.Frac, TypeInData, 1);
            Set(VFXExpressionOperation.Floor, TypeInData, 1);
            Set(VFXExpressionOperation.Log2, TypeInData, 1);
            Set(VFXExpressionOperation.Mul, TypeInData, 2);
            Set(VFXExpressionOperation.Divide, TypeInData, 2);
            Set(VFXExpressionOperation.Add, TypeInData, 2);
            Set(VFXExpressionOperation.Subtract, TypeInData, 2);
            Set(VFXExpressionOperation.Min, TypeInData, 2);
            Set(VFXExpressionOperation.Max, TypeInData, 2);
            Set(VFXExpressionOperation.Pow, TypeInData, 2);
            Set(VFXExpressionOperation.ATan2, TypeInData, 2);
            Set(VFXExpressionOperation.TRSToMatrix, VFXValueType.Matrix4x4, 3);
            Set(VFXExpressionOperation.InverseMatrix, VFXValueType.Matrix4x4, 1);
            Set(VFXExpressionOperation.InverseTRSMatrix, VFXValueType.Matrix4x4, 1);
            Set(VFXExpressionOperation.TransposeMatrix, VFXValueType.Matrix4x4, 1);
            Set(VFXExpressionOperation.ExtractPositionFromMatrix, VFXValueType.Float3, 1);
            Set(VFXExpressionOperation.ExtractAnglesFromMatrix, VFXValueType.Float3, 1);
            Set(VFXExpressionOperation.ExtractScaleFromMatrix, VFXValueType.Float3, 1);
            Set(VFXExpressionOperation.TransformMatrix, VFXValueType.Matrix4x4, 2);
            Set(VFXExpressionOperation.TransformPos, VFXValueType.Float3, 2);
            Set(VFXExpressionOperation.TransformVec, VFXValueType.Float3, 2);
            Set(VFXExpressionOperation.TransformDir, VFXValueType.Float3, 2);
            Set(VFXExpressionOperation.TransformVector4, VFXValueType.Float4, 2);
            Set(VFXExpressionOperation.RowToMatrix, VFXValueType.Matrix4x4, 4);
            Set(VFXExpressionOperation.ColumnToMatrix, VFXValueType.Matrix4x4, 4);
            Set(VFXExpressionOperation.AxisToMatrix, VFXValueType.Matrix4x4, 4);
            Set(VFXExpressionOperation.MatrixToRow, VFXValueType.Float4, 2);
            Set(VFXExpressionOperation.MatrixToColumn, VFXValueType.Float4, 2);
            Set(VFXExpressionOperation.MatrixToAxis, VFXValueType.Float3, 2);
            Set(VFXExpressionOperation.SampleCurve, VFXValueType.Float, 2);
            Set(VFXExpressionOperation.SampleGradient, VFXValueType.Float4, 2);
            Set(VFXExpressionOperation.SampleMeshVertexFloat, VFXValueType.Float, 3);
            Set(VFXExpressionOperation.SampleMeshVertexFloat2, VFXValueType.Float2, 3);
            Set(VFXExpressionOperation.SampleMeshVertexFloat3, VFXValueType.Float3, 3);
            Set(VFXExpressionOperation.SampleMeshVertexFloat4, VFXValueType.Float4, 3);
            Set(VFXExpressionOperation.SampleMeshVertexColor, VFXValueType.Float4, 3);
            Set(VFXExpressionOperation.SampleMeshIndex, VFXValueType.Uint32, 3);
            Set(VFXExpressionOperation.VertexBufferFromMesh, VFXValueType.Buffer, 2);
            Set(VFXExpressionOperation.VertexBufferFromSkinnedMeshRenderer, VFXValueType.Buffer, 2);
            Set(VFXExpressionOperation.IndexBufferFromMesh, VFXValueType.Buffer, 1);
            Set(VFXExpressionOperation.MeshFromSkinnedMeshRenderer, VFXValueType.Mesh, 1);
            Set(VFXExpressionOperation.RootBoneTransformFromSkinnedMeshRenderer, VFXValueType.Matrix4x4, 1);
            Set(VFXExpressionOperation.BakeCurve, VFXValueType.Float4, 1);
            Set(VFXExpressionOperation.BakeGradient, VFXValueType.Float3, 1);
            Set(VFXExpressionOperation.BitwiseLeftShift, VFXValueType.Uint32, 2);
            Set(VFXExpressionOperation.BitwiseRightShift, VFXValueType.Uint32, 2);
            Set(VFXExpressionOperation.BitwiseOr, VFXValueType.Uint32, 2);
            Set(VFXExpressionOperation.BitwiseAnd, VFXValueType.Uint32, 2);
            Set(VFXExpressionOperation.BitwiseXor, VFXValueType.Uint32, 2);
            Set(VFXExpressionOperation.BitwiseComplement, VFXValueType.Uint32, 1);
            Set(VFXExpressionOperation.CastUintToFloat, VFXValueType.Float, 1);
            Set(VFXExpressionOperation.CastIntToFloat, VFXValueType.Float, 1);
            Set(VFXExpressionOperation.CastFloatToUint, VFXValueType.Uint32, 1);
            Set(VFXExpressionOperation.CastIntToUint, VFXValueType.Uint32, 1);
            Set(VFXExpressionOperation.CastFloatToInt, VFXValueType.Int32, 1);
            Set(VFXExpressionOperation.CastUintToInt, VFXValueType.Int32, 1);
            Set(VFXExpressionOperation.CastIntToBool, VFXValueType.Boolean, 1);
            Set(VFXExpressionOperation.CastUintToBool, VFXValueType.Boolean, 1);
            Set(VFXExpressionOperation.CastFloatToBool, VFXValueType.Boolean, 1);
            Set(VFXExpressionOperation.CastBoolToInt, VFXValueType.Int32, 1);
            Set(VFXExpressionOperation.CastBoolToUint, VFXValueType.Uint32, 1);
            Set(VFXExpressionOperation.CastBoolToFloat, VFXValueType.Float, 1);
            Set(VFXExpressionOperation.RGBtoHSV, VFXValueType.Float3, 1);
            Set(VFXExpressionOperation.HSVtoRGB, VFXValueType.Float3, 1);
            Set(VFXExpressionOperation.Condition, VFXValueType.Boolean, 2);
            Set(VFXExpressionOperation.Branch, TypeInData, 2);
            Set(VFXExpressionOperation.GenerateRandom, VFXValueType.Float, 0);
            Set(VFXExpressionOperation.GenerateFixedRandom, VFXValueType.Float, 0);
            Set(VFXExpressionOperation.ExtractMatrixFromMainCamera, VFXValueType.Matrix4x4, 0);
            Set(VFXExpressionOperation.ExtractFOVFromMainCamera, VFXValueType.Float, 0);
            Set(VFXExpressionOperation.ExtractNearPlaneFromMainCamera, VFXValueType.Float, 0);
            Set(VFXExpressionOperation.ExtractFarPlaneFromMainCamera, VFXValueType.Float, 0);
            Set(VFXExpressionOperation.ExtractAspectRatioFromMainCamera, VFXValueType.Float, 0);
            Set(VFXExpressionOperation.ExtractPixelDimensionsFromMainCamera, VFXValueType.Float2, 0);
            Set(VFXExpressionOperation.ExtractScaledPixelDimensionsFromMainCamera, VFXValueType.Float2, 0);
            Set(VFXExpressionOperation.ExtractLensShiftFromMainCamera, VFXValueType.Float2, 0);
            Set(VFXExpressionOperation.GetBufferFromMainCamera, VFXValueType.CameraBuffer, 0);
            Set(VFXExpressionOperation.IsMainCameraOrthographic, VFXValueType.Boolean, 0);
            Set(VFXExpressionOperation.GetOrthographicSizeFromMainCamera, VFXValueType.Float, 0);
            Set(VFXExpressionOperation.LogicalAnd, VFXValueType.Boolean, 2);
            Set(VFXExpressionOperation.LogicalOr, VFXValueType.Boolean, 2);
            Set(VFXExpressionOperation.LogicalNot, VFXValueType.Boolean, 1);
            Set(VFXExpressionOperation.ValueNoise1D, VFXValueType.Float2, 3);
            Set(VFXExpressionOperation.ValueNoise2D, VFXValueType.Float3, 3);
            Set(VFXExpressionOperation.ValueNoise3D, VFXValueType.Float4, 3);
            Set(VFXExpressionOperation.ValueCurlNoise2D, VFXValueType.Float2, 3);
            Set(VFXExpressionOperation.ValueCurlNoise3D, VFXValueType.Float3, 3);
            Set(VFXExpressionOperation.PerlinNoise1D, VFXValueType.Float2, 3);
            Set(VFXExpressionOperation.PerlinNoise2D, VFXValueType.Float3, 3);
            Set(VFXExpressionOperation.PerlinNoise3D, VFXValueType.Float4, 3);
            Set(VFXExpressionOperation.PerlinCurlNoise2D, VFXValueType.Float2, 3);
            Set(VFXExpressionOperation.PerlinCurlNoise3D, VFXValueType.Float3, 3);
            Set(VFXExpressionOperation.CellularNoise1D, VFXValueType.Float2, 3);
            Set(VFXExpressionOperation.CellularNoise2D, VFXValueType.Float3, 3);
            Set(VFXExpressionOperation.CellularNoise3D, VFXValueType.Float4, 3);
            Set(VFXExpressionOperation.CellularCurlNoise2D, VFXValueType.Float2, 3);
            Set(VFXExpressionOperation.CellularCurlNoise3D, VFXValueType.Float3, 3);
            Set(VFXExpressionOperation.VoroNoise2D, VFXValueType.Float, 2);
            Set(VFXExpressionOperation.MeshVertexCount, VFXValueType.Uint32, 1);
            Set(VFXExpressionOperation.MeshChannelOffset, VFXValueType.Uint32, 2);
            Set(VFXExpressionOperation.MeshChannelInfos, VFXValueType.Uint32, 2);
            Set(VFXExpressionOperation.MeshVertexStride, VFXValueType.Uint32, 2);
            Set(VFXExpressionOperation.MeshIndexCount, VFXValueType.Uint32, 1);
            Set(VFXExpressionOperation.MeshIndexFormat, VFXValueType.Uint32, 1);
            Set(VFXExpressionOperation.BufferStride, VFXValueType.Uint32, 1);
            Set(VFXExpressionOperation.BufferCount, VFXValueType.Uint32, 1);
            Set(VFXExpressionOperation.TextureWidth, VFXValueType.Uint32, 1);
            Set(VFXExpressionOperation.TextureHeight, VFXValueType.Uint32, 1);
            Set(VFXExpressionOperation.TextureDepth, VFXValueType.Uint32, 1);
            Set(VFXExpressionOperation.TextureFormat, VFXValueType.Uint32, 1);
            Set(VFXExpressionOperation.ReadEventAttribute, TypeInData, 0);
            Set(VFXExpressionOperation.SpawnerStateNewLoop, VFXValueType.Boolean, 0);
            Set(VFXExpressionOperation.SpawnerStateLoopState, VFXValueType.Uint32, 0);
            Set(VFXExpressionOperation.SpawnerStateSpawnCount, VFXValueType.Float, 0);
            Set(VFXExpressionOperation.SpawnerStateDeltaTime, VFXValueType.Float, 0);
            Set(VFXExpressionOperation.SpawnerStateTotalTime, VFXValueType.Float, 0);
            Set(VFXExpressionOperation.SpawnerStateDelayBeforeLoop, VFXValueType.Float, 0);
            Set(VFXExpressionOperation.SpawnerStateLoopDuration, VFXValueType.Float, 0);
            Set(VFXExpressionOperation.SpawnerStateDelayAfterLoop, VFXValueType.Float, 0);
            Set(VFXExpressionOperation.SpawnerStateLoopIndex, VFXValueType.Int32, 0);
            Set(VFXExpressionOperation.SpawnerStateLoopCount, VFXValueType.Int32, 0);
            return table;
        }

        /// <summary>The type an operation produces; None for an unknown operation.</summary>
        public static VFXValueType GetTypeOfOperation(VFXExpressionOperation op, int data0, int data1, int data2, int data3)
        {
            if ((uint)op >= (uint)s_Operations.Length) return VFXValueType.None;
            var type = s_Operations[(int)op].Type;
            return type == TypeInData ? (VFXValueType)data3 : type;
        }

        /// <summary>The type of an expression from its operation and data words.</summary>
        internal static VFXValueType TypeOf(VFXExpressionOperation op, int[] data) =>
            GetTypeOfOperation(op, 0, 0, 0, data != null && data.Length > TypeDataIndex ? data[TypeDataIndex] : 0);

        /// <summary>How many of an operation's data words are parent expression indices; -1 for
        /// an unknown operation.</summary>
        internal static int ParentCount(VFXExpressionOperation op) =>
            (uint)op < (uint)s_Operations.Length ? s_Operations[(int)op].Parents : -1;

        /// <summary>The size of a value in 32-bit words: its components for vectors and
        /// matrices, 1 for scalars, objects and booleans; -1 for a type without a size
        /// (None, Spline).</summary>
        public static int GetSizeOfType(VFXValueType type)
        {
            switch (type)
            {
                case VFXValueType.Float2: return 2;
                case VFXValueType.Float3: return 3;
                case VFXValueType.Float4: return 4;
                case VFXValueType.Matrix4x4: return 16;
                case VFXValueType.Buffer: return 4;
                case VFXValueType.None:
                case VFXValueType.Spline:
                    Debug.LogError("Invalid value type: " + (int)type);
                    return -1;
                default:
                    if ((uint)type > (uint)VFXValueType.SkinnedMeshRenderer)
                    {
                        Debug.LogError("Invalid value type: " + (int)type);
                        return -1;
                    }
                    return 1;
            }
        }

        /// <summary>The dimension of a texture value type; Unknown for other types.</summary>
        public static TextureDimension GetTextureDimension(VFXValueType type)
        {
            switch (type)
            {
                case VFXValueType.Texture2D: return TextureDimension.Tex2D;
                case VFXValueType.Texture2DArray: return TextureDimension.Tex2DArray;
                case VFXValueType.Texture3D: return TextureDimension.Tex3D;
                case VFXValueType.TextureCube: return TextureDimension.Cube;
                case VFXValueType.TextureCubeArray: return TextureDimension.CubeArray;
                default: return TextureDimension.Unknown;
            }
        }
    }
}
