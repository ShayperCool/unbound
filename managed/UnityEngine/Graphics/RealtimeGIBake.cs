using System;
using System.Collections.Generic;
using System.Text.Json;
using Unbound.Runtime;

namespace UnityEngine
{
    /// <summary>
    /// Unbound's own precompute of realtime GI (docs/REALTIME_GI.md, A scene's bake) where the
    /// Unity 6000.0.71f1 Editor would bake: a scene the Editor opens whose LightmapSettings ask
    /// for a bake on load (Lightmapping.bakeOnSceneLoad IfMissingLightingData) and that has no
    /// lighting data (tests/projects/realtimegi bake cases: the Editor bakes when it opens such a
    /// scene; a scene loaded at runtime is not baked). The bake is the realtime part only: the
    /// Editor's bake also makes baked lightmaps, the light probes' baked lighting, reflection
    /// probes and the environment lighting, which Unbound does not (each said once); a script's
    /// bake in Edit mode makes the environment lighting (<see cref="BakeEnvironment"/>).
    /// </summary>
    internal static partial class RealtimeGI
    {
        /// <summary>StaticEditorFlags.ContributeGI.</summary>
        const uint ContributeGI = 1;

        /// <summary>The scenes (paths) the Editor baked when it opened them this session: a scene
        /// loaded again at runtime keeps its bake (tests/projects/realtimegi
        /// `roombakereload-base`), a scene never opened is not baked.</summary>
        static readonly HashSet<string> s_BakedScenes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        internal static bool BakedThisSession(string path) => !string.IsNullOrEmpty(path) && s_BakedScenes.Contains(path);

        /// <summary>The scenes (paths) a script baked in Edit mode this session
        /// (<see cref="BakeInEditMode"/>): their bake stays theirs for the session, also when
        /// Play mode loads them.</summary>
        static readonly HashSet<string> s_ScriptBakedScenes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        internal static bool ScriptBakedThisSession(string path) => !string.IsNullOrEmpty(path) && s_ScriptBakedScenes.Contains(path);

        /// <summary>The files the loaded scenes were made from, by scene handle: an Edit-mode bake
        /// bakes the renderers they made.</summary>
        static readonly Dictionary<int, LoadedFile> s_SceneFiles = new Dictionary<int, LoadedFile>();

        /// <summary>Lightmapping.isRunning: true while a bake runs.</summary>
        internal static bool Baking { get; private set; }

        internal const string BakeOnLoad = "its bake on load (Lightmapping.bakeOnSceneLoad)";
        internal const string ScriptBake = "Lightmapping.Bake";

        /// <summary>
        /// Lightmapping.Bake in Edit mode: bakes the open scenes synchronously, as the Unity
        /// 6000.0.71f1 Editor does (tests/projects/realtimegi `script_bake.py --edit`): it sends
        /// bakeStarted (isRunning still false), lightingDataUpdated while it runs (the Editor
        /// sends it as its work goes on, a number of times that is not specified: here once) and
        /// bakeCompleted (isRunning false again), answers true, and the scene then has the bake's
        /// realtime GI, in Edit mode at once and in Play mode, and is dirty (not saved). The
        /// realtime GI is Unbound's own precompute of the scene's renderers (<see cref="Bake"/>),
        /// cached under LibraryUnbound/RealtimeGI. The lighting data the Editor writes beside the
        /// scene, which the scene then names, is written for a bake of the environment alone
        /// (<see cref="WriteLightingData"/>). Not made, each said once per scene: baked lightmaps
        /// and the light probes' baked lighting where the scene's Lighting Settings have baked
        /// global illumination on (Q677); baked reflection probes. The environment lighting is
        /// made (<see cref="BakeEnvironment"/>). Of the baked lightmaps Unbound lays out where each renderer would
        /// go (<see cref="BakedAtlas"/>, written to LibraryUnbound/BakedGI; renderers get
        /// nothing). Renderers, lights, LOD Groups, Terrains and
        /// Light Probe Groups a script added to the scene after it loaded are not baked (named),
        /// and the scene's lightmaps are then not written. Several open scenes are baked one by one as they would load (not
        /// measured).
        /// </summary>
        internal static bool BakeInEditMode()
        {
            UnityEditor.Lightmapping.RaiseBakeStarted();
            Baking = true;
            try
            {
                bool first = true;
                foreach (var scene in Unbound.Runtime.SceneRegistry.Loaded.ToArray())
                {
                    if (!scene.IsLoaded || scene.IsPreview) continue;
                    if (!s_SceneFiles.TryGetValue(scene.Handle.Value, out var file))
                    {
                        Warn("script-bake-file:" + scene.Path, $"{scene.Path}: Lightmapping.Bake bakes it in the Editor; it was not made from a scene file, so Unbound does not bake it.");
                        continue;
                    }
                    var settings = scene.LightingSettings;
                    var made = new HashSet<Object>(file.Objects.Values);
                    bool added = false;
                    foreach (var root in scene.Roots)
                        foreach (var component in root.GetComponentsInChildren<Component>(true))
                            if (component != null && !made.Contains(component) && BakedAtlas.TakenByBake(component))
                            {
                                added = true;
                                Warn("script-bake-added:" + scene.Path + component.GetInstanceID(), $"{scene.Path}: '{component.name}' ({component.GetType().Name}) was added after the scene loaded; Unbound's bake (Lightmapping.Bake) does not take it, and writes no lightmaps of the scene.");
                            }
                    if (!string.IsNullOrEmpty(scene.Path)) s_ScriptBakedScenes.Add(scene.Path);
                    scene.BakedOnOpen = true;
                    NoteBakeParts(file, settings, ScriptBake);
                    SceneLoaded(scene.Handle.Value, default, file, settings, first, bake: true, scriptBake: true);
                    var environment = BakeEnvironment(file);
                    // The lightmaps are made where their lighting data would be written (all else
                    // given: the decision WriteLightingData makes with what the bake made).
                    // (A renderer the bake does not take, said above, changes their light: none.)
                    bool lightmaps = settings != null && settings.bakedGI && Native.IsReady && !added
                        && Unwritten(settings, FolderOf(scene), environment.Made, Rendering.GraphicsSettings.currentRenderPipeline != null,
                            lightmapsMade: true, HasProbeGroups(file)) == null;
                    var baked = BakedAtlas.Lay(file, settings, images: lightmaps);
                    WriteLightingData(scene, file, settings, environment, lightmaps ? baked : null);
                    first = false;
                    UnityEditor.SceneManagement.EditorSceneManager.Dirty(scene);
                }
                UnityEditor.Lightmapping.RaiseLightingDataUpdated();
            }
            finally
            {
                Baking = false;
            }
            UnityEditor.Lightmapping.RaiseBakeCompleted();
            return true;
        }

