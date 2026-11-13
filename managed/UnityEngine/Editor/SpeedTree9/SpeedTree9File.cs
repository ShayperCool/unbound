using System;
using System.Buffers.Binary;
using System.Text;

namespace Unbound.SpeedTree
{
    /// <summary>
    /// A SpeedTree 9 (<c>.st9</c>) file as Unity 6000.0's importer reads it: the signature
    /// <c>SpeedTree9______</c>, then tables. A table is a uint32 count and that many uint32 offsets,
    /// relative to the table's start, to its entries. An entry is a scalar, a packed struct, a string
    /// (uint32 length counting a terminating zero, then the bytes), an array (uint32 count, then the
    /// items) or another table. Little-endian. Only the entries the importer uses are read.
    /// </summary>
    internal sealed class SpeedTree9File
    {
        internal const string Signature = "SpeedTree9______";

        // Root entries.
        const int RootVersionMajor = 0, RootVersionMinor = 1, RootBounds = 2, RootLods = 5, RootBillboard = 6,
            RootCollisions = 7, RootMaterials = 10, RootWind = 15;

        /// <summary>One vertex as the file stores it (144 bytes).</summary>
        internal const int VertexSize = 144;
        const int DrawCallSize = 16;

        internal uint VersionMajor, VersionMinor;
        internal Vector3f BoundsMin, BoundsMax;
        internal Lod[] Lods;
        internal bool LastLodIsBillboard;
        internal Collision[] Collisions;
        internal Material[] Materials;
        internal Wind WindConfig;

        internal struct Vector3f { internal float X, Y, Z; }

        internal struct Vertex
        {
            internal Vector3f Anchor, Offset, Normal, Tangent, Binormal, Color, BranchWind1, BranchWind2;
            internal float U, V, LightmapU, LightmapV, AmbientOcclusion, BlendWeight, RippleWeight;
            internal bool CameraFacing;
        }

        internal struct DrawCall
        {
            internal uint MaterialIndex, IndexStart, IndexCount;
            internal bool ContainsFacingGeometry;
        }

        internal sealed class Lod
        {
            internal Vertex[] Vertices;
            internal uint[] Indices;
            internal DrawCall[] DrawCalls;
        }

        internal sealed class Collision
        {
            internal Vector3f Position, Position2;
            internal float Radius;
        }

        internal sealed class Map
        {
            internal bool Used;
            internal string Path;
            internal float R, G, B, A;
        }

        internal sealed class Material
        {
            internal string Name;
            internal bool TwoSided, FlipNormalsOnBackside, Billboard;
            internal Map[] Maps;
        }

        internal sealed class Branch
        {
            internal float[] Bend, Oscillation, Speed, Turbulence, Flexibility;
            internal float Independence;
        }

        internal sealed class Ripple
        {
            internal float[] Planar, Directional, Speed, Flexibility;
            internal float Shimmer, Independence;
        }

        internal sealed class Wind
        {
            internal float StrengthResponse, DirectionResponse, GustFrequency, GustStrengthMin, GustStrengthMax,
                GustDurationMin, GustDurationMax, GustRiseScalar, GustFallScalar;
            internal Branch Shared, Branch1, Branch2;
            internal Ripple Ripple;
            internal float SharedStartHeight, Branch1StretchLimit, Branch2StretchLimit;
            internal bool DoShared, DoBranch1, DoBranch2, DoRipple, DoShimmer;
        }

        readonly byte[] m_Data;

        SpeedTree9File(byte[] data) => m_Data = data;

        /// <summary>Whether the bytes start with the SpeedTree 9 signature.</summary>
        internal static bool HasSignature(byte[] data)
        {
            if (data.Length < Signature.Length) return false;
            for (int i = 0; i < Signature.Length; i++)
                if (data[i] != Signature[i]) return false;
            return true;
        }

