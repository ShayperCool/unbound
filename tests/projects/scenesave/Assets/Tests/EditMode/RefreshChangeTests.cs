using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>
/// Edit mode: files of imported assets changed on disk during the session (as a script or another
/// program changes them), then AssetDatabase.Refresh: a file only touched, a text file, a YAML
/// asset, an image and an image's .meta (an importer setting) changed; a material a scene object
/// uses changed (and the scene saved after); a UI Toolkit document changed; a change left without
/// Refresh for an editor update; a changed file imported by ImportAsset. Each step logs the
/// OnPostprocessAllAssets calls (SessionImportRecorder) and what the objects loaded before the
/// change answer after it: their content, whether they are still alive and whether loading the
/// asset again gives an equal object of the same instance id.
/// </summary>
public class RefreshChangeTests
{
    const string Root = "Assets/Refresh";
    const string Notes = Root + "/Notes.txt";
    const string Data = Root + "/Data.asset";
    const string Pic = Root + "/Pic.png";
    const string Mat = Root + "/Mat.mat";
    const string Doc = Root + "/Doc.uxml";

    static IEnumerator Step(string key, System.Action action)
    {
        SessionImportRecorder.Calls.Clear();
        try { action(); }
        catch (System.Exception e) { ProbeLog.Row(key + ".threw", e.GetType().Name + ": " + e.Message); }
        ProbeLog.Row(key, string.Join(" | ", SessionImportRecorder.Calls));
        SessionImportRecorder.Calls.Clear();
        yield return null;
        ProbeLog.Row(key + ".later", string.Join(" | ", SessionImportRecorder.Calls));
    }

    static byte[] Png(int size)
    {
        var texture = new Texture2D(size, size);
        var bytes = texture.EncodeToPNG();
        Object.DestroyImmediate(texture);
        return bytes;
    }

    static void Same(string key, Object before, Object now) =>
        ProbeLog.Row(key, (before != null ? "alive" : "dead") + "|" + (before == now ? "equal" : "unequal")
            + "|" + (now != null && before.GetInstanceID() == now.GetInstanceID() ? "same id" : "other id"));

    [UnityTest]
    public IEnumerator RefreshChanges()
    {
        AssetDatabase.CreateFolder("Assets", "Refresh");
        File.WriteAllText(Notes, "notes");
        File.WriteAllBytes(Pic, Png(4));
        AssetDatabase.Refresh();
        var data = ScriptableObject.CreateInstance<ProbeData>();
        data.count = 1;
        AssetDatabase.CreateAsset(data, Data);
        yield return null;

        var text = AssetDatabase.LoadAssetAtPath<TextAsset>(Notes);
        var asset = AssetDatabase.LoadAssetAtPath<ProbeData>(Data);
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Pic);
        ProbeLog.Row("start", text.text + "|" + asset.count + "|" + texture.width + "|" + texture.filterMode);

        yield return Step("touch", () =>
        {
            File.SetLastWriteTimeUtc(Notes, File.GetLastWriteTimeUtc(Notes).AddMinutes(5));
            AssetDatabase.Refresh();
        });

        yield return Step("text", () =>
        {
            File.WriteAllText(Notes, "changed notes");
            ProbeLog.Row("text.before", text.text);
            AssetDatabase.Refresh();
            ProbeLog.Row("text.after", text != null ? text.text : "dead");
            Same("text.object", text, AssetDatabase.LoadAssetAtPath<TextAsset>(Notes));
        });

        yield return Step("yaml", () =>
        {
            File.WriteAllText(Data, File.ReadAllText(Data).Replace("count: 1", "count: 7"));
            AssetDatabase.Refresh();
            ProbeLog.Row("yaml.after", asset != null ? asset.count.ToString() : "dead");
            Same("yaml.object", asset, AssetDatabase.LoadAssetAtPath<ProbeData>(Data));
        });

        yield return Step("image", () =>
        {
            File.WriteAllBytes(Pic, Png(8));
            AssetDatabase.Refresh();
            ProbeLog.Row("image.after", texture != null ? texture.width.ToString() : "dead");
            Same("image.object", texture, AssetDatabase.LoadAssetAtPath<Texture2D>(Pic));
        });

        yield return Step("meta", () =>
        {
            var meta = Pic + ".meta";
            File.WriteAllText(meta, File.ReadAllText(meta).Replace("    filterMode: 1", "    filterMode: 0"));
            AssetDatabase.Refresh();
            ProbeLog.Row("meta.after", texture != null ? texture.filterMode.ToString() : "dead");
            ProbeLog.Row("meta.importer", ((TextureImporter)AssetImporter.GetAtPath(Pic)).filterMode.ToString());
        });

        // A material a scene object uses, changed on disk: its content, and the scene saved after.
        var material = new Material(Shader.Find("Unlit/Color")) { color = Color.red };
        AssetDatabase.CreateAsset(material, Mat);
        material = AssetDatabase.LoadAssetAtPath<Material>(Mat);
        var user = GameObject.CreatePrimitive(PrimitiveType.Quad);
        user.GetComponent<MeshRenderer>().sharedMaterial = material;
        yield return Step("material", () =>
        {
            File.WriteAllText(Mat, File.ReadAllText(Mat).Replace("{r: 1, g: 0, b: 0, a: 1}", "{r: 0, g: 1, b: 0, a: 1}"));
            AssetDatabase.Refresh();
            ProbeLog.Row("material.after", material != null ? material.color.ToString() : "dead");
            Same("material.object", material, AssetDatabase.LoadAssetAtPath<Material>(Mat));
            ProbeLog.Row("material.user", user.GetComponent<MeshRenderer>().sharedMaterial == material);
        });
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(user.scene, Root + "/Scene.unity", true);
        ProbeLog.Row("material.scene", File.ReadAllText(Root + "/Scene.unity").Contains(AssetDatabase.AssetPathToGUID(Mat)) ? "references it" : "lost");
        Object.DestroyImmediate(user);

        // A UI Toolkit document changed on disk.
        File.WriteAllText(Doc, "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\"><ui:Label text=\"first\" /></ui:UXML>\n");
        AssetDatabase.Refresh();
        var document = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(Doc);
        yield return Step("document", () =>
        {
            File.WriteAllText(Doc, "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\"><ui:Label text=\"second\" /></ui:UXML>\n");
            AssetDatabase.Refresh();
            ProbeLog.Row("document.after", document != null ? document.CloneTree().Q<Label>().text : "dead");
            Same("document.object", document, AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(Doc));
        });

        yield return Step("unrefreshed", () => File.WriteAllText(Notes, "unrefreshed notes"));
        ProbeLog.Row("unrefreshed.text", text != null ? text.text : "dead");

        yield return Step("import", () =>
        {
            File.WriteAllText(Notes, "imported notes");
            AssetDatabase.ImportAsset(Notes);
            ProbeLog.Row("import.after", text != null ? text.text : "dead");
        });

        yield return Step("refresh-after-import", AssetDatabase.Refresh);
        AssetDatabase.DeleteAsset(Root);
    }
}