        /// <summary>The environment lighting a script's bake made of each scene (by path): kept for
        /// the session, as the bake's realtime GI, also when the scene is loaded again.</summary>
        static readonly Dictionary<string, (Rendering.SphericalHarmonicsL2? Probe, Cubemap Reflection)> s_BakedEnvironments =
            new Dictionary<string, (Rendering.SphericalHarmonicsL2?, Cubemap)>(StringComparer.OrdinalIgnoreCase);
        /// <summary>The default reflections the bake made (destroyed when a bake of the same
        /// scene replaces them).</summary>
        static readonly HashSet<Cubemap> s_MadeReflections = new HashSet<Cubemap>();

        /// <summary>Keeps for the session the environment lighting a bake made of the scene at
        /// <paramref name="path"/>.</summary>
        internal static void RememberEnvironment(string path, Rendering.SphericalHarmonicsL2? probe, Cubemap reflection)
        {
            if (!string.IsNullOrEmpty(path)) s_BakedEnvironments[path] = (probe, reflection);
        }

        /// <summary>Whether the scene at <paramref name="path"/> has, for the session, the
        /// environment given (tests).</summary>
        internal static bool BakedEnvironmentKept(string path, Rendering.SphericalHarmonicsL2? probe, Cubemap reflection) =>
            s_BakedEnvironments.TryGetValue(path, out var made) && Equals(made.Probe, probe) && made.Reflection == reflection;

        /// <summary>Forgets what a bake made of the scene at <paramref name="path"/> (tests).</summary>
        internal static void ForgetEnvironment(string path) => s_BakedEnvironments.Remove(path);

        /// <summary>A scene loaded again (read with <paramref name="file"/>) that a script's bake
        /// made the environment lighting of this session has it again, as it has the bake's
        /// realtime GI.</summary>
        internal static void ReapplyEnvironment(string path, IReferenceResolver file)
        {
            if (!string.IsNullOrEmpty(path) && s_BakedEnvironments.TryGetValue(path, out var made))
                RenderSettings.SetBakedEnvironment(file, made.Probe, made.Reflection);
        }

