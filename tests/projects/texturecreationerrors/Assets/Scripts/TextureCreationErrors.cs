using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Object = UnityEngine.Object;

public static class TextureCreationErrors
{
    const string OutputVariable = "TEXTURE_CREATION_ERRORS_OUT";

    [Serializable]
    public sealed class Row
    {
        public string k;
        public string v;
    }

    sealed class CaptureHandler : ILogHandler
    {
        readonly ILogHandler m_Wrapped;
        public readonly List<string> Logs = new List<string>();

        public CaptureHandler(ILogHandler wrapped) => m_Wrapped = wrapped;

        public void LogFormat(LogType type, Object context, string format, params object[] args)
        {
            Logs.Add(type + ": " + string.Format(CultureInfo.InvariantCulture, format, args));
            m_Wrapped.LogFormat(type, context, format, args);
        }

        public void LogException(Exception error, Object context)
        {
            Logs.Add(error.GetType().Name + ": " + error.Message);
            m_Wrapped.LogException(error, context);
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Record()
    {
        string output = Environment.GetEnvironmentVariable(OutputVariable);
        if (string.IsNullOrEmpty(output)) return;
        var rows = new List<string>();
        var engine = new List<string>();
        var previous = Debug.unityLogger.logHandler;
        var handler = new CaptureHandler(previous);
        Debug.unityLogger.logHandler = handler;
        void Logged(string text, string trace, LogType type) => engine.Add(type + ": " + text);
        Application.logMessageReceived += Logged;

        void Case(string name, Func<string> run)
        {
            engine.Clear();
            handler.Logs.Clear();
            string value;
            try { value = run(); }
            catch (Exception error) { value = error.GetType().Name + ": " + error.Message; }
            rows.Add(JsonUtility.ToJson(new Row { k = name, v = value + " | app=[" +
                string.Join("; ", engine) + "] logger=[" + string.Join("; ", handler.Logs) + "]" }));
        }

        string DestroyAndDescribe(Texture2D texture)
        {
            if (texture == null) return "null";
            string size = texture.width + "x" + texture.height;
            Object.DestroyImmediate(texture);
            return size;
        }

        try
        {
            var formats = new[] { TextureFormat.DXT1, TextureFormat.DXT5, TextureFormat.DXT1Crunched,
                TextureFormat.DXT5Crunched, TextureFormat.BC4, TextureFormat.BC5, TextureFormat.BC6H,
                TextureFormat.BC7, TextureFormat.ETC_RGB4, TextureFormat.ETC2_RGB, TextureFormat.ETC2_RGBA1,
                TextureFormat.ETC2_RGBA8, TextureFormat.ETC_RGB4Crunched, TextureFormat.ETC2_RGBA8Crunched,
                TextureFormat.EAC_R, TextureFormat.EAC_RG, TextureFormat.ASTC_6x6,
                TextureFormat.EAC_R_SIGNED, TextureFormat.EAC_RG_SIGNED };
            foreach (var format in formats)
                foreach (bool linear in new[] { false, true })
                {
                    string name = format + (linear ? ".linear" : ".srgb");
                    foreach (var (width, height) in new[] { (1, 1), (4, 7), (7, 4), (30, 18), (8, 8) })
                        Case(name + ".new." + width + "x" + height, () => DestroyAndDescribe(
                            new Texture2D(width, height, format, false, linear)));
                }

            Case("side.zero", () => DestroyAndDescribe(new Texture2D(0, 8, TextureFormat.DXT1, false)));
            Case("RGBA32.new.7x4", () => DestroyAndDescribe(new Texture2D(7, 4, TextureFormat.RGBA32, false)));
            foreach (var format in new[] { GraphicsFormat.RGBA_DXT1_UNorm, GraphicsFormat.RGBA_DXT1_SRGB })
                Case(format + ".new", () => DestroyAndDescribe(new Texture2D(4, 7, format, TextureCreationFlags.None)));
            foreach (var format in new[] { GraphicsFormat.RGBA_DXT3_UNorm, GraphicsFormat.RGBA_DXT3_SRGB,
                GraphicsFormat.R_EAC_SNorm, GraphicsFormat.RG_EAC_SNorm, GraphicsFormat.R_BC4_SNorm })
                foreach (var (width, height) in new[] { (4, 7), (7, 4), (8, 8) })
                    Case(format + ".new." + width + "x" + height, () => DestroyAndDescribe(
                        new Texture2D(width, height, format, TextureCreationFlags.None)));
        }
        finally
        {
            Debug.unityLogger.logHandler = previous;
            Application.logMessageReceived -= Logged;
        }

        rows.Add("{\"k\":\"complete\",\"v\":true}");
        File.WriteAllLines(Path.Combine(output, "values.jsonl"), rows);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit(0);
#endif
    }
}
