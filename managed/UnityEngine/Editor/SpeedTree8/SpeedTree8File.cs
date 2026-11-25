using System;

namespace Unbound.SpeedTree
{
    /// <summary>
    /// A SpeedTree 8 (<c>.st</c>) file: the signature <c>SpeedTree___</c>, then the table container
    /// of SpeedTree 9 (<see cref="SpeedTreeTables"/>). The root table holds the version, whether the
    /// last LOD is a billboard, the LODs, the collision objects, the materials and the wind; the
    /// entries after the wind (bones, and in version 10 a table of file properties) and the uint32
    /// entry before it are not read. Booleans take one byte in version 8 files and four in version
    /// 10 files; only their first byte counts. The layout is the one of the measured files, versions
    /// 8.0 and 10.0, which the Unity 6000.0.71f1 Editor imports as SpeedTree8FileTests checks;
    /// other versions are refused.
    /// </summary>
    internal sealed class SpeedTree8File
    {
        internal const string Signature = "SpeedTree___";

        // Root entries.
        const int RootVersionMajor = 0, RootVersionMinor = 1, RootLastLodIsBillboard = 2, RootLods = 3,
            RootCollisions = 4, RootMaterials = 5, RootWind = 7;

        /// <summary>One vertex as the file stores it (35 four-byte words).</summary>
        internal const int VertexSize = 140;
        const int DrawCallSize = 16;
        /// <summary>The wind entry: a uint32, 319 floats and the 28 options (one byte each).</summary>
        internal const int WindSize = 1308;

        /// <summary>The (major, minor) versions whose layout is measured.</summary>
        static readonly (uint Major, uint Minor)[] s_MeasuredVersions = { (8, 0), (10, 0) };

        internal uint VersionMajor, VersionMinor;
        internal bool LastLodIsBillboard;
        internal Lod[] Lods;
        internal Collision[] Collisions;
        internal Material[] Materials;
        internal Wind WindConfig;

        internal struct Vertex
        {
            internal SpeedTreeVector3 Anchor, Offset, LodOffset, Normal, Tangent, Binormal, Color;
            internal float U, V, LightmapU, LightmapV;
            /// <summary>The branch wind data a mesh keeps in uv0.zw.</summary>
            internal float BranchWindX, BranchWindY;
            /// <summary>The leaf and frond wind data a mesh keeps in uv3.xyz.</summary>
            internal SpeedTreeVector3 LeafWind;
            internal float AmbientOcclusion, BlendWeight;
        }

        /// <summary>The kind of geometry of a draw call. Branches, leaves, camera-facing leaves and
        /// billboards are measured; the others are the numbers the SpeedTree 8 shaders give them.</summary>
        internal enum GeometryType : uint
        {
            Branch = 0,
            Frond = 1,
            Leaf = 2,
            FacingLeaf = 3,
            Billboard = 4,
        }

        internal struct DrawCall
        {
            internal uint MaterialIndex, IndexStart, IndexCount;
            internal GeometryType GeometryType;
        }

        internal sealed class Lod
        {
            internal Vertex[] Vertices;
            internal uint[] Indices;
            internal DrawCall[] DrawCalls;
        }

        internal sealed class Collision
        {
            internal SpeedTreeVector3 Position, Position2;
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
            internal bool TwoSided, Billboard;
            internal Map[] Maps;
        }

        /// <summary>The number of samples of a wind curve.</summary>
        internal const int CurveSamples = 10;

        internal sealed class BranchLevel
        {
            internal float[] Distance, DirectionAdherence, Whip;
            internal float Turbulence, Twitch, TwitchFreqScale;
        }

        internal sealed class LeafGroup
        {
            internal float[] RippleDistance, TumbleFlip, TumbleTwist, TumbleDirectionAdherence, TwitchThrow;
            internal float TwitchSharpness, LeewardScalar;
        }

        /// <summary>The wind options, in the file's order.</summary>
        internal enum WindOption
        {
            GlobalWind, GlobalPreserveShape,
            BranchSimple1, BranchDirectional1, BranchDirectionalFrond1, BranchTurbulence1, BranchWhip1, BranchOscComplex1,
            BranchSimple2, BranchDirectional2, BranchDirectionalFrond2, BranchTurbulence2, BranchWhip2, BranchOscComplex2,
            LeafRippleVertexNormal1, LeafRippleComputed1, LeafTumble1, LeafTwitch1, LeafOcclusion1,
            LeafRippleVertexNormal2, LeafRippleComputed2, LeafTumble2, LeafTwitch2, LeafOcclusion2,
            FrondRippleOneSided, FrondRippleTwoSided, FrondRippleAdjustLighting, Rolling,
        }