        /// <summary>
        /// The environment lighting a bake makes of a scene, which it then has (matches the Unity
        /// 6000.0.71f1 Editor, tests/projects/bakeenvironment): the stored ambient probe, the
        /// skybox's (zero without one: the ambient mode then gives the ambient color), and the
        /// default reflection, the skybox drawn into a cube of the Default Reflection Resolution
        /// and convolved for glossy reflection as the texture importer convolves a reflection
        /// cube (without a skybox none: the scene keeps the reflection it had, but for one an
        /// earlier bake of it made). A part the render pipeline does not bake
        /// (SupportedRenderingFeatures' ambientProbeBaking and defaultReflectionProbeBaking) is
        /// taken away (<see cref="BakedParts"/>). The Editor stores both in the lighting data it
        /// writes beside the scene, the reflection as ReflectionProbe-0.exr; Unbound keeps them
        /// for the session (<see cref="ReapplyEnvironment"/>).
        /// <para>Not measured, and said once with the scene's environment kept as it was: a scene
        /// whose RenderSettings are not in effect (several open scenes: the active scene's
        /// are), a gamma colour space project, a Custom ambient source or reflection source (each
        /// part alone). Said once and made: a project with a render pipeline asset. Without a
        /// graphics device nothing is drawn: said once, the environment kept.</para>
        /// </summary>
        /// <returns>Whether the environment lighting was made as measured, each part of it (not
        /// where any part was kept as the scene had it), and the skybox's faces the default
        /// reflection was made of, or null where none was made.</returns>
        static EnvironmentBake BakeEnvironment(LoadedFile file)
        {
            (int Size, float[] Faces)? drawn = null;
            bool made = true;
            string path = file.Path;
            var (probe, reflection) = RenderSettings.BakedEnvironment(file);
            // Where none is made the scene keeps what it had, also when it loads again (the bake
            // stands in for its lighting data for the session). Without the native side nothing
            // is drawn or baked, as SceneLoaded.
            if (!Native.IsReady)
            {
                RememberEnvironment(path, probe, reflection);
                return default;
            }
            const string kept = "the scene keeps the environment lighting it had";
            string skipped = !RenderSettings.InEffect(file)
                ? $"{path}: Lightmapping.Bake makes the environment lighting of the scene whose lighting settings are in effect (the active scene's); of the other open scenes it is not measured and not made: {kept}."
                : QualitySettings.activeColorSpace != ColorSpace.Linear
                    ? $"{path}: Lightmapping.Bake's environment lighting in a gamma colour space project is not measured and not made: {kept}."
                    : !DynamicGI.SkyReadable()
                        ? $"{path}: Lightmapping.Bake needs a graphics device to draw the sky for the environment lighting; without one it is not made: {kept}."
                        : null;
            if (skipped != null)
            {
                Warn("bake-environment-kept:" + skipped, skipped);
                RememberEnvironment(path, probe, reflection);
                return default;
            }
            if (Rendering.GraphicsSettings.currentRenderPipeline != null)
                Warn("bake-environment-pipeline:" + path, $"{path}: Lightmapping.Bake's environment lighting with a render pipeline asset is not measured; Unbound makes it as the built-in render pipeline's.");
            var parts = BakedParts(Rendering.SupportedRenderingFeatures.active, RenderSettings.ambientMode, RenderSettings.defaultReflectionMode);
            made = parts.Probe != EnvironmentPart.Kept && parts.Reflection != EnvironmentPart.Kept;
            if (parts.Probe == EnvironmentPart.Kept)
                Warn("bake-environment-custom-ambient:" + path, $"{path}: Lightmapping.Bake's ambient probe with a Custom ambient source is not measured and not made: the scene keeps the probe it had.");
            if (parts.Reflection == EnvironmentPart.Kept)
                Warn("bake-environment-custom-reflection:" + path, $"{path}: Lightmapping.Bake's default reflection with a Custom reflection source is not measured and not made: the scene keeps the reflection it had.");
            // The reflection an earlier bake of this scene made: one of this session's, or a
            // project's baked cube its lighting data names (its file, which this bake replaces or
            // removes, in the Editor: tests/projects/bakeenvironment EnvSkyThenNone,
            // EnvBakedNoSky; a scene naming another scene's lighting data is not measured).
            var earlier = reflection != null && (s_MadeReflections.Contains(reflection) || RenderSettings.IsProjectReflection(reflection))
                ? reflection : null;
            if (parts.Probe == EnvironmentPart.Cleared)
                probe = new Rendering.SphericalHarmonicsL2();
            else if (parts.Probe == EnvironmentPart.Made)
                probe = DynamicGI.SkyboxProbe() ?? new Rendering.SphericalHarmonicsL2();
            if (parts.Reflection == EnvironmentPart.Cleared)
                reflection = null;
            else if (parts.Reflection == EnvironmentPart.Made)
            {
                // Without a skybox none is made: the scene keeps the reflection it had, but for
                // one an earlier bake made (tests/projects/bakeenvironment EnvNoSky, which keeps
                // the built-in lighting data's; EnvSkyThenNone, EnvBakedNoSky).
                if (RenderSettings.skybox == null)
                    reflection = reflection == earlier ? null : reflection;
                else
                    try
                    {
                        int size = RenderSettings.defaultReflectionResolution;
                        var faces = SkyboxFaces(size);
                        reflection = DefaultReflection(size, faces);
                        s_MadeReflections.Add(reflection);
                        drawn = (size, faces);
                    }
                    catch (InvalidOperationException e)
                    {
                        Warn("bake-reflection-failed:" + path, $"{path}: Lightmapping.Bake could not make the default reflection of the skybox: {e.Message}; the scene keeps the reflection it had.");
                        made = false;
                    }
            }
            RenderSettings.SetBakedEnvironment(file, probe, reflection);
            // The bake makes the ambient probe again, as a change of an ambient input does (with
            // the skybox taken away since the scene loaded, the ambient color then:
            // tests/projects/bakeenvironment EnvSkyThenNone).
            RenderSettings.AmbientChanged();
            if (earlier != null && earlier != reflection && s_MadeReflections.Remove(earlier))
                Object.DestroyImmediate(earlier);
            RememberEnvironment(path, probe, reflection);
            return new EnvironmentBake(made, drawn);
        }

        /// <summary>Why a bake's lighting data is not written beside its scene's
        /// <paramref name="folder"/> (null: untitled), or null where it is: nothing is written, or
        /// deleted, where any of it would not be the Editor's (a render pipeline asset's bake is
        /// not measured). <paramref name="editable"/>: whether a path is of the project's own
        /// assets (AssetDatabase's answer by default).</summary>
        internal static string Unwritten(LightingSettings settings, string folder, bool environmentMade, bool renderPipeline,
            bool lightmapsMade, bool probeGroups, Func<string, bool> editable = null) =>
            settings == null ? "a scene without Lighting Settings (the defaults') is not written yet"
            : settings.realtimeGI ? "Unbound writes it for a bake without realtime global illumination"
            : settings.bakedGI && probeGroups ? "the light probes of a bake with baked global illumination are not written yet"
            // (The Editor also writes the directions' image, Lightmap-N_comp_dir.png, and in the
            // Shadowmask Lighting Mode the shadowmask's, Lightmap-N_comp_shadowmask.png:
            // tests/projects/bakedbuffers Directional*, Masks*.)
            : settings.bakedGI && settings.directionalityMode == LightmapsMode.CombinedDirectional ? "directional lightmaps are not written yet"
            : settings.bakedGI && settings.mixedBakeMode == MixedLightingMode.Shadowmask ? "the shadowmask of the Shadowmask Lighting Mode is not written yet"
            : settings.bakedGI && settings.lightmapCompression != LightmapCompression.None ? "lightmaps with compression are not measured"
            : folder == null ? "an untitled scene's is not measured"
            : !Compat.BakeFilesMeasured ? $"of Unity {Application.unityVersion} it is not measured (its release family)"
            : renderPipeline ? "with a render pipeline asset it is not measured"
            : !environmentMade ? "this bake did not make all of the environment lighting (said)"
            : !(editable ?? UnityEditor.AssetDatabase.Editable)(folder) ? "the scene is not in the project's own assets"
            : settings.bakedGI && !lightmapsMade ? "this bake did not make all of its lightmaps' light (said)"
            : null;

        /// <summary>The folder a bake writes its files to beside the scene (null: untitled).</summary>
        static string FolderOf(Unbound.Runtime.SceneData scene) => string.IsNullOrEmpty(scene.Path) ? null
            : scene.Path.Substring(0, scene.Path.Length - System.IO.Path.GetExtension(scene.Path).Length);