        /// <summary>Reads a file whose signature <see cref="HasSignature"/> accepted; a table or entry
        /// outside the file throws <see cref="FormatException"/>.</summary>
        internal static SpeedTree9File Read(byte[] data)
        {
            var file = new SpeedTree9File(data);
            try
            {
                file.ReadRoot(Signature.Length);
            }
            catch (Exception e) when (e is ArgumentOutOfRangeException || e is IndexOutOfRangeException || e is OverflowException)
            {
                throw new FormatException("the SpeedTree 9 file is truncated or damaged", e);
            }
            return file;
        }

        void ReadRoot(int root)
        {
            VersionMajor = UInt(Entry(root, RootVersionMajor));
            VersionMinor = UInt(Entry(root, RootVersionMinor));
            int bounds = Entry(root, RootBounds);
            BoundsMin = Vec3(bounds);
            BoundsMax = Vec3(bounds + 12);
            Lods = Tables(Entry(root, RootLods), ReadLod);
            LastLodIsBillboard = UInt(Entry(Entry(root, RootBillboard), 0)) != 0;
            Collisions = Tables(Entry(root, RootCollisions), at => new Collision
            {
                Position = Vec3(Entry(at, 0)),
                Position2 = Vec3(Entry(at, 1)),
                Radius = Float(Entry(at, 2)),
            });
            Materials = Tables(Entry(root, RootMaterials), ReadMaterial);
            WindConfig = ReadWind(Entry(root, RootWind));
        }

        Lod ReadLod(int at)
        {
            int vertices = Entry(at, 0), indices = Entry(at, 1), draws = Entry(at, 2);
            var lod = new Lod
            {
                Vertices = new Vertex[Count(vertices, VertexSize)],
                Indices = new uint[Count(indices, 4)],
                DrawCalls = new DrawCall[Count(draws, DrawCallSize)],
            };
            for (int i = 0; i < lod.Vertices.Length; i++) lod.Vertices[i] = ReadVertex(vertices + 4 + i * VertexSize);
            for (int i = 0; i < lod.Indices.Length; i++) lod.Indices[i] = UInt(indices + 4 + i * 4);
            for (int i = 0; i < lod.DrawCalls.Length; i++)
            {
                int d = draws + 4 + i * DrawCallSize;
                lod.DrawCalls[i] = new DrawCall
                {
                    MaterialIndex = UInt(d),
                    // A one-byte boolean padded to four bytes.
                    ContainsFacingGeometry = m_Data[d + 4] != 0,
                    IndexStart = UInt(d + 8),
                    IndexCount = UInt(d + 12),
                };
            }
            return lod;
        }

        Vertex ReadVertex(int at) => new Vertex
        {
            Anchor = Vec3(at),
            Offset = Vec3(at + 12),
            // at + 24: the LOD offset, unused by the importer.
            Normal = Vec3(at + 36),
            Tangent = Vec3(at + 48),
            Binormal = Vec3(at + 60),
            U = Float(at + 72),
            V = Float(at + 76),
            LightmapU = Float(at + 80),
            LightmapV = Float(at + 84),
            Color = Vec3(at + 88),
            AmbientOcclusion = Float(at + 100),
            BlendWeight = Float(at + 104),
            BranchWind1 = Vec3(at + 108),
            BranchWind2 = Vec3(at + 120),
            RippleWeight = Float(at + 132),
            CameraFacing = m_Data[at + 136] != 0,
        };

        Material ReadMaterial(int at) => new Material
        {
            Name = String(Entry(at, 0)),
            TwoSided = UInt(Entry(at, 1)) != 0,
            FlipNormalsOnBackside = UInt(Entry(at, 2)) != 0,
            Billboard = UInt(Entry(at, 3)) != 0,
            Maps = Tables(Entry(at, 4), map =>
            {
                int color = Entry(map, 2);
                return new Map
                {
                    Used = UInt(Entry(map, 0)) != 0,
                    Path = String(Entry(map, 1)),
                    R = Float(color), G = Float(color + 4), B = Float(color + 8), A = Float(color + 12),
                };
            }),
        };