        internal const int WindOptionCount = (int)WindOption.Rolling + 1;

        /// <summary>The wind as the file stores it, in the file's units (distances unconverted).</summary>
        internal sealed class Wind
        {
            /// <summary>The leading uint32 (4 in every measured file), which nothing here uses.</summary>
            internal uint Leading;
            internal float StrengthResponse, DirectionResponse, AnchorOffset, AnchorDistanceScale,
                GustFrequency, GustStrengthMin, GustStrengthMax, GustDurationMin, GustDurationMax, GustRiseScalar, GustFallScalar;
            /// <summary>Ten oscillation curves.</summary>
            internal float[][] Oscillation;
            internal float GlobalHeight, GlobalHeightExponent;
            internal float[] GlobalDistance, GlobalDirectionAdherence;
            internal BranchLevel[] BranchLevels;
            internal SpeedTreeVector3 BranchWindAnchor;
            internal float MaxBranchLevel1Length;
            internal LeafGroup[] LeafGroups;
            internal float[] FrondRippleDistance;
            internal float FrondRippleTile, FrondRippleLightingScalar;
            internal bool[] Options;

            internal bool this[WindOption option] => Options[(int)option];
        }

        readonly SpeedTreeTables m_Tables;

        SpeedTree8File(byte[] data) => m_Tables = new SpeedTreeTables(data);

        /// <summary>Whether the bytes start with the SpeedTree 8 signature.</summary>
        internal static bool HasSignature(byte[] data) => SpeedTreeTables.HasSignature(data, Signature);

        /// <summary>Reads a file whose signature <see cref="HasSignature"/> accepted. A table or entry
        /// outside the file throws <see cref="FormatException"/>; a version whose layout is not
        /// measured throws <see cref="NotSupportedException"/>.</summary>
        internal static SpeedTree8File Read(byte[] data)
        {
            var file = new SpeedTree8File(data);
            return SpeedTreeTables.Guard("SpeedTree 8 file", () =>
            {
                file.ReadRoot(Signature.Length);
                return file;
            });
        }

        void ReadRoot(int root)
        {
            VersionMajor = m_Tables.UInt(m_Tables.Entry(root, RootVersionMajor));
            VersionMinor = m_Tables.UInt(m_Tables.Entry(root, RootVersionMinor));
            if (Array.IndexOf(s_MeasuredVersions, (VersionMajor, VersionMinor)) < 0)
                throw new NotSupportedException($"SpeedTree 8 file version {VersionMajor}.{VersionMinor} is not supported (the supported versions are 8.0 and 10.0)");
            LastLodIsBillboard = Bool(m_Tables.Entry(root, RootLastLodIsBillboard));
            Lods = m_Tables.Tables(m_Tables.Entry(root, RootLods), ReadLod);
            Collisions = m_Tables.Tables(m_Tables.Entry(root, RootCollisions), at => new Collision
            {
                Position = m_Tables.Vec3(m_Tables.Entry(at, 0)),
                Position2 = m_Tables.Vec3(m_Tables.Entry(at, 1)),
                Radius = m_Tables.Float(m_Tables.Entry(at, 2)),
            });
            Materials = m_Tables.Tables(m_Tables.Entry(root, RootMaterials), ReadMaterial);
            WindConfig = ReadWind(m_Tables.Entry(root, RootWind));
        }

        bool Bool(int at) => m_Tables.Byte(at) != 0;

        Lod ReadLod(int at)
        {
            int vertices = m_Tables.Entry(at, 0), indices = m_Tables.Entry(at, 1), draws = m_Tables.Entry(at, 2);
            var lod = new Lod
            {
                Vertices = new Vertex[m_Tables.Count(vertices, VertexSize)],
                Indices = new uint[m_Tables.Count(indices, 4)],
                DrawCalls = new DrawCall[m_Tables.Count(draws, DrawCallSize)],
            };
            for (int i = 0; i < lod.Vertices.Length; i++) lod.Vertices[i] = ReadVertex(vertices + 4 + i * VertexSize);
            for (int i = 0; i < lod.Indices.Length; i++) lod.Indices[i] = m_Tables.UInt(indices + 4 + i * 4);
            for (int i = 0; i < lod.DrawCalls.Length; i++)
            {
                int d = draws + 4 + i * DrawCallSize;
                lod.DrawCalls[i] = new DrawCall
                {
                    MaterialIndex = m_Tables.UInt(d),
                    GeometryType = (GeometryType)m_Tables.UInt(d + 4),
                    IndexStart = m_Tables.UInt(d + 8),
                    IndexCount = m_Tables.UInt(d + 12),
                };
            }
            return lod;
        }