        /// <summary>Whether the scene has light probes a bake would bake (an active light probe
        /// group with probes).</summary>
        internal static bool HasProbeGroups(LoadedFile file)
        {
            foreach (var obj in file.Objects.Values)
                if (obj is LightProbeGroup group && group != null && group.isActiveAndEnabled && group.probePositions.Length > 0) return true;
            return false;
        }

        /// <summary>A renderer's lightmap index in the lighting data for none of its own.</summary>
        const int NoLightmap = 0xFFFE;

        /// <summary>The name of a lightmap's image, as the Editor writes it beside the scene.</summary>
        static string LightmapImage(int index) => $"Lightmap-{index}_comp_light.exr";

        /// <summary>Writes the lightmaps' images of a bake with baked global illumination beside
        /// its scene (those an earlier bake left beyond them are deleted), imported as lightmaps;
        /// their GUIDs, the lightmapped renderers and the lights as the lighting data keeps them
        /// (none for a bake of the environment alone).</summary>
        static (string[] Guids, object[] Renderers, object[] Lights) WriteLightmaps(string folder, BakeResult baked)
        {
            var guids = new List<string>();
            for (int i = 0; baked != null && i < baked.Images.Length; i++)
            {
                string path = folder + "/" + LightmapImage(i);
                guids.Add(UnityEditor.AssetDatabase.WriteBakedFile(path, baked.Images[i]));
                if (UnityEditor.AssetImporter.GetAtPath(path) is UnityEditor.TextureImporter importer)
                {
                    // As the Editor imports a lightmap of a bake without lightmap compression
                    // (tests/projects/bakedbuffers written/Lightmap-0_comp_light.exr.meta).
                    importer.textureType = UnityEditor.TextureImporterType.Lightmap;
                    importer.streamingMipmaps = true;
                    importer.anisoLevel = 3;
                    importer.textureCompression = UnityEditor.TextureImporterCompression.Uncompressed;
                    importer.SaveAndReimport();
                }
            }
            // (A bake of the environment alone leaves them: not measured.)
            for (int i = guids.Count; baked != null; i++)
            {
                string stale = folder + "/" + LightmapImage(i);
                if (UnityEditor.AssetDatabase.AssetPathToGUID(stale).Length == 0) break;
                UnityEditor.AssetDatabase.DeleteAsset(stale);
            }
            if (baked == null) return (Array.Empty<string>(), Array.Empty<object>(), Array.Empty<object>());
            var renderers = new List<object>();
            // Every renderer of the bake, one without a lightmap of its own (lit by light probes,
            // Scale In Lightmap 0) with the index for none and its own placement
            // (tests/projects/bakedbuffers Shapes lightingData.json).
            foreach (var (fileId, lightmap, scaleOffset) in baked.Renderers)
                renderers.Add(new { fileId, lightmapIndex = lightmap >= 0 && lightmap < guids.Count ? lightmap : NoLightmap, scaleOffset });
            var lights = new List<object>();
            foreach (var (fileId, bakeType, mixedMode, channel, probeIndex) in baked.Lights)
                lights.Add(new { fileId, bakeType, mixedMode, channel, probeIndex });
            return (guids.ToArray(), renderers.ToArray(), lights.ToArray());
        }

        /// <summary>What <see cref="BakeEnvironment"/> made: whether the whole environment
        /// lighting was made as measured (default: not made), and the skybox's faces its default
        /// reflection was made of (its size, and red, green and blue floats, face by face), or
        /// null.</summary>
        internal readonly record struct EnvironmentBake(bool Made, (int Size, float[] Faces)? Drawn);

        /// <summary>The image a bake writes its default reflection to, in the scene's folder.</summary>
        const string ReflectionImage = "ReflectionProbe-0.exr";
        /// <summary>The lighting data's file and its LightingDataAsset's file ID.</summary>
        const string LightingDataFile = "LightingData.asset";
        const long LightingDataFileId = 112000000;
        /// <summary>The file ID of the cube a reflection's image imports to.</summary>
        const long ReflectionCubeFileId = 8900000;

