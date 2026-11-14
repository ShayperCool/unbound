using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
public class AssetAdapterProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod] static void Install() { new GameObject("AssetAdapterProbe").AddComponent<AssetAdapterProbe>(); }
    StreamWriter output;
    [Serializable] class Row { public string key; public string value; }
    void Put(string key, object value) => output.WriteLine(JsonUtility.ToJson(new Row { key = key, value = Convert.ToString(value, CultureInfo.InvariantCulture) }));
    void Start()
    {
        string path = Environment.GetEnvironmentVariable("ASSET_ADAPTER_OUT");
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            using (output = new StreamWriter(path)) { Record(); Put("complete", 1); }
            EditorApplication.Exit(0);
        }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }
    sealed class Capture : ILogHandler, IDisposable
    {
        readonly ILogHandler previous = Debug.unityLogger.logHandler;
        public readonly HashSet<string> messages = new HashSet<string>();
        public Capture() { Debug.unityLogger.logHandler = this; Application.logMessageReceived += Receive; }
        void Receive(string message, string stack, LogType type) { if (type == LogType.Warning || type == LogType.Error) messages.Add(type + ":" + message); }
        public void LogFormat(LogType type, Object context, string format, params object[] args)
        {
            Receive(string.Format(CultureInfo.InvariantCulture, format, args), "", type);
            previous.LogFormat(type, context, format, args);
        }
        public void LogException(Exception ex, Object context) => previous.LogException(ex, context);
        public void Dispose() { Application.logMessageReceived -= Receive; Debug.unityLogger.logHandler = previous; }
    }
    void Ranges()
    {
        var three = new PhysicsMaterial("range3d"); var two = new PhysicsMaterial2D("range2d");
        Action<float>[] setters = { v => three.dynamicFriction = v, v => three.staticFriction = v, v => three.bounciness = v, v => two.friction = v, v => two.bounciness = v };
        Func<float>[] getters = { () => three.dynamicFriction, () => three.staticFriction, () => three.bounciness, () => two.friction, () => two.bounciness };
        string[] names = { "dynamic3d", "static3d", "bounce3d", "friction2d", "bounce2d" };
        float[] values = { -2, -0f, 0, .5f, 2, 100001, float.MaxValue, float.PositiveInfinity, float.NegativeInfinity, float.NaN };
        using (var capture = new Capture())
        {
            for (int property = 0; property < setters.Length; property++)
                for (int value = 0; value < values.Length; value++)
                {
                    setters[property](.625f); capture.messages.Clear(); setters[property](values[value]);
                    string key = "range." + names[property] + "." + value;
                    Put(key + ".bits", BitConverter.ToInt32(BitConverter.GetBytes(getters[property]()), 0).ToString("x8", CultureInfo.InvariantCulture));
                    Put(key + ".messages", string.Join("|", capture.messages.OrderBy(x => x, StringComparer.Ordinal)));
                }
        }
        Object.DestroyImmediate(two); Object.DestroyImmediate(three);
    }
    void Record()
    {
        Ranges();
        foreach (var type in new[] { typeof(PhysicsMaterial), typeof(PhysicsMaterial2D), typeof(TextAsset) })
            Put("interfaces." + type.Name, string.Join(",", type.GetInterfaces().Select(t => t.FullName).OrderBy(s => s, StringComparer.Ordinal)));
        var three = Resources.Load<PhysicsMaterial>("Payload3D");
        Put("3d.loaded", three != null);
        Put("3d.dynamic", three.dynamicFriction); Put("3d.static", three.staticFriction); Put("3d.bounce", three.bounciness);
        Put("3d.friction-mode", (int)three.frictionCombine); Put("3d.bounce-mode", (int)three.bounceCombine);
        var threeCopy = Object.Instantiate(three);
        Put("3d.distinct", threeCopy != three); Put("3d.clone-name", threeCopy.name);
        Put("3d.clone-dynamic", threeCopy.dynamicFriction); Put("3d.clone-static", threeCopy.staticFriction); Put("3d.clone-bounce", threeCopy.bounciness);
        Put("3d.clone-friction-mode", (int)threeCopy.frictionCombine); Put("3d.clone-bounce-mode", (int)threeCopy.bounceCombine);
        threeCopy.dynamicFriction = .875f; threeCopy.staticFriction = .5f; threeCopy.bounciness = .25f;
        Put("3d.original-dynamic", three.dynamicFriction); Put("3d.original-static", three.staticFriction); Put("3d.original-bounce", three.bounciness);
        var two = Resources.Load<PhysicsMaterial2D>("Payload2D");
        Put("2d.loaded", two != null); Put("2d.friction", two.friction); Put("2d.bounce", two.bounciness);
        Put("2d.friction-mode", (int)two.frictionCombine); Put("2d.bounce-mode", (int)two.bounceCombine);
        var twoCopy = Object.Instantiate(two);
        Put("2d.distinct", twoCopy != two); Put("2d.clone-name", twoCopy.name);
        Put("2d.clone-friction", twoCopy.friction); Put("2d.clone-bounce", twoCopy.bounciness);
        Put("2d.clone-friction-mode", (int)twoCopy.frictionCombine); Put("2d.clone-bounce-mode", (int)twoCopy.bounceCombine);
        twoCopy.friction = .875f; twoCopy.bounciness = .125f;
        Put("2d.original-friction", two.friction); Put("2d.original-bounce", two.bounciness);
        var text = Resources.Load<TextAsset>("PayloadText");
        Put("text.loaded", text != null); Put("text.content", text.text); Put("text.bytes", BitConverter.ToString(text.bytes));
        var textCopy = Object.Instantiate(text);
        Put("text.distinct", textCopy != text); Put("text.clone-name", textCopy.name);
        Put("text.clone-content", textCopy.text); Put("text.clone-bytes", BitConverter.ToString(textCopy.bytes));
        var returnedBytes = textCopy.bytes; returnedBytes[0] = 0;
        Put("text.returned-array", textCopy.text); Put("text.original", text.text);
        var runtimeText = new TextAsset("runtime snapshot") { name = "runtime-text" };
        var runtimeCopy = Object.Instantiate(runtimeText); Object.DestroyImmediate(runtimeText);
        Put("text.destroyed-original", runtimeCopy.text);
        var control = new PhysicsMaterial("runtime-material") { dynamicFriction = float.NaN, staticFriction = -2, bounciness = .5f };
        var controlCopy = Object.Instantiate(control);
        Put("3d.nonfinite", float.IsNaN(controlCopy.dynamicFriction)); Put("3d.negative", controlCopy.staticFriction);
        Object.DestroyImmediate(controlCopy); Object.DestroyImmediate(control); Object.DestroyImmediate(runtimeCopy);
        Object.DestroyImmediate(textCopy); Object.DestroyImmediate(twoCopy); Object.DestroyImmediate(threeCopy);
    }
}
