using System;
using System.Collections.Generic;
using System.Text.Json;
using Unbound.Runtime;
using UnityEngine.Rendering;

namespace UnityEngine
{
    /// <summary>A lightmap: its color, directional and shadowmask textures.</summary>
    public sealed partial class LightmapData
    {
        internal Texture2D m_Light;
        internal Texture2D m_Dir;
        internal Texture2D m_ShadowMask;

        public LightmapData() { }

        [Obsolete("Use lightmapColor property (UnityUpgradable) -> lightmapColor")]
        public Texture2D lightmapLight { get => m_Light; set => m_Light = value; }
        public Texture2D lightmapColor { get => m_Light; set => m_Light = value; }
        public Texture2D lightmapDir { get => m_Dir; set => m_Dir = value; }
        public Texture2D shadowMask { get => m_ShadowMask; set => m_ShadowMask = value; }
        [Obsolete("Property LightmapData.lightmap has been deprecated. Use LightmapData.lightmapColor instead (UnityUpgradable) -> lightmapColor")]
        public Texture2D lightmap { get => m_Light; set => m_Light = value; }
        [Obsolete("Property LightmapData.lightmapFar has been deprecated. Use LightmapData.lightmapColor instead (UnityUpgradable) -> lightmapColor")]
        public Texture2D lightmapFar { get => m_Light; set => m_Light = value; }
        [Obsolete("Property LightmapData.lightmapNear has been deprecated. Use LightmapData.lightmapDir instead (UnityUpgradable) -> lightmapDir")]
        public Texture2D lightmapNear { get => m_Dir; set => m_Dir = value; }

        internal LightmapData Copy() => new LightmapData { m_Light = m_Light, m_Dir = m_Dir, m_ShadowMask = m_ShadowMask };
    }

    /// <summary>The loaded scenes' lightmaps and light probes. The getter hands out copies, the
    /// setter takes a copy, as Unity's do.</summary>
    public sealed partial class LightmapSettings : Object
    {
        static LightmapData[] s_Lightmaps = Array.Empty<LightmapData>();
        static LightmapsMode s_Mode = LightmapsMode.CombinedDirectional;
        static LightProbes s_LightProbes;

        public static LightmapData[] lightmaps
        {
            get => Array.ConvertAll(s_Lightmaps, l => l?.Copy());
            set => s_Lightmaps = value == null ? Array.Empty<LightmapData>() : Array.ConvertAll(value, l => l?.Copy());
        }

        public static LightmapsMode lightmapsMode { get => s_Mode; set => s_Mode = value; }

        public static LightProbes lightProbes
        {
            get => s_LightProbes;
            set
            {
                s_LightProbes = value;
                LightProbes.RaiseUpdated();
            }
        }

        internal static void Reset()
        {
            s_Lightmaps = Array.Empty<LightmapData>();
            s_Mode = LightmapsMode.CombinedDirectional;
            s_LightProbes = null;
        }

        /// <summary>The lightmap a renderer draws with (no copy), or null.</summary>
        internal static LightmapData Of(int index) =>
            index >= 0 && index < s_Lightmaps.Length ? s_Lightmaps[index] : null;

        internal static int Count => s_Lightmaps.Length;

        internal static void Append(LightmapData[] maps)
        {
            var all = new LightmapData[s_Lightmaps.Length + maps.Length];
            s_Lightmaps.CopyTo(all, 0);
            maps.CopyTo(all, s_Lightmaps.Length);
            s_Lightmaps = all;
        }
    }

    /// <summary>
    /// Baked light probes and the tetrahedralization that interpolates them: a point inside the
    /// probes' hull blends the four probes of its tetrahedron by barycentric weights; a point
    /// outside blends the three of the hull triangle that, moved along its probes' hull rays,
    /// passes through it (Unity's scheme, Cupisz, "Light probe interpolation using tetrahedral
    /// tessellations", GDC 2012).
    /// </summary>
    public sealed partial class LightProbes : Object
    {
        internal struct Cell
        {
            internal int I0, I1, I2, I3;
            internal int N0, N1, N2, N3;
            /// <summary>Inner cells: rows of the matrix taking a point less the fourth probe to
            /// the first three barycentric coordinates.</summary>
            internal Vector3 R0, R1, R2;
        }