        Vertex ReadVertex(int at) => new Vertex
        {
            Anchor = m_Tables.Vec3(at),
            Offset = m_Tables.Vec3(at + 12),
            LodOffset = m_Tables.Vec3(at + 24),
            Normal = m_Tables.Vec3(at + 36),
            Tangent = m_Tables.Vec3(at + 48),
            Binormal = m_Tables.Vec3(at + 60),
            U = m_Tables.Float(at + 72),
            V = m_Tables.Float(at + 76),
            LightmapU = m_Tables.Float(at + 80),
            LightmapV = m_Tables.Float(at + 84),
            Color = m_Tables.Vec3(at + 88),
            BranchWindX = m_Tables.Float(at + 100),
            BranchWindY = m_Tables.Float(at + 104),
            LeafWind = m_Tables.Vec3(at + 108),
            // at + 120: a word the importer does not use (its contents vary with the geometry).
            AmbientOcclusion = m_Tables.Float(at + 124),
            BlendWeight = m_Tables.Float(at + 128),
            // at + 132 and at + 136: unused by the importer.
        };

        Material ReadMaterial(int at) => new Material
        {
            Name = m_Tables.String(m_Tables.Entry(at, 0)),
            TwoSided = Bool(m_Tables.Entry(at, 1)),
            Billboard = Bool(m_Tables.Entry(at, 2)),
            Maps = m_Tables.Tables(m_Tables.Entry(at, 3), map =>
            {
                int color = m_Tables.Entry(map, 2);
                return new Map
                {
                    Used = Bool(m_Tables.Entry(map, 0)),
                    Path = m_Tables.String(m_Tables.Entry(map, 1)),
                    R = m_Tables.Float(color), G = m_Tables.Float(color + 4), B = m_Tables.Float(color + 8), A = m_Tables.Float(color + 12),
                };
            }),
        };

        Wind ReadWind(int at)
        {
            if (at + WindSize > m_Tables.Length) throw new OverflowException();
            int p = at;
            float Next() { float v = m_Tables.Float(p); p += 4; return v; }
            float[] Curve()
            {
                var c = new float[CurveSamples];
                for (int i = 0; i < c.Length; i++) c[i] = Next();
                return c;
            }
            var wind = new Wind { Leading = m_Tables.UInt(p) };
            p += 4;
            wind.StrengthResponse = Next();
            wind.DirectionResponse = Next();
            wind.AnchorOffset = Next();
            wind.AnchorDistanceScale = Next();
            wind.GustFrequency = Next();
            wind.GustStrengthMin = Next();
            wind.GustStrengthMax = Next();
            wind.GustDurationMin = Next();
            wind.GustDurationMax = Next();
            wind.GustRiseScalar = Next();
            wind.GustFallScalar = Next();
            wind.Oscillation = new float[CurveSamples][];
            for (int i = 0; i < wind.Oscillation.Length; i++) wind.Oscillation[i] = Curve();
            wind.GlobalHeight = Next();
            wind.GlobalHeightExponent = Next();
            wind.GlobalDistance = Curve();
            wind.GlobalDirectionAdherence = Curve();
            wind.BranchLevels = new BranchLevel[2];
            for (int i = 0; i < wind.BranchLevels.Length; i++)
                wind.BranchLevels[i] = new BranchLevel
                {
                    Distance = Curve(),
                    DirectionAdherence = Curve(),
                    Whip = Curve(),
                    Turbulence = Next(),
                    Twitch = Next(),
                    TwitchFreqScale = Next(),
                };
            wind.BranchWindAnchor = new SpeedTreeVector3 { X = Next(), Y = Next(), Z = Next() };
            wind.MaxBranchLevel1Length = Next();
            wind.LeafGroups = new LeafGroup[2];
            for (int i = 0; i < wind.LeafGroups.Length; i++)
                wind.LeafGroups[i] = new LeafGroup
                {
                    RippleDistance = Curve(),
                    TumbleFlip = Curve(),
                    TumbleTwist = Curve(),
                    TumbleDirectionAdherence = Curve(),
                    TwitchThrow = Curve(),
                    TwitchSharpness = Next(),
                    LeewardScalar = Next(),
                };
            wind.FrondRippleDistance = Curve();
            wind.FrondRippleTile = Next();
            wind.FrondRippleLightingScalar = Next();
            wind.Options = new bool[WindOptionCount];
            for (int i = 0; i < wind.Options.Length; i++) wind.Options[i] = m_Tables.Byte(p++) != 0;
            return wind;
        }
    }
}