        Wind ReadWind(int at)
        {
            int common = Entry(at, 0);
            return new Wind
            {
                StrengthResponse = Float(Entry(common, 0)),
                DirectionResponse = Float(Entry(common, 1)),
                GustFrequency = Float(Entry(common, 5)),
                GustStrengthMin = Float(Entry(common, 6)),
                GustStrengthMax = Float(Entry(common, 7)),
                GustDurationMin = Float(Entry(common, 8)),
                GustDurationMax = Float(Entry(common, 9)),
                GustRiseScalar = Float(Entry(common, 10)),
                GustFallScalar = Float(Entry(common, 11)),
                Shared = ReadBranch(Entry(at, 1)),
                Branch1 = ReadBranch(Entry(at, 2)),
                Branch2 = ReadBranch(Entry(at, 3)),
                Ripple = ReadRipple(Entry(at, 4)),
                SharedStartHeight = Float(Entry(at, 10)),
                Branch1StretchLimit = Float(Entry(at, 11)),
                Branch2StretchLimit = Float(Entry(at, 12)),
                DoShared = UInt(Entry(at, 15)) != 0,
                DoBranch1 = UInt(Entry(at, 16)) != 0,
                DoBranch2 = UInt(Entry(at, 17)) != 0,
                DoRipple = UInt(Entry(at, 18)) != 0,
                DoShimmer = UInt(Entry(at, 19)) != 0,
            };
        }

        Branch ReadBranch(int at) => new Branch
        {
            Bend = Floats(Entry(at, 0)),
            Oscillation = Floats(Entry(at, 1)),
            Speed = Floats(Entry(at, 2)),
            Turbulence = Floats(Entry(at, 3)),
            Flexibility = Floats(Entry(at, 4)),
            Independence = Float(Entry(at, 5)),
        };

        Ripple ReadRipple(int at) => new Ripple
        {
            Planar = Floats(Entry(at, 0)),
            Directional = Floats(Entry(at, 1)),
            Speed = Floats(Entry(at, 2)),
            Flexibility = Floats(Entry(at, 3)),
            Shimmer = Float(Entry(at, 4)),
            Independence = Float(Entry(at, 5)),
        };

        /// <summary>The absolute offset of entry <paramref name="index"/> of the table at <paramref name="table"/>.</summary>
        int Entry(int table, int index) => table + (int)UInt(table + (index + 1) * 4);

        /// <summary>The count at <paramref name="at"/> of items of <paramref name="itemSize"/> bytes
        /// (offsets for a table) that follow it, which must fit in the file.</summary>
        int Count(int at, int itemSize = 4)
        {
            long count = UInt(at);
            if (at + 4 + count * itemSize > m_Data.Length) throw new OverflowException();
            return (int)count;
        }

        T[] Tables<T>(int table, Func<int, T> read)
        {
            var items = new T[Count(table)];
            for (int i = 0; i < items.Length; i++) items[i] = read(Entry(table, i));
            return items;
        }

        float[] Floats(int at)
        {
            var values = new float[Count(at)];
            for (int i = 0; i < values.Length; i++) values[i] = Float(at + 4 + i * 4);
            return values;
        }

        uint UInt(int at) => BinaryPrimitives.ReadUInt32LittleEndian(m_Data.AsSpan(at, 4));
        float Float(int at) => BinaryPrimitives.ReadSingleLittleEndian(m_Data.AsSpan(at, 4));
        Vector3f Vec3(int at) => new Vector3f { X = Float(at), Y = Float(at + 4), Z = Float(at + 8) };

        string String(int at)
        {
            int length = Count(at, 1);
            return length <= 1 ? "" : Encoding.UTF8.GetString(m_Data, at + 4, length - 1);
        }
    }
}