        internal Vector3[] m_Positions = Array.Empty<Vector3>();
        internal SphericalHarmonicsL2[] m_Probes = Array.Empty<SphericalHarmonicsL2>();
        internal Cell[] m_Cells = Array.Empty<Cell>();
        internal Vector3[] m_HullRays = Array.Empty<Vector3>();

        LightProbes() { }

        public Vector3[] positions => (Vector3[])m_Positions.Clone();
        public int count => m_Positions.Length;
        public int countSelf => m_Positions.Length;
        public int cellCount => m_Cells.Length;
        public int cellCountSelf => m_Cells.Length;

        public SphericalHarmonicsL2[] bakedProbes
        {
            get => (SphericalHarmonicsL2[])m_Probes.Clone();
            set
            {
                if (value == null || value.Length != m_Positions.Length)
                {
                    Debug.LogError($"Coefficients array must have probeCount ({m_Positions.Length}) elements.");
                    return;
                }
                m_Probes = (SphericalHarmonicsL2[])value.Clone();
            }
        }

        [Obsolete("Use bakedProbes instead.")]
        public float[] coefficients
        {
            get
            {
                var flat = new float[m_Probes.Length * 27];
                for (int i = 0; i < m_Probes.Length; i++)
                    for (int k = 0; k < 27; k++) flat[i * 27 + k] = m_Probes[i][k / 9, k % 9];
                return flat;
            }
            set
            {
                if (value == null || value.Length != m_Probes.Length * 27)
                {
                    Debug.LogError($"Coefficients array must have 27*probeCount ({m_Probes.Length * 27}) elements.");
                    return;
                }
                for (int i = 0; i < m_Probes.Length; i++)
                    for (int k = 0; k < 27; k++) m_Probes[i][k / 9, k % 9] = value[i * 27 + k];
            }
        }

        public Vector3[] GetPositionsSelf() => positions;

        public static event Action lightProbesUpdated;
        public static event Action tetrahedralizationCompleted;
        public static event Action needsRetetrahedralization;

        internal static void RaiseUpdated() => lightProbesUpdated?.Invoke();

        /// <summary>The loaded scenes' probes are tetrahedralized when baked; Unbound holds one
        /// scene's set, so there is nothing to merge.</summary>
        public static void Tetrahedralize() => tetrahedralizationCompleted?.Invoke();
        public static void TetrahedralizeAsync() => tetrahedralizationCompleted?.Invoke();

        public static LightProbes GetSharedLightProbesForScene(SceneManagement.Scene scene) => LightmapSettings.lightProbes;
        public static LightProbes GetInstantiatedLightProbesForScene(SceneManagement.Scene scene) => LightmapSettings.lightProbes;

        /// <summary>The lighting at a point: the scene's probes interpolated, or the ambient probe
        /// when there are none.</summary>
        public static void GetInterpolatedProbe(Vector3 position, Renderer renderer, out SphericalHarmonicsL2 probe)
        {
            var probes = LightmapSettings.lightProbes;
            int hint = renderer != null ? renderer.m_ProbeCell : -1;
            if (probes == null || !probes.Interpolate(position, ref hint, out probe))
            {
                probe = RenderSettings.ambientProbe;
                return;
            }
            if (renderer != null) renderer.m_ProbeCell = hint;
        }

        public static void CalculateInterpolatedLightAndOcclusionProbes(Vector3[] positions, SphericalHarmonicsL2[] lightProbes, Vector4[] occlusionProbes)
        {
            if (positions == null) throw new ArgumentNullException(nameof(positions));
            if (lightProbes != null && lightProbes.Length < positions.Length) throw new ArgumentException("lightProbes is smaller than positions.", nameof(lightProbes));
            if (occlusionProbes != null && occlusionProbes.Length < positions.Length) throw new ArgumentException("occlusionProbes is smaller than positions.", nameof(occlusionProbes));
            for (int i = 0; i < positions.Length; i++)
            {
                GetInterpolatedProbe(positions[i], null, out var sh);
                if (lightProbes != null) lightProbes[i] = sh;
                if (occlusionProbes != null) occlusionProbes[i] = Vector4.one;
            }
        }

