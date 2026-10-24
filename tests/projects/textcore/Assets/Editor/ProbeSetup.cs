using System;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// `Unity -batchmode -projectPath tests/projects/textcore -executeMethod ProbeSetup.Create -quit`: copies
/// the probe's fonts into <c>Assets/Fonts</c> (Liberation Sans from the OS, Inter from the Editor's
/// resources; they are not committed), and makes the probe scene: a TextCoreProbe with the project
/// fonts and the Editor resource UI Toolkit uses by default (NotInter-Regular). The SHA-256 of each
/// font's data is kept with the probe, so a reference recorded from another file is recognized.
/// </summary>
public static class ProbeSetup
{
    const string NotInter = "UIPackageResources/Fonts/NotInter/NotInter-Regular.ttf";

    static readonly (string source, string asset)[] s_Fonts =
    {
        ("/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf", "Assets/Fonts/LiberationSans-Regular.ttf"),
        (EditorApplication.applicationContentsPath + "/Resources/Fonts/Inter-Regular.ttf", "Assets/Fonts/Inter-Regular.ttf"),
    };

    public static void Create()
    {
        foreach (var (source, asset) in s_Fonts)
        {
            if (!File.Exists(asset)) File.Copy(source, asset);
            AssetDatabase.ImportAsset(asset);
        }

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var go = new GameObject("TextCoreProbe");
        var probe = go.AddComponent<TextCoreProbe>();
        var fonts = new Font[s_Fonts.Length + 1];
        var hashes = new string[fonts.Length];
        for (int i = 0; i < s_Fonts.Length; i++)
        {
            fonts[i] = AssetDatabase.LoadAssetAtPath<Font>(s_Fonts[i].asset);
            hashes[i] = Sha256(File.ReadAllBytes(s_Fonts[i].asset));
        }
        var notInter = (Font)EditorGUIUtility.Load(NotInter);
        fonts[fonts.Length - 1] = notInter;
        hashes[fonts.Length - 1] = Sha256(FontData(notInter));
        File.WriteAllBytes("/tmp/uitk_text_notinter.ttf", FontData(notInter));
        probe.fonts = fonts;
        probe.fontHashes = hashes;
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
        AssetDatabase.SaveAssets();
    }

    static byte[] FontData(Font font)
    {
        var data = new SerializedObject(font).FindProperty("m_FontData");
        var bytes = new byte[data.arraySize];
        for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)data.GetArrayElementAtIndex(i).intValue;
        return bytes;
    }

    static string Sha256(byte[] data)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
    }
}