        /// <summary>
        /// The files a script's bake writes beside its scene, as the Unity 6000.0.71f1 Editor writes
        /// them (tests/projects/bakeenvironment `written/`): the scene's folder (its name), the
        /// default reflection's image (ReflectionProbe-0.exr, the skybox's faces side by side as
        /// half floats; imported as a cube convolved for glossy reflection, clamped, trilinear),
        /// which the scene then reflects, or none (one an earlier bake wrote is deleted), and the
        /// lighting data (LightingData.asset), which the scene then names. Written for a bake of
        /// the environment alone (baked and realtime global illumination off) that made all of
        /// it, of a scene of the project's own assets, in the measured release family, without a
        /// render pipeline asset (<see cref="Unwritten"/>); otherwise
        /// nothing is written or deleted (said once): the lighting data of other bakes is not
        /// written yet. The files are the Editor's but for the GUIDs of the new assets.
        /// </summary>
        internal static void WriteLightingData(Unbound.Runtime.SceneData scene, LoadedFile file, LightingSettings settings, EnvironmentBake environment,
            BakeResult baked = null)
        {
            scene.LightingDataWritten = false;
            string folder = FolderOf(scene);
            // (Without the native side, as in managed tests, nothing is made or written.)
            string unwritten = Unwritten(settings, folder, Native.IsReady && environment.Made, Rendering.GraphicsSettings.currentRenderPipeline != null,
                baked?.Images is { Length: > 0 }, file != null && HasProbeGroups(file));
            if (unwritten != null)
            {
                string name = folder == null ? "an untitled scene" : scene.Path;
                Warn("script-bake-asset:" + name, $"{name}: Lightmapping.Bake writes its lighting data (LightingData.asset beside it) in the Editor, which the scene then names; {unwritten}: Unbound keeps this bake for the session (LibraryUnbound/RealtimeGI) without writing it.");
                return;
            }
            var faces = environment.Drawn;
            if (!UnityEditor.AssetDatabase.IsValidFolder(folder))
            {
                int slash = folder.LastIndexOf('/');
                UnityEditor.AssetDatabase.CreateFolder(folder.Substring(0, slash), folder.Substring(slash + 1));
            }
            var (probe, reflection) = RenderSettings.BakedEnvironment(file);
            var imagePath = folder + "/" + ReflectionImage;
            string imageGuid = null;
            if (faces is { } drawn)
            {
                var floats = new byte[drawn.Faces.Length * sizeof(float)];
                Buffer.BlockCopy(drawn.Faces, 0, floats, 0, floats.Length);
                var reply = JsonDocument.Parse(Native.Call("baked-gi-reflection-image", JsonSerializer.Serialize(new { size = drawn.Size, faces = Convert.ToBase64String(floats) }))).RootElement;
                imageGuid = UnityEditor.AssetDatabase.WriteBakedFile(imagePath, Convert.FromBase64String(reply.GetProperty("image").GetString()));
                if (UnityEditor.AssetImporter.GetAtPath(imagePath) is UnityEditor.TextureImporter importer)
                {
                    var import = new UnityEditor.TextureImporterSettings();
                    importer.ReadTextureSettings(import);
                    import.textureShape = UnityEditor.TextureImporterShape.TextureCube;
                    import.generateCubemap = UnityEditor.TextureImporterGenerateCubemap.AutoCubemap;
                    import.cubemapConvolution = UnityEditor.TextureImporterCubemapConvolution.Specular;
                    import.seamlessCubemap = true;
                    import.wrapMode = TextureWrapMode.Clamp;
                    import.filterMode = FilterMode.Trilinear;
                    import.aniso = 0;
                    importer.SetTextureSettings(import);
                    importer.compressionQuality = 100;
                    importer.SaveAndReimport();
                }
            }
            else if (UnityEditor.AssetDatabase.AssetPathToGUID(imagePath).Length > 0)
                UnityEditor.AssetDatabase.DeleteAsset(imagePath);
            var (lightmapGuids, renderers, lights) = WriteLightmaps(folder, baked);
            var coefficients = new float[27];
            if (probe is { } sh)
                for (int c = 0; c < 3; c++)
                    for (int k = 0; k < 9; k++) coefficients[9 * c + k] = sh[c, k];
            var data = JsonDocument.Parse(Native.Call("baked-gi-lighting-data", JsonSerializer.Serialize(new
            {
                unityVersion = Application.unityVersion,
                sceneGuid = UnityEditor.AssetDatabase.AssetPathToGUID(scene.Path),
                lightmapsMode = (int)settings.directionalityMode,
                ambientProbe = coefficients,
                reflectionGuid = string.IsNullOrEmpty(imageGuid) ? null : imageGuid,
                lightmaps = lightmapGuids,
                renderers,
                lights,
            }))).RootElement;
            var dataGuid = UnityEditor.AssetDatabase.WriteBakedFile(folder + "/" + LightingDataFile, Convert.FromBase64String(data.GetProperty("data").GetString()), LightingDataFileId);
            if (string.IsNullOrEmpty(dataGuid)) return;
            scene.LightingDataRef = new ObjectRef(LightingDataFileId, dataGuid, 2);
            scene.LightingDataWritten = true;
            // The scene reflects the written image, as the Editor's does.
            if (!string.IsNullOrEmpty(imageGuid) && RenderSettings.BakedReflection(imageGuid, ReflectionCubeFileId) is { } imported)
            {
                if (reflection != null && s_MadeReflections.Remove(reflection))
                    Object.DestroyImmediate(reflection);
                RenderSettings.SetBakedEnvironment(file, probe, imported);
                RememberEnvironment(scene.Path, probe, imported);
            }
        }

        /// <summary>What a bake does with a part of a scene's environment lighting.</summary>
        internal enum EnvironmentPart
        {
            /// <summary>Made of the sky.</summary>
            Made,
            /// <summary>Taken away: the render pipeline does not bake it (a zero probe, no
            /// reflection; tests/projects/bakeenvironment EnvNoBaking).</summary>
            Cleared,
            /// <summary>Left as the scene had it: a Custom source (not measured).</summary>
            Kept,
        }

        /// <summary>What a bake does with the ambient probe and with the default reflection: each
        /// is taken away where the render pipeline does not bake it (SupportedRenderingFeatures'
        /// ambientProbeBaking and defaultReflectionProbeBaking), left as it was with a Custom
        /// source, else made.</summary>
        internal static (EnvironmentPart Probe, EnvironmentPart Reflection) BakedParts(Rendering.SupportedRenderingFeatures features, Rendering.AmbientMode ambient, Rendering.DefaultReflectionMode reflection) =>
            (!features.ambientProbeBaking ? EnvironmentPart.Cleared : ambient == Rendering.AmbientMode.Custom ? EnvironmentPart.Kept : EnvironmentPart.Made,
             !features.defaultReflectionProbeBaking ? EnvironmentPart.Cleared : reflection != Rendering.DefaultReflectionMode.Skybox ? EnvironmentPart.Kept : EnvironmentPart.Made);