        public static void CalculateInterpolatedLightAndOcclusionProbes(List<Vector3> positions, List<SphericalHarmonicsL2> lightProbes, List<Vector4> occlusionProbes)
        {
            if (positions == null) throw new ArgumentNullException(nameof(positions));
            lightProbes?.Clear();
            occlusionProbes?.Clear();
            foreach (var p in positions)
            {
                GetInterpolatedProbe(p, null, out var sh);
                lightProbes?.Add(sh);
                occlusionProbes?.Add(Vector4.one);
            }
        }

        [Obsolete("Use GetInterpolatedProbe instead.")]
        public void GetInterpolatedLightProbe(Vector3 position, Renderer renderer, float[] coefficients)
        {
            if (coefficients == null || coefficients.Length < 27) throw new ArgumentException("Coefficients array must have 27 elements.", nameof(coefficients));
            int hint = -1;
            if (!Interpolate(position, ref hint, out var sh)) sh = RenderSettings.ambientProbe;
            for (int k = 0; k < 27; k++) coefficients[k] = sh[k / 9, k % 9];
        }

        const float Epsilon = -1e-4f;

        /// <summary>The probes blended at <paramref name="p"/>, walking the cells from
        /// <paramref name="cell"/> (a renderer's last cell) toward it; false without probes.</summary>
        internal bool Interpolate(Vector3 p, ref int cell, out SphericalHarmonicsL2 sh)
        {
            sh = default;
            if (m_Probes.Length == 0) return false;
            if (m_Cells.Length == 0)
            {
                sh = m_Probes[Nearest(p)];
                return true;
            }
            if (cell < 0 || cell >= m_Cells.Length) cell = 0;
            Span<float> w = stackalloc float[4];
            for (int steps = 0; steps < m_Cells.Length + 4; steps++)
            {
                int next = Step(p, cell, w);
                if (next == cell)
                {
                    Blend(cell, w, out sh);
                    return true;
                }
                if (next < 0) break;
                cell = next;
            }
            // The walk cycled (degenerate cells): try each cell.
            for (int c = 0; c < m_Cells.Length; c++)
            {
                if (Step(p, c, w) != c) continue;
                cell = c;
                Blend(c, w, out sh);
                return true;
            }
            sh = m_Probes[Nearest(p)];
            return true;
        }

        int Nearest(Vector3 p)
        {
            int best = 0;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < m_Positions.Length; i++)
            {
                float d = (m_Positions[i] - p).sqrMagnitude;
                if (d < bestDistance) { bestDistance = d; best = i; }
            }
            return best;
        }

        void Blend(int cell, ReadOnlySpan<float> w, out SphericalHarmonicsL2 sh)
        {
            var c = m_Cells[cell];
            sh = m_Probes[c.I0] * w[0] + m_Probes[c.I1] * w[1] + m_Probes[c.I2] * w[2];
            if (c.I3 >= 0) sh += m_Probes[c.I3] * w[3];
        }

        /// <summary>The weights of <paramref name="p"/> in a cell: the cell itself when they are
        /// all non-negative, else the neighbor across the face of the most negative one.</summary>
        int Step(Vector3 p, int index, Span<float> w)
        {
            var c = m_Cells[index];
            if (c.I3 >= 0)
            {
                var d = p - m_Positions[c.I3];
                w[0] = Vector3.Dot(c.R0, d);
                w[1] = Vector3.Dot(c.R1, d);
                w[2] = Vector3.Dot(c.R2, d);
                w[3] = 1f - w[0] - w[1] - w[2];
                int worst = 0;
                for (int k = 1; k < 4; k++) if (w[k] < w[worst]) worst = k;
                if (w[worst] >= Epsilon) return index;
                return worst switch { 0 => c.N0, 1 => c.N1, 2 => c.N2, _ => c.N3 };
            }
            // An outer cell: the hull triangle, its probes moved along their rays.
            Vector3 a = m_Positions[c.I0], b = m_Positions[c.I1], e = m_Positions[c.I2];
            Vector3 ra = Ray(c.I0), rb = Ray(c.I1), re = Ray(c.I2);
            var normal = Vector3.Cross(b - a, e - a);
            if (Vector3.Dot(normal, ra + rb + re) < 0f) normal = -normal;
            // Inside the hull: the inner cell across the triangle.
            if (Vector3.Dot(normal, p - a) < 0f) return c.N3;
            float t = RayParameter(p, a, b, e, ra, rb, re);
            Vector3 ta = a + ra * t, tb = b + rb * t, te = e + re * t;
            Barycentric(p, ta, tb, te, out w[0], out w[1], out w[2]);
            w[3] = 0f;
            int least = 0;
            for (int k = 1; k < 3; k++) if (w[k] < w[least]) least = k;
            if (w[least] >= Epsilon) return index;
            return least switch { 0 => c.N0, 1 => c.N1, _ => c.N2 };
        }

