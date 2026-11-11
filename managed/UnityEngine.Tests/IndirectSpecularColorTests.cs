using System;
using System.Collections.Generic;
using System.Reflection;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.Rendering;
using Xunit;

namespace Unbound.Tests
{
    [CollectionDefinition("RenderSettings state", DisableParallelization = true)]
    public class RenderSettingsStateCollection { }

    /// <summary>
    /// <c>unity_IndirectSpecColor</c> against the Editor's values
    /// (tests/projects/indirectspec/Reference/unity-6000.0.71f1/{linear,gamma}.jsonl, the
    /// <c>dump unity_IndirectSpecColor</c> records). The scene has no serialized
    /// <c>m_IndirectSpecularColor</c>; its lighting data's <c>m_BakedAmbientProbeInLinear</c> DC
    /// term is <see cref="L0"/> (offline parse of the fixture's LightingData.asset, the input).
    /// </summary>
    [Collection("RenderSettings state")]
    public class IndirectSpecularColorTests
    {
        static readonly Color L0 = new Color(0.18028377f, 0.22571412f, 0.30692285f, 1f);
        const float Tolerance = 2e-5f; // analyze.py's predeclared tolerance

        /// <summary>The fixture's RenderSettings (serializedVersion 10: no m_IndirectSpecularColor).</summary>
        const string Version10 = @"RenderSettings:
  serializedVersion: 10
  m_Fog: 0
  m_AmbientSkyColor: {r: 0.25, g: 0.3, b: 0.4, a: 1}
  m_AmbientEquatorColor: {r: 0.2, g: 0.2, b: 0.2, a: 1}
  m_AmbientGroundColor: {r: 0.1, g: 0.08, b: 0.06, a: 1}
  m_AmbientIntensity: 1
  m_AmbientMode: 1
  m_SkyboxMaterial: {fileID: 0}
  m_HaloTexture: {fileID: 0}
  m_DefaultReflectionMode: 0
  m_DefaultReflectionResolution: 128
  m_ReflectionBounces: 1
  m_ReflectionIntensity: 1
  m_CustomReflection: {fileID: 0}
  m_Sun: {fileID: 0}
";

        static string Explicit(string color) => Version10.Replace("serializedVersion: 10", "serializedVersion: 9")
            + $"  m_IndirectSpecularColor: {color}\n";

        static readonly ObjectRef ProjectBake = new ObjectRef(112000000, "cd1e7cf349cac13319b42bd6b2033b3d", 2);
        static readonly ObjectRef BuiltinData = new ObjectRef(112000000, "0000000000000000f000000000000000", 0);

        static SphericalHarmonicsL2 Probe(Color dc)
        {
            var sh = new SphericalHarmonicsL2();
            sh[0, 0] = dc.r; sh[1, 0] = dc.g; sh[2, 0] = dc.b;
            // Higher bands must not leak into the DC term.
            sh[0, 1] = 0.5f; sh[1, 4] = -0.25f; sh[2, 8] = 0.125f;
            return sh;
        }

        // ambientIntensity, reflectionIntensity, Native linear RGBA, Native gamma RGBA (settled ==
        // immediate in every setting).
        public static IEnumerable<object[]> Settings() => new[]
        {
            new object[] { 1f, 1f, new[] { 0.18028377f, 0.225714117f, 0.306922853f, 1f }, new[] { 0.461695135f, 0.5124164f, 0.589933038f, 1f } },
            new object[] { 1f, 0.25f, new[] { 0.0450709425f, 0.05642853f, 0.07673071f, 0.25f }, new[] { 0.234985322f, 0.2634517f, 0.3069564f, 0.25f } },
            new object[] { 0.5f, 1f, new[] { 0.18028377f, 0.225714117f, 0.306922853f, 1f }, new[] { 0.461695135f, 0.5124164f, 0.589933038f, 1f } },
            new object[] { 0.5f, 0.25f, new[] { 0.0450709425f, 0.05642853f, 0.07673071f, 0.25f }, new[] { 0.234985322f, 0.2634517f, 0.3069564f, 0.25f } },
        };

        [Theory]
        [MemberData(nameof(Settings))]
        public void WithoutASerializedColorItIsTheBakedSkyboxDCAsTheEditorGivesIt(float ambient, float reflection, float[] linear, float[] gamma)
        {
            using var state = new State();
            var scene = state.Scene(Version10, ProjectBake, Probe(L0));
            state.Activate(scene);
            RenderSettings.ambientIntensity = ambient;
            RenderSettings.reflectionIntensity = reflection;
            AssertNear(linear, RenderSettings.IndirectSpecularGlobal(true), "linear");
            AssertNear(gamma, RenderSettings.IndirectSpecularGlobal(false), "gamma");
        }

