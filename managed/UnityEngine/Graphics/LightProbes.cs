using System;
using System.Collections.Generic;
using System.Text.Json;
using UnityEngine.Rendering;

namespace UnityEngine
{
    /// <summary>
    /// Light probes and the tetrahedralization that interpolates them: a point inside the
    /// probes' hull blends the four probes of its tetrahedron by barycentric weights; a point
    /// outside blends the three of the hull triangle that, moved along its probes' hull rays,
    /// passes through it (Unity's scheme, Cupisz, "Light probe interpolation using tetrahedral
    /// tessellations", GDC 2012). Immutable once built, except for the coefficients, which
    /// LightProbes.bakedProbes replaces as a whole; a LightProbesQuery keeps the set it was
    /// created with.
    /// </summary>
    internal sealed class ProbeSet
    {
        internal struct Cell
        {
            internal int I0, I1, I2, I3;
            internal int N0, N1, N2, N3;
            /// <summary>Inner cells: rows of the matrix taking a point less the fourth probe to
            /// the first three barycentric coordinates.</summary>
            internal Vector3 R0, R1, R2;
        }

        internal Vector3[] Positions = Array.Empty<Vector3>();
        internal SphericalHarmonicsL2[] Probes = Array.Empty<SphericalHarmonicsL2>();
        /// <summary>Per probe, the occlusion of the mixed lights by shadow mask channel.</summary>
        internal Vector4[] Occlusion = Array.Empty<Vector4>();
        internal Cell[] Cells = Array.Empty<Cell>();
        internal Vector3[] HullRays = Array.Empty<Vector3>();

        internal static readonly ProbeSet Empty = new ProbeSet();

        /// <summary>A copy whose coefficients do not change with the original's.</summary>
        internal ProbeSet Snapshot() => new ProbeSet
        {
            Positions = Positions,
            Probes = (SphericalHarmonicsL2[])Probes.Clone(),
            Occlusion = Occlusion,
            Cells = Cells,
            HullRays = HullRays,
        };

        const float Epsilon = -1e-4f;

        /// <summary>
        /// The probes' lighting and occlusion at <paramref name="p"/> as LightProbes'
        /// interpolation queries give it: the cell containing it is walked to from
        /// <paramref name="cell"/> (any index out of range starts at the first) and becomes the
        /// new hint. Outside the probes' hull, or without probes, there is none: the hint
        /// becomes -1 and false is returned (measured: tests/projects/lightprobes, queries outside
        /// the hull give the ambient probe, occlusion 1 and index -1).
        /// </summary>
        internal bool Evaluate(Vector3 p, ref int cell, out SphericalHarmonicsL2 sh, out Vector4 occlusion)
        {
            sh = default;
            occlusion = Vector4.one;
            if (Probes.Length == 0)
            {
                cell = -1;
                return false;
            }
            if (Cells.Length == 0)
            {
                int nearest = Nearest(p);
                sh = Probes[nearest];
                occlusion = OcclusionOf(nearest);
                cell = -1;
                return true;
            }
            Span<float> w = stackalloc float[4];
            int found = Locate(p, cell, w);
            if (found < 0 || Cells[found].I3 < 0)
            {
                cell = -1;
                return false;
            }
            cell = found;
            Blend(found, w, out sh);
            occlusion = BlendOcclusion(found, w);
            return true;
        }

        /// <summary>The probes blended at <paramref name="p"/> as a renderer gets them, walking the
        /// cells from <paramref name="cell"/> (a renderer's last cell) toward it, outer cells
        /// extrapolating along the hull rays; false without probes.</summary>
        internal bool Interpolate(Vector3 p, ref int cell, out SphericalHarmonicsL2 sh)
        {
            sh = default;
            if (Probes.Length == 0) return false;
            if (Cells.Length == 0)
            {
                sh = Probes[Nearest(p)];
                return true;
            }
            Span<float> w = stackalloc float[4];
            int found = Locate(p, cell, w);
            if (found < 0)
            {
                sh = Probes[Nearest(p)];
                return true;
            }
            cell = found;
            Blend(found, w, out sh);
            return true;
        }