        Vector3 Ray(int probe) => probe < m_HullRays.Length ? m_HullRays[probe] : Vector3.zero;

        /// <summary>The smallest t ≥ 0 at which the triangle (a + t·ra, b + t·rb, c + t·rc)
        /// passes through p: a root of the cubic det(b(t) − a(t), c(t) − a(t), p − a(t)).</summary>
        static float RayParameter(Vector3 p, Vector3 a, Vector3 b, Vector3 c, Vector3 ra, Vector3 rb, Vector3 rc)
        {
            // u = u0 + t·u1, v = v0 + t·v1, w = w0 + t·w1; det = u · (v × w).
            Vector3 u0 = b - a, u1 = rb - ra, v0 = c - a, v1 = rc - ra, w0 = p - a, w1 = -ra;
            Vector3 x0 = Vector3.Cross(v0, w0);
            Vector3 x1 = Vector3.Cross(v0, w1) + Vector3.Cross(v1, w0);
            Vector3 x2 = Vector3.Cross(v1, w1);
            double k0 = Vector3.Dot(u0, x0);
            double k1 = Vector3.Dot(u0, x1) + Vector3.Dot(u1, x0);
            double k2 = Vector3.Dot(u0, x2) + Vector3.Dot(u1, x1);
            double k3 = Vector3.Dot(u1, x2);
            double best = double.MaxValue;
            foreach (var root in CubicRoots(k3, k2, k1, k0))
                if (root >= -1e-6 && root < best) best = root;
            return best == double.MaxValue ? 0f : (float)Math.Max(best, 0.0);
        }

        static IEnumerable<double> CubicRoots(double a, double b, double c, double d)
        {
            double scale = Math.Max(Math.Max(Math.Abs(a), Math.Abs(b)), Math.Max(Math.Abs(c), Math.Abs(d)));
            if (scale == 0) yield break;
            if (Math.Abs(a) < 1e-9 * scale)
            {
                if (Math.Abs(b) < 1e-9 * scale)
                {
                    if (Math.Abs(c) > 1e-12 * scale) yield return -d / c;
                    yield break;
                }
                double disc = c * c - 4 * b * d;
                if (disc < 0) yield break;
                double s = Math.Sqrt(disc);
                yield return (-c - s) / (2 * b);
                yield return (-c + s) / (2 * b);
                yield break;
            }
            // Depressed cubic t³ + pt + q by t = x − b/3a.
            double bn = b / a, cn = c / a, dn = d / a;
            double p = cn - bn * bn / 3, q = 2 * bn * bn * bn / 27 - bn * cn / 3 + dn;
            double shift = -bn / 3;
            double disc3 = q * q / 4 + p * p * p / 27;
            if (disc3 > 0)
            {
                double s = Math.Sqrt(disc3);
                yield return Math.Cbrt(-q / 2 + s) + Math.Cbrt(-q / 2 - s) + shift;
            }
            else if (p == 0)
            {
                yield return shift;
            }
            else
            {
                double r = 2 * Math.Sqrt(-p / 3);
                double phi = Math.Acos(Math.Clamp(3 * q / (p * r), -1, 1)) / 3;
                for (int k = 0; k < 3; k++) yield return r * Math.Cos(phi - 2 * Math.PI * k / 3) + shift;
            }
        }

        static void Barycentric(Vector3 p, Vector3 a, Vector3 b, Vector3 c, out float wa, out float wb, out float wc)
        {
            Vector3 v0 = b - a, v1 = c - a, v2 = p - a;
            float d00 = Vector3.Dot(v0, v0), d01 = Vector3.Dot(v0, v1), d11 = Vector3.Dot(v1, v1);
            float d20 = Vector3.Dot(v2, v0), d21 = Vector3.Dot(v2, v1);
            float denominator = d00 * d11 - d01 * d01;
            if (Mathf.Abs(denominator) < 1e-12f)
            {
                wa = 1f; wb = wc = 0f;
                return;
            }
            wb = (d11 * d20 - d01 * d21) / denominator;
            wc = (d00 * d21 - d01 * d20) / denominator;
            wa = 1f - wb - wc;
        }