        [Fact]
        public void RejectedCandidatesAreFarFromTheResult()
        {
            // The negative controls of the witness: ambient scaling (a 0.5) and, in gamma, encoding
            // before scaling or not encoding at all (values of those candidates at a 0.5, r 0.25).
            using var state = new State();
            state.Activate(state.Scene(Version10, ProjectBake, Probe(L0)));
            RenderSettings.ambientIntensity = 0.5f;
            RenderSettings.reflectionIntensity = 0.25f;
            var linear = RenderSettings.IndirectSpecularGlobal(true);
            var gamma = RenderSettings.IndirectSpecularGlobal(false);
            Assert.True(Mathf.Abs(linear.x - 0.0225354713f) > 0.02f, "ambient-scaled candidate");
            Assert.True(Mathf.Abs(gamma.x - 0.115423784f) > 0.1f, "encode-then-scale candidate"); // sRGB(L0.r) * 0.25
            Assert.True(Mathf.Abs(gamma.x - 0.0450709425f) > 0.1f, "unencoded candidate");
        }

        [Fact]
        public void AnExplicitColorKeepsTheGammaToLinearConversionAndItsAlpha()
        {
            using var state = new State();
            state.Activate(state.Scene(Explicit("{r: 0.44657898, g: 0.4964133, b: 0.5748178, a: 0.75}"), ProjectBake, Probe(L0)));
            RenderSettings.reflectionIntensity = 0.25f;
            RenderSettings.ambientIntensity = 0.5f;
            // GammaToLinearSpace of each channel (exact sRGB), alpha as serialized.
            AssertNear(new[] { 0.167883681f, 0.210736356f, 0.289941199f, 0.75f }, RenderSettings.IndirectSpecularGlobal(true), "linear");
            AssertNear(new[] { 0.44657898f, 0.4964133f, 0.5748178f, 0.75f }, RenderSettings.IndirectSpecularGlobal(false), "gamma");
        }

        [Fact]
        public void AnExplicitBlackStaysBlack()
        {
            using var state = new State();
            state.Activate(state.Scene(Explicit("{r: 0, g: 0, b: 0, a: 1}"), ProjectBake, Probe(L0)));
            AssertNear(new[] { 0f, 0f, 0f, 1f }, RenderSettings.IndirectSpecularGlobal(true), "linear");
            AssertNear(new[] { 0f, 0f, 0f, 1f }, RenderSettings.IndirectSpecularGlobal(false), "gamma");
        }

        [Fact]
        public void WithoutAProjectBakeItStaysAsBefore()
        {
            using var state = new State();
            var none = new[] { 0f, 0f, 0f, 0f };
            foreach (var (reference, probe) in new (ObjectRef, SphericalHarmonicsL2?)[] { (default, null), (ProjectBake, null), (BuiltinData, Probe(L0)) })
            {
                state.Activate(state.Scene(Version10, reference, probe));
                AssertNear(none, RenderSettings.IndirectSpecularGlobal(true), $"linear {reference.Guid}");
                AssertNear(none, RenderSettings.IndirectSpecularGlobal(false), $"gamma {reference.Guid}");
            }
        }

        [Fact]
        public void UnmeasuredEnvironmentsStayAsBefore()
        {
            using var state = new State();
            state.Activate(state.Scene(Version10, ProjectBake, Probe(L0)));
            var none = new[] { 0f, 0f, 0f, 0f };
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            AssertNear(none, RenderSettings.IndirectSpecularGlobal(true), "custom reflection");
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            foreach (var mode in new[] { AmbientMode.Skybox, AmbientMode.Flat })
            {
                RenderSettings.ambientMode = mode;
                AssertNear(none, RenderSettings.IndirectSpecularGlobal(true), mode.ToString());
            }
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.s_EnvironmentProbe = new SphericalHarmonicsL2();
            AssertNear(none, RenderSettings.IndirectSpecularGlobal(true), "UpdateEnvironment");
            RenderSettings.s_EnvironmentProbe = null;
            AssertNear(new[] { 0.18028377f, 0.225714117f, 0.306922853f, 1f }, RenderSettings.IndirectSpecularGlobal(true), "back in the measured case");
        }

