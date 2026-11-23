using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// The capture (`environment_bake.py native|engine`, <c>ENVIRONMENT_OUT</c> set): opens each
/// scene of <c>Assets/Scenes</c> in Edit mode, bakes it with <see cref="Lightmapping.Bake"/> and
/// writes to <c>&lt;out&gt;/&lt;scene&gt;/</c>:
/// <para><c>record.txt</c>: the bake's answer, the ambient probe (RenderSettings.ambientProbe,
/// 27 numbers, red, green and blue) and the probe the bake stored (read in the Custom ambient
/// mode), the default reflection (ReflectionProbe.defaultTexture:
/// its name, type, size, levels and format; for one of the project's assets, its file), the
/// files of the scene's folder (what the bake wrote) and the lighting data the scene names;
/// <c>cube.f32</c>: every texel of every level of the default reflection, level by level and
/// face by face (+x, -x, +y, -y, +z, -z), as 32-bit floats of RGBA. The Editor's is an imported
/// asset that cannot be read: it is imported again readable and uncompressed first (its import
/// settings as the bake wrote them are kept as <c>import.txt</c>); a built-in one is not read.
/// What is logged during the bake goes to <c>messages.txt</c>, and the files the bake wrote
/// beside the scene, as it wrote them, to <c>written/</c> (the folder's .meta as
/// <c>folder.meta</c>).</para>
/// <para>In the Editor it runs with <c>-executeMethod EnvironmentRecord.Record</c>; with
/// <c>ENVIRONMENT_AUTO</c> set (Unbound's <c>unbound test</c>) it starts itself once Edit mode
/// has run a few updates.</para>
/// </summary>
[InitializeOnLoad]
public static class EnvironmentRecord
{
    static EnvironmentRecord()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ENVIRONMENT_OUT"))) return;
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ENVIRONMENT_AUTO"))) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        int updates = 0;
        EditorApplication.CallbackFunction wait = null;
        wait = () =>
        {
            if (++updates < 30) return;
            EditorApplication.update -= wait;
            Record();
        };
        EditorApplication.update += wait;
    }

    static string Number(float v) => v.ToString("R", CultureInfo.InvariantCulture);

    public static void Record()
    {
        var root = Environment.GetEnvironmentVariable("ENVIRONMENT_OUT");
        var log = new List<string>();
        try
        {
            foreach (var path in Directory.GetFiles("Assets/Scenes", "*.unity").OrderBy(p => p, StringComparer.Ordinal))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                var folder = Path.Combine(root, name);
                Directory.CreateDirectory(folder);
                EditorSceneManager.OpenScene(path);
                // `*ThenNone`: baked with its skybox first, then without one (the bake replaces
                // the first bake's reflection).
                if (name.EndsWith("ThenNone", StringComparison.Ordinal))
                {
                    Lightmapping.Bake();
                    RenderSettings.skybox = null;
                }
                // `EnvNoBaking`: a render pipeline that bakes neither the ambient probe nor the
                // default reflection (SupportedRenderingFeatures), for the bake.
                var features = UnityEngine.Rendering.SupportedRenderingFeatures.active;
                if (name == "EnvNoBaking")
                    UnityEngine.Rendering.SupportedRenderingFeatures.active = new UnityEngine.Rendering.SupportedRenderingFeatures
                        { ambientProbeBaking = false, defaultReflectionProbeBaking = false };
                var said = new List<string>();
                Application.LogCallback listen = (message, stack, type) => said.Add(type + ": " + message);
                Application.logMessageReceived += listen;
                bool baked;
                try { baked = Lightmapping.Bake(); }
                finally
                {
                    Application.logMessageReceived -= listen;
                    UnityEngine.Rendering.SupportedRenderingFeatures.active = features;
                }
                File.WriteAllLines(Path.Combine(folder, "messages.txt"), said);
                // The files the bake wrote beside the scene (its folder's .meta too), as it wrote
                // them: copied before the reflection is imported again below.
                var written = Path.Combine(folder, "written");
                Directory.CreateDirectory(written);
                var bakeFolder = Path.Combine(Path.GetDirectoryName(path), name);
                if (File.Exists(bakeFolder + ".meta")) File.Copy(bakeFolder + ".meta", Path.Combine(written, "folder.meta"), true);
                if (Directory.Exists(bakeFolder))
                    foreach (var file in Directory.GetFiles(bakeFolder))
                        File.Copy(file, Path.Combine(written, Path.GetFileName(file)), true);

                var lines = new List<string> { "bake: " + baked };
                var sh = RenderSettings.ambientProbe;
                var coefficients = new List<string>();
                for (int c = 0; c < 3; c++)
                    for (int k = 0; k < 9; k++) coefficients.Add(Number(sh[c, k]));
                lines.Add("ambientProbe: " + string.Join(" ", coefficients));
                // The probe the bake stored, whatever the ambient source: the Custom mode gives it
                // as it is.
                var mode = RenderSettings.ambientMode;
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Custom;
                var stored = RenderSettings.ambientProbe;
                RenderSettings.ambientMode = mode;
                coefficients.Clear();
                for (int c = 0; c < 3; c++)
                    for (int k = 0; k < 9; k++) coefficients.Add(Number(stored[c, k]));
                lines.Add("storedProbe: " + string.Join(" ", coefficients));
                var sceneFolder = Path.Combine(Path.GetDirectoryName(path), name);
                var files = Directory.Exists(sceneFolder)
                    ? Directory.GetFiles(sceneFolder).Select(Path.GetFileName).OrderBy(f => f, StringComparer.Ordinal)
                    : Enumerable.Empty<string>();
                lines.Add("files: " + string.Join(" ", files));
                var data = Lightmapping.lightingDataAsset;
                lines.Add("lightingData: " + (data == null ? "none" : AssetDatabase.GetAssetPath(data)));
                var texture = ReflectionProbe.defaultTexture;
                if (texture == null)
                    lines.Add("default: none");
                else
                {
                    var asset = AssetDatabase.GetAssetPath(texture);
                    lines.Add($"default: {texture.name} {texture.GetType().Name} {texture.width} levels {texture.mipmapCount}");
                    lines.Add("defaultFormat: " + texture.graphicsFormat);
                    lines.Add("defaultFile: " + (asset.StartsWith("Assets/", StringComparison.Ordinal) ? asset : "none"));
                    var cube = texture as Cubemap;
                    if (cube != null && !cube.isReadable && asset.StartsWith("Assets/", StringComparison.Ordinal))
                    {
                        File.Copy(asset + ".meta", Path.Combine(folder, "import.txt"), true);
                        var importer = (TextureImporter)AssetImporter.GetAtPath(asset);
                        importer.isReadable = true;
                        importer.textureCompression = TextureImporterCompression.Uncompressed;
                        importer.SaveAndReimport();
                        cube = AssetDatabase.LoadAssetAtPath<Cubemap>(asset);
                    }
                    if (cube != null && cube.isReadable)
                        using (var w = new BinaryWriter(File.Create(Path.Combine(folder, "cube.f32"))))
                            for (int level = 0; level < cube.mipmapCount; level++)
                                for (int face = 0; face < 6; face++)
                                    foreach (var p in cube.GetPixels((CubemapFace)face, level))
                                    {
                                        w.Write(p.r);
                                        w.Write(p.g);
                                        w.Write(p.b);
                                        w.Write(p.a);
                                    }
                }
                File.WriteAllLines(Path.Combine(folder, "record.txt"), lines);
                log.Add(name + ": done");
            }
        }
        catch (Exception e)
        {
            log.Add("error: " + e);
        }
        File.WriteAllLines(Path.Combine(root, "log.txt"), log);
        EditorApplication.Exit(0);
    }
}