        internal static LightProbes FromJson(JsonElement json)
        {
            var probes = new LightProbes { name = "LightProbes" };
            var positions = Floats(json.GetProperty("positions"));
            var coefficients = Floats(json.GetProperty("coefficients"));
            int count = positions.Length / 3;
            probes.m_Positions = new Vector3[count];
            probes.m_Probes = new SphericalHarmonicsL2[count];
            for (int i = 0; i < count; i++)
            {
                probes.m_Positions[i] = new Vector3(positions[3 * i], positions[3 * i + 1], positions[3 * i + 2]);
                for (int k = 0; k < 27; k++) probes.m_Probes[i][k / 9, k % 9] = coefficients[27 * i + k];
            }
            var rays = Floats(json.GetProperty("hullRays"));
            probes.m_HullRays = new Vector3[rays.Length / 3];
            for (int i = 0; i < probes.m_HullRays.Length; i++)
                probes.m_HullRays[i] = new Vector3(rays[3 * i], rays[3 * i + 1], rays[3 * i + 2]);
            var cells = new List<Cell>();
            foreach (var t in json.GetProperty("tetrahedra").EnumerateArray())
            {
                var idx = Ints(t.GetProperty("indices"));
                var n = Ints(t.GetProperty("neighbors"));
                var m = Floats(t.GetProperty("matrix"));
                cells.Add(new Cell
                {
                    I0 = idx[0], I1 = idx[1], I2 = idx[2], I3 = idx[3],
                    N0 = n[0], N1 = n[1], N2 = n[2], N3 = n[3],
                    R0 = new Vector3(m[0], m[1], m[2]),
                    R1 = new Vector3(m[4], m[5], m[6]),
                    R2 = new Vector3(m[8], m[9], m[10]),
                });
            }
            probes.m_Cells = cells.ToArray();
            return probes;
        }

        static float[] Floats(JsonElement array)
        {
            var values = new float[array.GetArrayLength()];
            int i = 0;
            foreach (var v in array.EnumerateArray()) values[i++] = v.GetSingle();
            return values;
        }

