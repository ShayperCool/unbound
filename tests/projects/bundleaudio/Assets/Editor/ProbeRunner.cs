using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// `-executeMethod ProbeRunner.Create` writes test tones as WAV files, imports them with each
/// compression the Editor offers for StandaloneLinux64 (Vorbis at a few qualities, ADPCM, PCM; mono
/// and stereo; decompressed on load, compressed in memory, streaming) and builds them into the
/// `audio` bundle under Reference/bundles. `-executeMethod ProbeRunner.Record` with
/// <c>AUDIO_PROBE_OUT</c> set plays the probe scene, where <see cref="AudioProbe"/> loads it.
/// </summary>
public static class ProbeRunner
{
    static void Wav(string path, int rate, int channels, float seconds, float frequency)
    {
        int frames = (int)(rate * seconds);
        using var w = new BinaryWriter(File.Create(path));
        int bytes = frames * channels * 2;
        w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes); w.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        w.Write(System.Text.Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)channels); w.Write(rate);
        w.Write(rate * channels * 2); w.Write((short)(channels * 2)); w.Write((short)16);
        w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(bytes);
        for (int i = 0; i < frames; i++)
            for (int c = 0; c < channels; c++)
            {
                double t = i / (double)rate;
                double v = 0.5 * System.Math.Sin(2 * System.Math.PI * frequency * (c + 1) * t) + 0.2 * System.Math.Sin(2 * System.Math.PI * 1234.5 * t);
                w.Write((short)System.Math.Round(v * 32000));
            }
    }

    static void Import(string name, int rate, int channels, AudioCompressionFormat format, float quality, AudioClipLoadType load)
    {
        var path = $"Assets/Bundled/{name}.wav";
        Wav(path, rate, channels, 0.75f, 440f);
        AssetDatabase.ImportAsset(path);
        var importer = (AudioImporter)AssetImporter.GetAtPath(path);
        var settings = importer.defaultSampleSettings;
        settings.compressionFormat = format;
        settings.quality = quality;
        settings.loadType = load;
        settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
        importer.defaultSampleSettings = settings;
        importer.assetBundleName = "audio";
        importer.SaveAndReimport();
    }

    public static void Create()
    {
        foreach (var path in new[] { "Assets/Bundled", "Assets/Scenes" })
            AssetDatabase.DeleteAsset(path);
        foreach (var dir in new[] { "Assets/Bundled", "Assets/Scenes" })
            Directory.CreateDirectory(dir);

        Import("vorbis100_mono_44100", 44100, 1, AudioCompressionFormat.Vorbis, 1f, AudioClipLoadType.DecompressOnLoad);
        Import("vorbis70_stereo_44100", 44100, 2, AudioCompressionFormat.Vorbis, 0.7f, AudioClipLoadType.DecompressOnLoad);
        Import("vorbis30_mono_22050", 22050, 1, AudioCompressionFormat.Vorbis, 0.3f, AudioClipLoadType.CompressedInMemory);
        Import("vorbis50_stereo_48000", 48000, 2, AudioCompressionFormat.Vorbis, 0.5f, AudioClipLoadType.Streaming);
        Import("adpcm_mono_44100", 44100, 1, AudioCompressionFormat.ADPCM, 1f, AudioClipLoadType.DecompressOnLoad);
        Import("adpcm_stereo_32000", 32000, 2, AudioCompressionFormat.ADPCM, 1f, AudioClipLoadType.DecompressOnLoad);
        Import("pcm_stereo_44100", 44100, 2, AudioCompressionFormat.PCM, 1f, AudioClipLoadType.DecompressOnLoad);
        Import("pcm_mono_11025", 11025, 1, AudioCompressionFormat.PCM, 1f, AudioClipLoadType.CompressedInMemory);
        Import("adpcm_mono_22050", 22050, 1, AudioCompressionFormat.ADPCM, 1f, AudioClipLoadType.CompressedInMemory);
        Import("vorbis60_mono_48000", 48000, 1, AudioCompressionFormat.Vorbis, 0.6f, AudioClipLoadType.DecompressOnLoad);

        var probeScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(probeScene, "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
        AssetDatabase.SaveAssets();

        var output = "Reference/bundles";
        if (Directory.Exists(output)) Directory.Delete(output, true);
        Directory.CreateDirectory(output);
        BuildPipeline.BuildAssetBundles(output, BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.DeterministicAssetBundle, BuildTarget.StandaloneLinux64);
    }

    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