        /// <summary>Whether a cell lies outside the probes' hull (a hull triangle extended).</summary>
        internal bool IsOuterCell(int cell) => cell >= 0 && cell < Cells.Length && Cells[cell].I3 < 0;

        /// <summary>The cell whose weights of <paramref name="p"/> (in <paramref name="w"/>) are all
        /// non-negative, walked to from <paramref name="start"/>; -1 when none is.</summary>
        int Locate(Vector3 p, int start, Span<float> w)
        {
            int cell = start < 0 || start >= Cells.Length ? 0 : start;
            for (int steps = 0; steps < Cells.Length + 4; steps++)
            {
                int next = Step(p, cell, w);
                if (next == cell) return cell;
                if (next < 0) break;
                cell = next;
            }
            // The walk cycled (degenerate cells): try each cell.
            for (int c = 0; c < Cells.Length; c++)
                if (Step(p, c, w) == c) return c;
            return -1;
        }

        int Nearest(Vector3 p)
        {
            int best = 0;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < Positions.Length; i++)
            {
                float d = (Positions[i] - p).sqrMagnitude;
                if (d < bestDistance) { bestDistance = d; best = i; }
            }
            return best;
        }

        Vector4 OcclusionOf(int probe) => probe < Occlusion.Length ? Occlusion[probe] : Vector4.one;

        void Blend(int cell, ReadOnlySpan<float> w, out SphericalHarmonicsL2 sh)
        {
            var c = Cells[cell];
            sh = Probes[c.I0] * w[0] + Probes[c.I1] * w[1] + Probes[c.I2] * w[2];
            if (c.I3 >= 0) sh += Probes[c.I3] * w[3];
        }

        Vector4 BlendOcclusion(int cell, ReadOnlySpan<float> w)
        {
            var c = Cells[cell];
            return OcclusionOf(c.I0) * w[0] + OcclusionOf(c.I1) * w[1] + OcclusionOf(c.I2) * w[2] + OcclusionOf(c.I3) * w[3];
        }