        static int[] Ints(JsonElement array)
        {
            var values = new int[array.GetArrayLength()];
            int i = 0;
            foreach (var v in array.EnumerateArray()) values[i++] = v.GetInt32();
            return values;
        }
    }

    /// <summary>A group of light probe positions for the bake; at runtime the baked probes
    /// (LightmapSettings.lightProbes) light the scene.</summary>
    [ClassId(220)]
    public sealed partial class LightProbeGroup : Behaviour
    {
        Vector3[] m_SourcePositions = Array.Empty<Vector3>();
        bool m_Dering = true;

        public LightProbeGroup() { }

        public Vector3[] probePositions
        {
            get => (Vector3[])m_SourcePositions.Clone();
            set => m_SourcePositions = value == null ? Array.Empty<Vector3>() : (Vector3[])value.Clone();
        }

        public bool dering { get => m_Dering; set => m_Dering = value; }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            base.ReadSerialized(body, resolver);
            var positions = new List<Vector3>();
            foreach (var p in body.Get("m_SourcePositions")?.Seq() ?? Array.Empty<YNode>())
                positions.Add(new Vector3(p.Float("x"), p.Float("y"), p.Float("z")));
            m_SourcePositions = positions.ToArray();
            m_Dering = body.Bool("m_Dering", true);
        }
    }

    /// <summary>
    /// A scene's baked lighting (its LightingData asset): the lightmaps join
    /// LightmapSettings.lightmaps, each lightmapped renderer gets its lightmap index and scale and
    /// offset, each baked light its baking output, and the probes become
    /// LightmapSettings.lightProbes. What a renderer draws with is <see cref="Apply"/>'s.
    /// </summary>
    internal static class BakedLighting
    {
        static bool s_WarnedProbeMerge;

        internal static void Read(JsonElement root, LoadedFile file)
        {
            int offset = LightmapSettings.Count;
            if (root.TryGetProperty("lightmapsMode", out var mode)) LightmapSettings.lightmapsMode = (LightmapsMode)mode.GetInt32();
            if (root.TryGetProperty("lightmaps", out var lightmaps) && lightmaps.GetArrayLength() > 0)
            {
                var maps = new List<LightmapData>();
                foreach (var m in lightmaps.EnumerateArray())
                    maps.Add(new LightmapData { m_Light = Texture(m, "color"), m_Dir = Texture(m, "dir"), m_ShadowMask = Texture(m, "shadowMask") });
                LightmapSettings.Append(maps.ToArray());
            }
            if (file != null && root.TryGetProperty("renderers", out var renderers))
            {
                foreach (var r in renderers.EnumerateArray())
                {
                    if (!(Find(file, r) is Renderer renderer)) continue;
                    int index = r.GetProperty("index").GetInt32();
                    renderer.lightmapIndex = index >= 0xFFFE ? -1 : index + offset;
                    renderer.lightmapScaleOffset = Vector(r.GetProperty("st"));
                    int dynamic = r.GetProperty("indexDynamic").GetInt32();
                    renderer.realtimeLightmapIndex = dynamic >= 0xFFFE ? -1 : dynamic;
                    renderer.realtimeLightmapScaleOffset = Vector(r.GetProperty("stDynamic"));
                }
            }
            if (file != null && root.TryGetProperty("lights", out var lights))
            {
                foreach (var l in lights.EnumerateArray())
                {
                    if (!(Find(file, l) is Light light)) continue;
                    light.bakingOutput = new LightBakingOutput
                    {
                        probeOcclusionLightIndex = l.GetProperty("probeOcclusionLightIndex").GetInt32(),
                        occlusionMaskChannel = l.GetProperty("occlusionMaskChannel").GetInt32(),
                        lightmapBakeType = (LightmapBakeType)l.GetProperty("lightmapBakeType").GetInt32(),
                        mixedLightingMode = (MixedLightingMode)l.GetProperty("mixedLightingMode").GetInt32(),
                        isBaked = l.GetProperty("isBaked").GetBoolean(),
                    };
                }
            }
            // Baked reflection probes reflect the cubemaps the bake made for them.
            if (file != null && root.TryGetProperty("reflectionProbes", out var reflections))
            {
                foreach (var r in reflections.EnumerateArray())
                {
                    if (!(Find(file, r) is ReflectionProbe probe)) continue;
                    var cubemap = r.GetProperty("cubemap");
                    probe.bakedTexture = cubemap.ValueKind == JsonValueKind.Object
                        ? RenderSettings.BakedReflection(cubemap.GetProperty("guid").GetString(), cubemap.GetProperty("fileId").GetInt64())
                        : null;
                }
            }
            if (root.TryGetProperty("probes", out var probes) && probes.ValueKind == JsonValueKind.Object)
            {
                if (LightmapSettings.lightProbes != null && !s_WarnedProbeMerge)
                {
                    s_WarnedProbeMerge = true;
                    Debug.LogWarning("Unbound: the light probes of additively loaded scenes are not merged yet: the last loaded scene's probes light the scene");
                }
                LightmapSettings.lightProbes = LightProbes.FromJson(probes);
            }
        }

        /// <summary>The scene object an entry is of: by its file id, or (in a prefab instance) by
        /// the id Unity gives a prefab instance's object, its prefab file id XOR the instance's
        /// (assumed, not measured).</summary>
        static Object Find(LoadedFile file, JsonElement entry)
        {
            long id = entry.GetProperty("object").GetInt64();
            long prefab = entry.GetProperty("prefab").GetInt64();
            if (prefab != 0) id = (id ^ prefab) & long.MaxValue;
            return file.Objects.TryGetValue(id, out var obj) ? obj : null;
        }

        static Texture2D Texture(JsonElement map, string key)
        {
            if (!map.TryGetProperty(key, out var r) || r.ValueKind != JsonValueKind.Object) return null;
            return AssetDatabaseRuntime.Load(r.GetProperty("guid").GetString(), r.GetProperty("fileId").GetInt64(), typeof(Texture2D)) as Texture2D;
        }

        static Vector4 Vector(JsonElement v)
        {
            var f = new float[4];
            int i = 0;
            foreach (var x in v.EnumerateArray()) if (i < 4) f[i++] = x.GetSingle();
            return new Vector4(f[0], f[1], f[2], f[3]);
        }

        static readonly int s_Lightmap = Shader.PropertyToID("unity_Lightmap");
        static readonly int s_LightmapInd = Shader.PropertyToID("unity_LightmapInd");
        static readonly int s_ShadowMask = Shader.PropertyToID("unity_ShadowMask");
        static readonly int s_LightmapST = Shader.PropertyToID("unity_LightmapST");
        static readonly int[] s_ShIds =
        {
            Shader.PropertyToID("unity_SHAr"), Shader.PropertyToID("unity_SHAg"), Shader.PropertyToID("unity_SHAb"),
            Shader.PropertyToID("unity_SHBr"), Shader.PropertyToID("unity_SHBg"), Shader.PropertyToID("unity_SHBb"),
            Shader.PropertyToID("unity_SHC"),
        };

        /// <summary>
        /// What a renderer's draws take from the baked lighting: a lightmapped renderer its
        /// lightmap (unity_Lightmap, unity_LightmapInd, unity_ShadowMask, unity_LightmapST) and the
        /// LIGHTMAP_ON (and DIRLIGHTMAP_COMBINED) keywords; one lit by blended probes the probes
        /// interpolated at its bounds' center (or anchor) as ShadeSH9's constants. Null when
        /// neither applies. <paramref name="lightmaps"/>, <paramref name="probes"/>: what the
        /// pipeline asks for (PerObjectData).
        /// </summary>
        internal static Dictionary<int, object> Apply(Renderer renderer, Dictionary<int, object> properties, List<string> keywords, bool lightmaps = true, bool probes = true)
        {
            if (renderer == null) return properties;
            var map = lightmaps ? LightmapSettings.Of(renderer.lightmapIndex) : null;
            if (map != null && map.m_Light != null)
            {
                properties = properties != null ? new Dictionary<int, object>(properties) : new Dictionary<int, object>();
                properties[s_Lightmap] = map.m_Light;
                properties[s_LightmapST] = renderer.lightmapScaleOffset;
                keywords.Add("LIGHTMAP_ON");
                if (map.m_Dir != null && LightmapSettings.lightmapsMode == LightmapsMode.CombinedDirectional)
                {
                    properties[s_LightmapInd] = map.m_Dir;
                    keywords.Add("DIRLIGHTMAP_COMBINED");
                }
                if (map.m_ShadowMask != null) properties[s_ShadowMask] = map.m_ShadowMask;
                return properties;
            }
            var lightProbes = LightmapSettings.lightProbes;
            if (!probes || lightProbes == null || lightProbes.count == 0 || renderer.lightProbeUsage != LightProbeUsage.BlendProbes)
                return properties;
            var anchor = renderer.probeAnchor;
            var position = anchor != null ? anchor.position : renderer.bounds.center;
            int cell = renderer.m_ProbeCell;
            if (!lightProbes.Interpolate(position, ref cell, out var sh)) return properties;
            renderer.m_ProbeCell = cell;
            return WithProbe(sh, properties);
        }

        static int s_ImmediateCell = -1;

        /// <summary>The light probes interpolated at a point (a Graphics.DrawMesh draw's bounds
        /// centre or anchor), as a renderer that blends probes gets them.</summary>
        internal static Dictionary<int, object> ApplyProbes(Vector3 position, Dictionary<int, object> properties)
        {
            var lightProbes = LightmapSettings.lightProbes;
            if (lightProbes == null || lightProbes.count == 0) return properties;
            if (!lightProbes.Interpolate(position, ref s_ImmediateCell, out var sh)) return properties;
            return WithProbe(sh, properties);
        }

        static Dictionary<int, object> WithProbe(SphericalHarmonicsL2 sh, Dictionary<int, object> properties)
        {
            sh.ShaderConstants(out var ar, out var ag, out var ab, out var br, out var bg, out var bb, out var c);
            properties = properties != null ? new Dictionary<int, object>(properties) : new Dictionary<int, object>();
            properties[s_ShIds[0]] = ar;
            properties[s_ShIds[1]] = ag;
            properties[s_ShIds[2]] = ab;
            properties[s_ShIds[3]] = br;
            properties[s_ShIds[4]] = bg;
            properties[s_ShIds[5]] = bb;
            properties[s_ShIds[6]] = c;
            return properties;
        }
    }
}