        /// <summary>The skybox's default reflection at <paramref name="size"/> texels a side: its
        /// faces stored as half floats and glossy-convolved, every level down to one texel, in a
        /// readable RGBAHalf cube (the Editor's imported one is BC6H and not readable).</summary>
        static Cubemap DefaultReflection(int size, float[] faces)
        {
            var bytes = new byte[faces.Length * sizeof(float)];
            Buffer.BlockCopy(faces, 0, bytes, 0, bytes.Length);
            var json = Native.Call("baked-gi-default-reflection", JsonSerializer.Serialize(new { size, faces = Convert.ToBase64String(bytes) }));
            var root = JsonDocument.Parse(json).RootElement;
            int levels = root.GetProperty("levels").GetInt32();
            var texels = Convert.FromBase64String(root.GetProperty("texels").GetString());
            var cube = new Cubemap(size, TextureFormat.RGBAHalf, levels) { name = "ReflectionProbe-0" };
            int at = 0;
            for (int level = 0; level < levels; level++)
            {
                int side = Math.Max(size >> level, 1);
                for (int face = 0; face < 6; face++)
                {
                    cube.SetPixelData(texels, level, (CubemapFace)face, at);
                    at += side * side * 8;
                }
            }
            cube.Apply(false, false);
            return cube;
        }

        /// <summary>Says once what the Editor's bake of a scene (<paramref name="what"/>: when it
        /// opens it without lighting data, or a script's) would make and Unbound does not: baked
        /// lightmaps and the light probes' baked lighting (baked global illumination on; a scene
        /// without Lighting Settings has the defaults', which have it on; Q677), the environment
        /// lighting from a sky (the ambient probe and the default reflection; a script's bake
        /// makes it), baked reflection probes, Adaptive Probe Volumes. Unbound makes the realtime
        /// GI alone (<see cref="Bake"/>).</summary>
        internal static void NoteBakeParts(LoadedFile file, LightingSettings settings, string what)
        {
            string path = file?.Path ?? "a scene";
            if (settings == null || settings.bakedGI)
                Warn("bake-baked:" + what + path, $"{path}: {what} would make baked lightmaps and the light probes' baked lighting, which Unbound does not make (Q677).");
            // (A script's bake makes it, BakeEnvironment.)
            if (what != ScriptBake && (RenderSettings.skybox != null || RenderSettings.ambientMode == Rendering.AmbientMode.Skybox))
                Warn("bake-environment:" + what + path, $"{path}: {what} would make the environment lighting from the sky (the ambient probe and the default reflection), which Unbound does not make.");
            if (file == null) return;
            bool reflection = false, volumes = false;
            foreach (var obj in file.Objects.Values)
            {
                if (!reflection && obj is ReflectionProbe probe && probe != null && probe.mode == Rendering.ReflectionProbeMode.Baked)
                {
                    reflection = true;
                    Warn("bake-reflection:" + what + path, $"{path}: {what} would bake its reflection probes, which Unbound does not make.");
                }
                // Adaptive Probe Volumes (a render pipeline's ProbeVolume components, active and
                // enabled, where the pipeline has them in place of light probe groups:
                // SupportedRenderingFeatures.overridesLightProbeSystem): the Editor's bake writes
                // their Baking Set's cell data. (A pipeline says so when it first renders: a bake
                // on its first scene's load comes before and does not say it.)
                if (!volumes && (settings == null || settings.bakedGI) && Rendering.SupportedRenderingFeatures.active.overridesLightProbeSystem
                    && obj is MonoBehaviour behaviour && behaviour != null && behaviour.isActiveAndEnabled
                    && behaviour.GetType().FullName == AdaptiveProbeVolumeType)
                {
                    volumes = true;
                    Warn("bake-apv:" + what + path, $"{path}: {what} would bake its Adaptive Probe Volumes (their Baking Set's cell data), which Unbound does not make.");
                }
            }
        }

        /// <summary>The component of an Adaptive Probe Volume (the Scriptable Render Pipeline
        /// Core package's).</summary>
        internal const string AdaptiveProbeVolumeType = "UnityEngine.Rendering.ProbeVolume";

        /// <summary>The renderers of the scene's LOD Groups below their most detailed level: realtime
        /// GI lights only the most detailed one (Unity 6000.0 Manual, "Lightmapping LODs": "the
        /// system lights the most detailed model out of the LOD Group as if it has the Contribute
        /// Global Illumination setting enabled", lower LODs take their indirect light from light
        /// probes).</summary>
        static HashSet<Renderer> LowerLods(LoadedFile file)
        {
            var lower = new HashSet<Renderer>();
            foreach (var obj in file.Objects.Values)
            {
                if (!(obj is LODGroup group)) continue;
                var lods = group.GetLODs();
                for (int level = 1; level < lods.Length; level++)
                    foreach (var r in lods[level].renderers)
                        if (r != null) lower.Add(r);
                // A renderer at the most detailed level as well stays in.
                if (lods.Length > 0)
                    foreach (var r in lods[0].renderers)
                        if (r != null) lower.Remove(r);
            }
            return lower;
        }

        /// <summary>The Lightmap Parameters the bake takes: the realtime fields of the built-in
        /// Default-Medium asset (tests/projects/realtimegi's scenes use it, and its
        /// RoomSystemsSystemTag1 copy of it); of these the bake takes the Cluster Resolution, the
        /// Irradiance Budget and Quality, the Modelling Tolerance and the System Tag as they are
        /// and refuses other values of the rest.</summary>
        internal struct BakeParameters
        {
            internal float Resolution, ClusterResolution, BackFaceTolerance, ModellingTolerance, Pushoff;
            internal int IrradianceBudget, IrradianceQuality, SystemTag;
            internal bool IsTransparent, EdgeStitching;

            internal static BakeParameters DefaultMedium => new BakeParameters
            {
                Resolution = 1f, ClusterResolution = 0.5f, IrradianceBudget = 128, IrradianceQuality = 8192,
                BackFaceTolerance = 0.9f, IsTransparent = false, ModellingTolerance = 0.01f, SystemTag = -1,
                EdgeStitching = true, Pushoff = 0.0001f,
            };