        /// <summary>The weights of <paramref name="p"/> in a cell: the cell itself when they are
        /// all non-negative, else the neighbor across the face of the most negative one.</summary>
        int Step(Vector3 p, int index, Span<float> w)
        {
            var c = Cells[index];
            if (c.I3 >= 0)
            {
                var d = p - Positions[c.I3];
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
            Vector3 a = Positions[c.I0], b = Positions[c.I1], e = Positions[c.I2];
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

        Vector3 Ray(int probe) => probe < HullRays.Length ? HullRays[probe] : Vector3.zero;

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

        /// <summary>The probe set of a lighting data's `probes`.</summary>
        internal static ProbeSet FromJson(JsonElement json)
        {
            var set = new ProbeSet();
            var positions = Floats(json.GetProperty("positions"));
            var coefficients = Floats(json.GetProperty("coefficients"));
            int count = positions.Length / 3;
            set.Positions = new Vector3[count];
            set.Probes = new SphericalHarmonicsL2[count];
            set.Occlusion = new Vector4[count];
            for (int i = 0; i < count; i++)
            {
                set.Positions[i] = new Vector3(positions[3 * i], positions[3 * i + 1], positions[3 * i + 2]);
                for (int k = 0; k < 27; k++) set.Probes[i][k / 9, k % 9] = coefficients[27 * i + k];
                set.Occlusion[i] = Vector4.one;
            }
            if (json.TryGetProperty("occlusion", out var occlusion))
            {
                int i = 0;
                foreach (var o in occlusion.EnumerateArray())
                {
                    if (i >= count) break;
                    set.Occlusion[i++] = ProbeOcclusion(Ints(o.GetProperty("lights")), Floats(o.GetProperty("values")), Ints(o.GetProperty("channels")));
                }
            }
            var rays = Floats(json.GetProperty("hullRays"));
            set.HullRays = new Vector3[rays.Length / 3];
            for (int i = 0; i < set.HullRays.Length; i++)
                set.HullRays[i] = new Vector3(rays[3 * i], rays[3 * i + 1], rays[3 * i + 2]);
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
            set.Cells = cells.ToArray();
            return set;
        }

        /// <summary>A probe's occlusion vector: by shadow mask channel, the occlusion of the mixed
        /// light baked into it; channels no light uses are 1.</summary>
        internal static Vector4 ProbeOcclusion(int[] lights, float[] values, int[] channels)
        {
            var v = Vector4.one;
            for (int k = 0; k < 4 && k < lights.Length; k++)
                if (lights[k] >= 0 && channels[k] >= 0 && channels[k] < 4) v[channels[k]] = values[k];
            return v;
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

    /// <summary>
    /// A scene's baked light probes. The first loaded scene's are LightmapSettings.lightProbes;
    /// the probes of scenes loaded additively join them, and those of unloaded scenes leave them,
    /// only when they are tetrahedralized again (Tetrahedralize, TetrahedralizeAsync), as in the
    /// Editor: a load or unload raises needsRetetrahedralization and the interpolation keeps the
    /// previous set until then (measured: tests/projects/lightprobes).
    /// </summary>
    public sealed partial class LightProbes : Object
    {
        /// <summary>The scene's own probes and cells.</summary>
        internal ProbeSet m_Self = ProbeSet.Empty;
        /// <summary>What the probes interpolate with: the scene's own set, or all loaded scenes'.</summary>
        internal ProbeSet m_Set = ProbeSet.Empty;

        LightProbes() { }

        public Vector3[] positions => (Vector3[])m_Set.Positions.Clone();
        public int count => m_Set.Positions.Length;
        public int countSelf => m_Self.Positions.Length;
        public int cellCount => m_Set.Cells.Length;
        public int cellCountSelf => m_Self.Cells.Length;

        public SphericalHarmonicsL2[] bakedProbes
        {
            get => (SphericalHarmonicsL2[])m_Set.Probes.Clone();
            set
            {
                if (value == null || value.Length != m_Set.Positions.Length)
                {
                    Debug.LogError($"Coefficients array must have probeCount ({m_Set.Positions.Length}) elements.");
                    return;
                }
                m_Set.Probes = (SphericalHarmonicsL2[])value.Clone();
            }
        }

        [Obsolete("Use bakedProbes instead.")]
        public float[] coefficients
        {
            get
            {
                var probes = m_Set.Probes;
                var flat = new float[probes.Length * 27];
                for (int i = 0; i < probes.Length; i++)
                    for (int k = 0; k < 27; k++) flat[i * 27 + k] = probes[i][k / 9, k % 9];
                return flat;
            }
            set
            {
                if (value == null || value.Length != m_Set.Probes.Length * 27)
                {
                    Debug.LogError($"Coefficients array must have 27*probeCount ({m_Set.Probes.Length * 27}) elements.");
                    return;
                }
                var probes = (SphericalHarmonicsL2[])m_Set.Probes.Clone();
                for (int i = 0; i < probes.Length; i++)
                    for (int k = 0; k < 27; k++) probes[i][k / 9, k % 9] = value[i * 27 + k];
                m_Set.Probes = probes;
            }
        }

        public Vector3[] GetPositionsSelf() => (Vector3[])m_Self.Positions.Clone();

        public static event Action lightProbesUpdated;
        public static event Action tetrahedralizationCompleted;
        public static event Action needsRetetrahedralization;

        internal static void RaiseUpdated() => lightProbesUpdated?.Invoke();

        /// <summary>The loaded scenes with baked probes (by scene handle), in load order: the sets
        /// a tetrahedralization combines.</summary>
        static readonly List<(int Scene, LightProbes Probes)> s_Scenes = new List<(int, LightProbes)>();
        /// <summary>Each scene's own probes object, by scene handle. It outlives the scene's load
        /// membership: GetSharedLightProbesForScene answers it while the scene is valid, also during
        /// its sceneUnloaded (measured: tests/projects/lightprobes lifecycle.jsonl).</summary>
        static readonly Dictionary<int, LightProbes> s_Own = new Dictionary<int, LightProbes>();

        internal static void ResetScenes()
        {
            s_Scenes.Clear();
            s_Own.Clear();
        }

        /// <summary>A scene's baked probes, loaded: the first become the current probes; those of
        /// a scene loaded beside them wait for the next tetrahedralization.</summary>
        internal static void SceneLoaded(int scene, LightProbes probes)
        {
            s_Scenes.RemoveAll(s => s.Scene == scene);
            s_Scenes.Add((scene, probes));
            s_Own[scene] = probes;
            if (LightmapSettings.lightProbes == null)
            {
                LightmapSettings.SetLightProbes(probes);
                return;
            }
            needsRetetrahedralization?.Invoke();
        }

        /// <summary>A scene unloaded on its own (UnloadScene, UnloadSceneAsync): its probes leave
        /// the loaded set and needsRetetrahedralization is raised, before its sceneUnloaded; the
        /// current probes keep the combined set until the next tetrahedralization, and the scene's
        /// own object stays readable while the scene is valid (measured, Unity 6000.0.71f1:
        /// tests/projects/lightprobes Reference lifecycle.jsonl, published on branch
        /// t3/light-probes-lifecycle-2, commit f8516528). Unity raises this group after
        /// UnloadSceneAsync has returned; Unbound unloads within the call (an open gap).</summary>
        internal static void SceneUnloaded(int scene)
        {
            if (s_Scenes.RemoveAll(s => s.Scene == scene) > 0) needsRetetrahedralization?.Invoke();
        }

        /// <summary>
        /// A SceneManager Single load replaces the loaded scenes, before the first of them is
        /// unloaded: their probes leave the loaded set without needsRetetrahedralization, and the
        /// current probes, when they are one of those scenes' own, lose their combined set
        /// (count and cellCount 0) but keep their own (countSelf, cellCountSelf), already during
        /// the outgoing sceneUnloaded. The incoming scene's probes then become the current ones
        /// as its lighting data is read, before activeSceneChanged, again without an event
        /// (measured, Unity 6000.0.71f1: tests/projects/lightprobes Reference lifecycle.jsonl,
        /// branch t3/light-probes-lifecycle-2, commit f8516528; one outgoing scene). With several
        /// outgoing scenes the same reset is applied once for all of them: inferred, not measured.
        /// </summary>
        internal static void SingleLoadReplacing()
        {
            var current = LightmapSettings.lightProbes;
            if (current != null && s_Scenes.Exists(s => s.Probes == current)) current.m_Set = ProbeSet.Empty;
            s_Scenes.Clear();
        }

        /// <summary>The current probes take those of every loaded scene: a scene's own baked cells
        /// when it is the only one, else all probes tetrahedralized together.</summary>
        static void Retetrahedralize()
        {
            var current = LightmapSettings.lightProbes;
            if (current == null) return;
            if (s_Scenes.Count == 0 || (s_Scenes.Count == 1 && s_Scenes[0].Probes == current))
            {
                current.m_Set = current.m_Self;
                return;
            }
            var sets = new List<ProbeSet>();
            foreach (var (_, probes) in s_Scenes) sets.Add(probes.m_Self);
            current.m_Set = ProbeTetrahedralization.Combine(sets);
        }

        public static void Tetrahedralize()
        {
            Retetrahedralize();
            tetrahedralizationCompleted?.Invoke();
        }

        /// <summary>The probes change when the job completes, not at the call, and the call raises
        /// no event (measured: tests/projects/lightprobes README, not in its Reference). In the
        /// Editor the completion varies between runs, from within frames to much later, so it is
        /// not recorded: completing on the next frame and raising tetrahedralizationCompleted then
        /// are Unbound's choice, not measured.</summary>
        public static void TetrahedralizeAsync() => CompleteLater();

        static async void CompleteLater()
        {
            await Awaitable.NextFrameAsync();
            Retetrahedralize();
            tetrahedralizationCompleted?.Invoke();
        }

        /// <summary>A scene's own probes while the scene is valid, also once it left the loaded
        /// set; null for an invalid scene and for a scene still loading whose probes are not read
        /// yet (measured: tests/projects/lightprobes lifecycle.jsonl). A loaded scene without baked
        /// probes gets the current probes (not measured).</summary>
        public static LightProbes GetSharedLightProbesForScene(SceneManagement.Scene scene)
        {
            if (!scene.IsValid()) return null;
            if (s_Own.TryGetValue(scene.handle, out var own)) return own;
            return scene.isLoaded ? LightmapSettings.lightProbes : null;
        }

        public static LightProbes GetInstantiatedLightProbesForScene(SceneManagement.Scene scene) => GetSharedLightProbesForScene(scene);

        static ProbeSet Current => LightmapSettings.lightProbes?.m_Set ?? ProbeSet.Empty;

        /// <summary>The lighting at a point: the current probes interpolated, or the ambient probe
        /// outside their hull (also for a renderer: measured).</summary>
        public static void GetInterpolatedProbe(Vector3 position, Renderer renderer, out SphericalHarmonicsL2 probe)
        {
            int hint = renderer != null ? renderer.m_ProbeCell : -1;
            if (!Current.Evaluate(position, ref hint, out probe, out _)) probe = RenderSettings.ambientProbe;
        }

        public static void CalculateInterpolatedLightAndOcclusionProbes(Vector3[] positions, SphericalHarmonicsL2[] lightProbes, Vector4[] occlusionProbes)
        {
            if (positions == null) throw new ArgumentNullException(nameof(positions));
            if (lightProbes == null && occlusionProbes == null) throw new ArgumentException("Argument lightProbes and occlusionProbes cannot both be null.");
            if (lightProbes != null && lightProbes.Length < positions.Length)
                throw new ArgumentException(nameof(lightProbes), "Argument lightProbes has less elements than positions");
            if (occlusionProbes != null && occlusionProbes.Length < positions.Length)
                throw new ArgumentException(nameof(occlusionProbes), "Argument occlusionProbes has less elements than positions");
            Calculate(positions, positions.Length, lightProbes, occlusionProbes);
        }

        public static void CalculateInterpolatedLightAndOcclusionProbes(List<Vector3> positions, List<SphericalHarmonicsL2> lightProbes, List<Vector4> occlusionProbes)
        {
            if (positions == null) throw new ArgumentNullException(nameof(positions));
            if (lightProbes == null && occlusionProbes == null) throw new ArgumentException("Argument lightProbes and occlusionProbes cannot both be null.");
            var shs = lightProbes != null ? new SphericalHarmonicsL2[positions.Count] : null;
            var occlusion = occlusionProbes != null ? new Vector4[positions.Count] : null;
            Calculate(positions.ToArray(), positions.Count, shs, occlusion);
            if (lightProbes != null) { lightProbes.Clear(); lightProbes.AddRange(shs); }
            if (occlusionProbes != null) { occlusionProbes.Clear(); occlusionProbes.AddRange(occlusion); }
        }

        static void Calculate(Vector3[] positions, int count, SphericalHarmonicsL2[] lightProbes, Vector4[] occlusionProbes)
        {
            var set = Current;
            var ambient = RenderSettings.ambientProbe;
            int hint = -1;
            for (int i = 0; i < count; i++)
            {
                if (!set.Evaluate(positions[i], ref hint, out var sh, out var occlusion)) sh = ambient;
                if (lightProbes != null) lightProbes[i] = sh;
                if (occlusionProbes != null) occlusionProbes[i] = occlusion;
            }
        }

        [Obsolete("Use GetInterpolatedProbe instead.")]
        public void GetInterpolatedLightProbe(Vector3 position, Renderer renderer, float[] coefficients)
        {
            if (coefficients == null || coefficients.Length < 27) throw new ArgumentException("Coefficients array must have 27 elements.", nameof(coefficients));
            int hint = -1;
            if (!m_Set.Evaluate(position, ref hint, out var sh, out _)) sh = RenderSettings.ambientProbe;
            for (int k = 0; k < 27; k++) coefficients[k] = sh[k / 9, k % 9];
        }

        /// <summary>The probes blended at <paramref name="p"/> as a renderer gets them
        /// (<see cref="ProbeSet.Interpolate"/>).</summary>
        internal bool Interpolate(Vector3 p, ref int cell, out SphericalHarmonicsL2 sh) => m_Set.Interpolate(p, ref cell, out sh);

        internal bool IsOuterCell(int cell) => m_Set.IsOuterCell(cell);

        internal static LightProbes FromJson(JsonElement json)
        {
            var set = ProbeSet.FromJson(json);
            return new LightProbes { name = "LightProbes", m_Self = set, m_Set = set };
        }
    }
}
