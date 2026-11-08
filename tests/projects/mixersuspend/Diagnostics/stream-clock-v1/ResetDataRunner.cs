using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ResetDataRunner
{
    public static void Prepare() {
        Directory.CreateDirectory("Assets/Resources");
        foreach (string name in new[] { "reset_disk", "reset_modified", "reset_stream_preloaded", "reset_stream_deferred" }) {
            string path = "Assets/Resources/" + name + ".wav";
            using (var w = new BinaryWriter(File.Create(path))) {
                int frames = name.StartsWith("reset_stream_") ? 36000 : 4096;
                int bytes = frames * 2 * 2;
                w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes); w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                w.Write(16); w.Write((short)1); w.Write((short)2); w.Write(48000); w.Write(48000 * 4); w.Write((short)4); w.Write((short)16);
                w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(bytes);
                for (int i = 0; i < frames * 2; i++) w.Write((short)4096);
            }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (AudioImporter)AssetImporter.GetAtPath(path); var settings = importer.defaultSampleSettings;
            bool stream = name.StartsWith("reset_stream_");
            settings.loadType = stream ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = stream ? AudioCompressionFormat.Vorbis : AudioCompressionFormat.PCM;
            settings.preloadAudioData = !name.Contains("deferred");
            importer.defaultSampleSettings = settings; importer.forceToMono = false; importer.SaveAndReimport();
        }
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("ResetDataProbe").AddComponent<ResetDataProbe>(); Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), "Assets/Scenes/ResetData.unity");
        AssetDatabase.SaveAssets(); EditorApplication.Exit(0);
    }
    public static void Record() { EditorSceneManager.OpenScene("Assets/Scenes/ResetData.unity"); EditorApplication.EnterPlaymode(); }
}