            /// <summary>Why the bake cannot take these parameters, or null.</summary>
            internal string Refusal()
            {
                var d = DefaultMedium;
                if (Resolution != d.Resolution) return $"a Resolution of {Resolution} (only {d.Resolution} is measured)";
                if (BackFaceTolerance != d.BackFaceTolerance) return $"a Backface Tolerance of {BackFaceTolerance} (only {d.BackFaceTolerance} is measured)";
                if (IsTransparent) return "Is Transparent (not measured)";
                if (!EdgeStitching) return "Edge Stitching off (not measured)";
                if (Pushoff != d.Pushoff) return $"a Pushoff of {Pushoff} (only {d.Pushoff} is measured)";
                if (!(ClusterResolution > 0f) || !(IrradianceBudget > 0) || !(IrradianceQuality > 0))
                    return $"a Cluster Resolution of {ClusterResolution}, an Irradiance Budget of {IrradianceBudget} and Quality of {IrradianceQuality}";
                return null;
            }

            /// <summary>What makes renderers share a system (all the realtime fields).</summary>
            internal ulong Group()
            {
                ulong h = 1469598103934665603UL;
                void Mix(long v) { h = (h ^ (ulong)v) * 1099511628211UL; }
                Mix(BitConverter.SingleToInt32Bits(Resolution));
                Mix(BitConverter.SingleToInt32Bits(ClusterResolution));
                Mix(BitConverter.SingleToInt32Bits(BackFaceTolerance));
                Mix(BitConverter.SingleToInt32Bits(ModellingTolerance));
                Mix(BitConverter.SingleToInt32Bits(Pushoff));
                Mix(IrradianceBudget);
                Mix(IrradianceQuality);
                Mix(SystemTag);
                Mix(IsTransparent ? 1 : 0);
                Mix(EdgeStitching ? 1 : 0);
                return h;
            }
        }

        static readonly Dictionary<(string, long), BakeParameters?> s_Parameters = new Dictionary<(string, long), BakeParameters?>();

        /// <summary>The Lightmap Parameters asset a renderer's reference stands for: its own, else
        /// the scene's (none at all: Default-Medium).</summary>
        static ObjectRef EffectiveParameters(ObjectRef reference, LightingSettings settings) =>
            reference.IsNull && settings != null ? settings.DefaultLightmapParameters : reference;

        /// <summary>The Lightmap Parameters a reference names (<see cref="EffectiveParameters"/>),
        /// or null when they cannot be read.</summary>
        static BakeParameters? ParametersOf(ObjectRef reference, LightingSettings settings)
        {
            reference = EffectiveParameters(reference, settings);
            if (!OwnParameters(reference)) return BakeParameters.DefaultMedium;
            // The other built-in assets (Default-HighResolution and the lower ones) are not measured.
            if (reference.Guid == BuiltinExtraGuid) return null;
            var key = (reference.Guid, reference.FileId);
            if (s_Parameters.TryGetValue(key, out var known)) return known;
            BakeParameters? read = null;
            try
            {
                foreach (var doc in Native.LoadDocuments(reference.Guid))
                {
                    if (doc.ClassId != 1113 || doc.FileId != reference.FileId) continue;
                    var b = doc.Body;
                    read = new BakeParameters
                    {
                        Resolution = b.Float("resolution", 1f),
                        ClusterResolution = b.Float("clusterResolution", 0.5f),
                        IrradianceBudget = b.Int("irradianceBudget", 128),
                        IrradianceQuality = b.Int("irradianceQuality", 8192),
                        BackFaceTolerance = b.Float("backFaceTolerance", 0.9f),
                        IsTransparent = b.Bool("isTransparent"),
                        ModellingTolerance = b.Float("modellingTolerance", 0.01f),
                        SystemTag = b.Int("systemTag", -1),
                        EdgeStitching = b.Int("edgeStitching", 1) != 0,
                        Pushoff = b.Float("pushoff", 0.0001f),
                    };
                }
            }
            catch (Exception) { read = null; }
            s_Parameters[key] = read;
            return read;
        }