        [Fact]
        public void EachSceneUsesOnlyItsOwnLightingData()
        {
            using var state = new State();
            var other = new Color(0.05f, 0.1f, 0.2f, 1f);
            var a = state.Scene(Version10, ProjectBake, Probe(L0));
            state.Activate(a);
            RenderSettings.reflectionIntensity = 0.25f;
            // An additive scene's lighting data does not touch the active scene's source.
            var b = state.Scene(Version10, new ObjectRef(112000000, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", 2), Probe(other));
            var c = state.Scene(Version10, default, null);
            AssertNear(new[] { 0.0450709425f, 0.05642853f, 0.07673071f, 0.25f }, RenderSettings.IndirectSpecularGlobal(true), "a, additive loaded");

            state.Activate(b);
            AssertNear(new[] { 0.05f, 0.1f, 0.2f, 1f }, RenderSettings.IndirectSpecularGlobal(true), "b, its own bake and settings");
            state.Activate(c);
            AssertNear(new[] { 0f, 0f, 0f, 0f }, RenderSettings.IndirectSpecularGlobal(true), "c, no bake: not a's or b's");
            // Back to a: its script-set reflectionIntensity and its own source are restored.
            state.Activate(a);
            AssertNear(new[] { 0.0450709425f, 0.05642853f, 0.07673071f, 0.25f }, RenderSettings.IndirectSpecularGlobal(true), "a restored");
        }

        [Fact]
        public void ASceneLoadedBesideTheActiveOneKeepsItsLightingEnvironmentToItself()
        {
            // The Unity 6000.0.71f1 Editor, tests/projects/realtimegi *-plus-*: the ambient
            // probe and the default reflection are those of the active scene's lighting data; a
            // scene loaded beside it (here one without lighting data) changes neither until it
            // is the active one, and the first scene's are back with it.
            using var state = new State();
            var first = state.Scene(Version10, default, null);
            state.Activate(first);
            var probe = Probe(L0);
            var sky = new Cubemap(4, TextureFormat.RGBA32, false);
            try
            {
                RenderSettings.s_BakedProbe = probe;
                RenderSettings.s_BakedReflection = sky;
                RenderSettings.ambientMode = AmbientMode.Custom;
                var added = state.Scene(Version10, default, null);
                RenderSettings.ReadLightingData(default, state.ResolverOf(added), firstScene: false);
                Assert.Equal(probe, RenderSettings.ambientProbe);
                Assert.Same(sky, ReflectionProbe.defaultTexture);
                state.Activate(added);
                RenderSettings.ambientMode = AmbientMode.Custom;
                Assert.Equal(new SphericalHarmonicsL2(), RenderSettings.ambientProbe);
                Assert.Equal("UnityBlackCube", ReflectionProbe.defaultTexture.name);
                state.Activate(first);
                Assert.Equal(probe, RenderSettings.ambientProbe);
                Assert.Same(sky, ReflectionProbe.defaultTexture);
                // The first scene's lighting data read again while the other is in effect
                // waits for its scene as well.
                state.Activate(added);
                RenderSettings.ReadLightingData(default, state.ResolverOf(first), firstScene: false);
                Assert.Equal("UnityBlackCube", ReflectionProbe.defaultTexture.name);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sky);
            }
        }

        [Fact]
        public void SettingsReadWithoutASceneHaveNoBakedSource()
        {
            using var state = new State();
            state.Activate(state.Scene(Version10, ProjectBake, Probe(L0)));
            var resolver = new LoadedFile();
            RenderSettings.SceneLoaded(null, UnityEngine.Tests.TestYaml.DocumentBody(Version10), resolver);
            RenderSettings.BindBakedSkybox(resolver, ProjectBake, Probe(L0));
            AssertNear(new[] { 0f, 0f, 0f, 0f }, RenderSettings.IndirectSpecularGlobal(true), "sceneless settings");
        }

        [Fact]
        public void ReadingTheScenesLightingDataAgainReplacesItsSource()
        {
            using var state = new State();
            var scene = state.Scene(Version10, ProjectBake, Probe(L0));
            state.Activate(scene);
            // No lighting data reference: returns before the native side, the old source is gone.
            RenderSettings.ReadLightingData(default, state.ResolverOf(scene), firstScene: false);
            AssertNear(new[] { 0f, 0f, 0f, 0f }, RenderSettings.IndirectSpecularGlobal(true), "after re-read");
        }

        [Fact]
        public void TheShaderGlobalIsThisValue()
        {
            using var state = new State();
            state.Activate(state.Scene(Version10, ProjectBake, Probe(L0)));
            RenderSettings.reflectionIntensity = 0.25f;
            var globals = new List<(string Name, Vector4 Value)>();
            LightingGlobals.Collect(globals, new List<string>());
            var expected = RenderSettings.IndirectSpecularGlobal(QualitySettings.activeColorSpace == ColorSpace.Linear);
            Assert.Contains(globals, g => g.Name == "unity_IndirectSpecColor" && g.Value == expected);
        }

        static void AssertNear(float[] expected, Vector4 actual, string what)
        {
            for (int i = 0; i < 4; i++)
                Assert.True(Mathf.Abs(expected[i] - actual[i]) <= Tolerance, $"{what}[{i}]: expected {expected[i]} got {actual[i]}");
        }

        /// <summary>Saves every RenderSettings static (its per-scene records included) and the halo
        /// global its reader sets, and puts them back.</summary>
        /// <summary>The RenderSettings of a new scene of an earlier release.</summary>
        const string EarlierDefault = @"RenderSettings:
  serializedVersion: 9
  m_AmbientMode: 0
  m_SkyboxMaterial: {fileID: 10304, guid: 0000000000000000f000000000000000, type: 0}
  m_DefaultReflectionMode: 0
  m_DefaultReflectionResolution: 128
  m_Sun: {fileID: 0}
";

        /// <summary>A scene file holding one directional light with these angles of its own,
        /// under a parent turned about the vertical when <paramref name="parentTurn"/> is not 0.</summary>
        static LoadedFile FileWithSun(Vector3 angles, float parentTurn, List<GameObject> made, float intensity = 1f)
        {
            var file = new LoadedFile();
            var sun = new GameObject("Sun");
            made.Add(sun);
            var light = sun.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = intensity;
            if (parentTurn != 0f)
            {
                var parent = new GameObject("Turned");
                made.Add(parent);
                parent.transform.rotation = Quaternion.Euler(0f, parentTurn, 0f);
                sun.transform.SetParent(parent.transform, false);
            }
            sun.transform.localRotation = Quaternion.Euler(angles);
            file.Objects[1] = light;
            return file;
        }

        [Fact]
        public void AnEarlierReleasesDefaultSceneIsKnownByItsSettingsVersionAndItsSunsOwnAngles()
        {
            // The Unity 6000.0.71f1 Editor, tests/projects/realtimegi legacy*, currentdefault and
            // `--color-space gamma`: a scene without lighting data takes the built-in one when
            // its RenderSettings are an earlier release's with a new scene's environment, in a
            // linear project. The sun's own angles count (legacychildsun: under a turned parent),
            // not its color or intensity (legacydimsun).
            var body = UnityEngine.Tests.TestYaml.DocumentBody(EarlierDefault);
            var current = UnityEngine.Tests.TestYaml.DocumentBody(EarlierDefault.Replace("serializedVersion: 9", "serializedVersion: 10"));
            var made = new List<GameObject>();
            var colorSpace = QualitySettings.s_ColorSpace;
            try
            {
                QualitySettings.s_ColorSpace = ColorSpace.Linear;
                var defaultAngles = new Vector3(50f, -30f, 0f);
                Assert.True(RenderSettings.EarlierDefaultEnvironment(body, FileWithSun(defaultAngles, 0f, made)));
                Assert.True(RenderSettings.EarlierDefaultEnvironment(body, FileWithSun(defaultAngles, 90f, made)));
                Assert.True(RenderSettings.EarlierDefaultEnvironment(body, FileWithSun(defaultAngles, 0f, made, intensity: 0.3f)));
                Assert.False(RenderSettings.EarlierDefaultEnvironment(body, FileWithSun(new Vector3(20f, 40f, 0f), 0f, made)));
                // The default direction reached through a parent is not the default scene's light.
                Assert.False(RenderSettings.EarlierDefaultEnvironment(body, FileWithSun(new Vector3(50f, -120f, 0f), 90f, made)));
                Assert.False(RenderSettings.EarlierDefaultEnvironment(body, new LoadedFile()));
                Assert.False(RenderSettings.EarlierDefaultEnvironment(current, FileWithSun(defaultAngles, 0f, made)));
                QualitySettings.s_ColorSpace = ColorSpace.Gamma;
                Assert.False(RenderSettings.EarlierDefaultEnvironment(body, FileWithSun(defaultAngles, 0f, made)));
            }
            finally
            {
                QualitySettings.s_ColorSpace = colorSpace;
                foreach (var go in made) UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Fact]
        public void ASceneWithoutLightingDataIsNamedInAReleaseThatIsNotMeasured()
        {
            // Zero and black are what the Unity 6000.0.71f1 Editor gives; releases before Unity 6
            // bake the ambient probe and the sky's reflection by default (its upgrade guide), so
            // there Unbound says once that it gives none.
            const string Member = "A scene without lighting data";
            static int Hits()
            {
                foreach (var hit in Stubs.HitReport())
                    if (hit.Key == Member) return hit.Value;
                return 0;
            }
            using var state = new State();
            var release = ProjectInfo.UnityVersion;
            var logs = new List<string>();
            var previous = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = new Capture(logs);
            try
            {
                ProjectInfo.UnityVersion = "6000.0.71f1";
                int before = Hits();
                var measured = state.Scene(Version10, default, null);
                RenderSettings.ReadLightingData(default, state.ResolverOf(measured), firstScene: false);
                Assert.Equal(before, Hits());
                Assert.DoesNotContain(logs, l => l.Contains(Member));
                ProjectInfo.UnityVersion = "2022.3.62f1";
                var earlier = state.Scene(Version10, default, null);
                RenderSettings.ReadLightingData(default, state.ResolverOf(earlier), firstScene: false);
                Assert.Equal(before + 1, Hits());
                if (before == 0) Assert.Contains(logs, l => l.Contains(Member) && l.Contains("not measured"));
                // A scene with lighting data is not named.
                RenderSettings.ReadLightingData(ProjectBake, state.ResolverOf(earlier), firstScene: false);
                Assert.Equal(before + 1, Hits());
            }
            finally
            {
                Debug.unityLogger.logHandler = previous;
                ProjectInfo.UnityVersion = release;
            }
        }

        sealed class Capture : ILogHandler
        {
            readonly List<string> m_Logs;
            internal Capture(List<string> logs) => m_Logs = logs;
            public void LogFormat(LogType type, UnityEngine.Object context, string format, params object[] args) => m_Logs.Add(string.Format(format, args));
            public void LogException(Exception exception, UnityEngine.Object context) => m_Logs.Add(exception.Message);
        }

        sealed class State : IDisposable
        {
            const BindingFlags Statics = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
            readonly List<(FieldInfo Field, object Value)> m_Fields = new List<(FieldInfo, object)>();
            readonly System.Collections.IDictionary m_Scenes;
            readonly List<(object Key, object Value)> m_SceneEntries = new List<(object, object)>();
            readonly Texture m_Halo = Shader.GetGlobalTexture("_HaloFalloff");
            readonly Dictionary<SceneData, LoadedFile> m_Resolvers = new Dictionary<SceneData, LoadedFile>();

            internal State()
            {
                foreach (var f in typeof(RenderSettings).GetFields(Statics))
                    if (!f.IsInitOnly && !f.IsLiteral) m_Fields.Add((f, f.GetValue(null)));
                m_Scenes = (System.Collections.IDictionary)typeof(RenderSettings).GetField("s_SceneSettings", Statics).GetValue(null);
                foreach (System.Collections.DictionaryEntry e in m_Scenes) m_SceneEntries.Add((e.Key, e.Value));
            }

            /// <summary>A loaded scene: its RenderSettings document, then its lighting data, read as
            /// the loader does (same resolver, settings first).</summary>
            internal SceneData Scene(string renderSettings, ObjectRef lightingData, SphericalHarmonicsL2? probe)
            {
                var scene = new SceneData { IsLoaded = true, LoadingState = UnityEngine.SceneManagement.Scene.LoadingState.Loaded };
                var resolver = new LoadedFile();
                m_Resolvers[scene] = resolver;
                RenderSettings.SceneLoaded(scene, UnityEngine.Tests.TestYaml.DocumentBody(renderSettings), resolver);
                RenderSettings.BindBakedSkybox(resolver, lightingData, probe);
                return scene;
            }

            internal LoadedFile ResolverOf(SceneData scene) => m_Resolvers[scene];

            internal void Activate(SceneData scene) => RenderSettings.ActiveSceneChanged(scene);

            public void Dispose()
            {
                foreach (var (field, value) in m_Fields) field.SetValue(null, value);
                m_Scenes.Clear();
                foreach (var (key, value) in m_SceneEntries) m_Scenes[key] = value;
                Shader.SetGlobalTexture("_HaloFalloff", m_Halo);
            }
        }
    }
}