        /// <summary>The scene's realtime GI made by Unbound's own precompute from its renderers
        /// (what <c>realtime-gi-load</c> answers for the Editor's data, with the renderers it
        /// left out), or null when there is nothing to bake or the bake failed (said once).
        /// Its light probes, those of its LightProbeGroups, become the scene's.</summary>
        static JsonElement? Bake(int scene, LoadedFile file, LightingSettings settings)
        {
            float resolution = settings.indirectResolution;
            var renderers = new List<object>();
            var refused = new List<string>();
            var probes = new List<float>();
            var probePositions = new List<Vector3>();
            var lowerLods = LowerLods(file);
            foreach (var pair in file.Objects)
            {
                if (pair.Value is Terrain terrain && terrain.enabled && terrain.gameObject.activeInHierarchy
                    && (terrain.gameObject.m_StaticEditorFlags & ContributeGI) != 0)
                    refused.Add($"'{terrain.name}': a Terrain (not made by Unbound's precompute)");
                if (LightProbeGroup.TakenByBake(pair.Value, out var group))
                    foreach (var p in group.WorldProbePositions())
                    {
                        probePositions.Add(p);
                        probes.Add(p.x); probes.Add(p.y); probes.Add(p.z);
                    }
                // (A renderer destroyed since the scene loaded is not baked.)
                if (!(pair.Value is MeshRenderer renderer) || renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (!TakesContributor(file.Path, pair.Key, renderer, lowerLods)) continue;
                if (BakeRequestOf(pair.Key, renderer, resolution, settings, out var why) is object request) renderers.Add(request);
                else refused.Add($"'{renderer.name}': {why}");
            }
            foreach (var why in refused)
                Warn("bake:" + file.Path + why, $"{file.Path}: a renderer is left out of Unbound's realtime GI bake: {why}");
            if (renderers.Count == 0)
            {
                if (probePositions.Count > 0)
                    Warn("bake-probes:" + file.Path, $"{file.Path}: its bake on load would light its light probes from realtime GI; it has no renderer Unbound's precompute takes, so they are not lit.");
                return null;
            }
            JsonElement root;
            try
            {
                var json = Native.Call("realtime-gi-bake", JsonSerializer.Serialize(new { renderers, probes }));
                root = JsonDocument.Parse(json).RootElement;
            }
            catch (Exception e)
            {
                Warn("bake-failed:" + file.Path, $"{file.Path}: Unbound's realtime GI bake of it failed, so it has no realtime GI: {e.Message}");
                return null;
            }
            if (root.TryGetProperty("refused", out var left))
                foreach (var r in left.EnumerateArray())
                {
                    var name = BakedLighting.Find(file, r)?.name ?? "?";
                    Warn("bake-refused:" + file.Path + name, $"{file.Path}: '{name}' is left out of Unbound's realtime GI bake: {r.GetProperty("error").GetString()}");
                }
            if (probePositions.Count > 0)
            {
                var set = new ProbeSet
                {
                    Positions = probePositions.ToArray(),
                    Probes = new Rendering.SphericalHarmonicsL2[probePositions.Count],
                    Occlusion = new Vector4[probePositions.Count],
                    Baked = false,
                };
                for (int i = 0; i < set.Occlusion.Length; i++) set.Occlusion[i] = Vector4.one;
                ProbeTetrahedralization.Build(set);
                LightProbes.SceneLoaded(scene, LightProbes.FromSet(set));
            }
            return root;
        }

        /// <summary>Whether the bake takes an enabled renderer of an active GameObject: one with
        /// Contribute Global Illumination that is not below the most detailed level of a LOD Group.
        /// What the Editor's realtime GI makes of a contributor lit by light probes is not measured:
        /// Unbound bakes it as one with a realtime lightmap, as before it read Receive Global
        /// Illumination, and says so once.</summary>
        internal static bool TakesContributor(string path, long fileId, MeshRenderer renderer, HashSet<Renderer> lowerLods)
        {
            if ((renderer.gameObject.m_StaticEditorFlags & ContributeGI) == 0 || lowerLods.Contains(renderer)) return false;
            if (renderer.receiveGI != ReceiveGI.Lightmaps)
                Warn("bake-probe-lit:" + path + fileId, $"{path}: '{renderer.name}' contributes to global illumination and is lit by light probes; Unbound's realtime GI bake gives it a realtime lightmap (what the Editor does is not measured).");
            return true;
        }

        /// <summary>A renderer of the bake as the native side takes it (its mesh, its matrix and
        /// the settings that make its realtime lightmap UVs and clusters), or null with why it
        /// cannot be baked.</summary>
        static object BakeRequestOf(long id, MeshRenderer renderer, float resolution, LightingSettings settings, out string why)
        {
            using var engineReads = Mesh.EngineReads();
            why = null;
            var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null) { why = "it has no mesh"; return null; }
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
                if (mesh.GetTopology(sub) != MeshTopology.Triangles) { why = $"a submesh of {mesh.GetTopology(sub)}"; return null; }
            float scale = RealtimeTexelsPerUnit(renderer, resolution);
            if (!(scale > 0f)) { why = "it has no realtime lightmap resolution (Scale In Lightmap 0 is not measured)"; return null; }
            if (!(ParametersOf(renderer.m_LightmapParameters, settings) is BakeParameters parameters))
            {
                why = EffectiveParameters(renderer.m_LightmapParameters, settings).Guid == BuiltinExtraGuid
                    ? "its built-in Lightmap Parameters are not Default-Medium (not measured)"
                    : "its Lightmap Parameters cannot be read";
                return null;
            }
            if (parameters.Refusal() is string refusal) { why = "its Lightmap Parameters have " + refusal; return null; }
            bool automatic = MakesAutomaticCharts(renderer);
            if (automatic && AutomaticChartsRefusal(renderer, mesh, s_NoRenderers) is string charts) { why = charts; return null; }
            var vertices = mesh.vertices;
            var normals = mesh.normals;
            var source = mesh.uv3;
            if (source.Length != vertices.Length) source = mesh.uv2;
            if (source.Length != vertices.Length) source = mesh.uv;
            if (source.Length != vertices.Length) { why = "it has no lightmap UVs"; return null; }
            var positions = new float[3 * vertices.Length];
            var normalValues = new float[normals.Length == vertices.Length ? 3 * vertices.Length : 0];
            var uvs = new float[2 * vertices.Length];
            for (int v = 0; v < vertices.Length; v++)
            {
                positions[3 * v] = vertices[v].x; positions[3 * v + 1] = vertices[v].y; positions[3 * v + 2] = vertices[v].z;
                uvs[2 * v] = source[v].x; uvs[2 * v + 1] = source[v].y;
                if (normalValues.Length > 0)
                {
                    normalValues[3 * v] = normals[v].x; normalValues[3 * v + 1] = normals[v].y; normalValues[3 * v + 2] = normals[v].z;
                }
            }
            var m = renderer.transform.localToWorldMatrix;
            var matrix = new float[16];
            for (int c = 0; c < 4; c++)
                for (int r = 0; r < 4; r++) matrix[4 * c + r] = m[r, c];
            return new
            {
                @object = id,
                prefab = 0L,
                positions,
                normals = normalValues,
                uvs,
                triangles = mesh.triangles,
                localToWorld = matrix,
                pixelSize = 1f / scale,
                minChartTexels = renderer.m_MinimumChartSize,
                automatic = automatic
                    ? new { matrix = WorldScale(renderer.transform), maxDistance = renderer.m_AutoUVMaxDistance, maxAngle = renderer.m_AutoUVMaxAngle, ignoreNormals = renderer.m_IgnoreNormalsForChartDetection }
                    : null,
                clusterSize = 1f / (scale * parameters.ClusterResolution),
                modellingTolerance = parameters.ModellingTolerance,
                group = parameters.Group(),
                rays = parameters.IrradianceQuality,
                budget = parameters.IrradianceBudget,
            };
        }
    }
}
