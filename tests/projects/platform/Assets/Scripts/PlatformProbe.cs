using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Calls the platform APIs as a game on a device would (AndroidJNI and its Java wrappers, Android
/// permissions, iOS device queries, Handheld, the webcam, microphone, location, compass and gyro,
/// user authorization, screen and keyboard queries) and records what each returns or throws and
/// what it logs, as JSON lines in <c>PLATFORM_PROBE_OUT</c>; then quits. The Linux Editor's answers
/// are the reference.
/// </summary>
public class PlatformProbe : MonoBehaviour
{
    readonly StringBuilder m_Log = new StringBuilder();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("PlatformProbe").AddComponent<PlatformProbe>();

    class Runnable : AndroidJavaProxy
    {
        public Runnable() : base("java.lang.Runnable") { }
        public void run() { }
    }

    static string Text(object value)
    {
        switch (value)
        {
            case null: return "null";
            case string s: return "\"" + s + "\"";
            case float f: return f.ToString("R", CultureInfo.InvariantCulture);
            case double d: return d.ToString("R", CultureInfo.InvariantCulture);
            case IntPtr p: return "ptr " + (p == IntPtr.Zero ? "0" : "nonzero");
            case Array a: return a.GetType().GetElementType().Name + "[" + a.Length + "]";
            default: return Convert.ToString(value, CultureInfo.InvariantCulture);
        }
    }

    static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");

    void Record(string call, string result)
    {
        m_Log.Append("{\"call\":\"").Append(Escape(call)).Append("\",\"result\":\"").Append(Escape(result)).Append("\"}\n");
    }

    void Call(string name, Func<object> f)
    {
        string result;
        try { result = "returns " + Text(f()); }
        catch (Exception e) { result = "throws " + e.GetType().FullName + ": " + e.Message; }
        Record(name, result);
    }

    void Call(string name, Action f) => Call(name, () => { f(); return "void"; });

    void OnLog(string condition, string stackTrace, LogType type) =>
        Record("log", type + ": " + condition);

    void Start()
    {
        Application.logMessageReceived += OnLog;

        Call("AndroidJNI.GetVersion", () => AndroidJNI.GetVersion());
        Call("AndroidJNI.AttachCurrentThread", () => AndroidJNI.AttachCurrentThread());
        Call("AndroidJNI.FindClass", () => AndroidJNI.FindClass("java/lang/String"));
        Call("AndroidJNI.NewStringUTF", () => AndroidJNI.NewStringUTF("x"));
        Call("AndroidJNI.GetStringUTFChars", () => AndroidJNI.GetStringUTFChars(IntPtr.Zero));
        Call("AndroidJNI.GetStaticMethodID", () => AndroidJNI.GetStaticMethodID(IntPtr.Zero, "valueOf", "(I)Ljava/lang/String;"));
        Call("AndroidJNI.CallStaticIntMethod", () => AndroidJNI.CallStaticIntMethod(IntPtr.Zero, IntPtr.Zero, new jvalue[0]));
        Call("AndroidJNI.ExceptionOccurred", () => AndroidJNI.ExceptionOccurred());
        Call("AndroidJNI.PushLocalFrame", () => AndroidJNI.PushLocalFrame(4));
        Call("AndroidJNI.GetArrayLength", () => AndroidJNI.GetArrayLength(IntPtr.Zero));
        Call("AndroidJNIHelper.debug", () => AndroidJNIHelper.debug);
        Call("AndroidJNIHelper.GetConstructorID", () => AndroidJNIHelper.GetConstructorID(IntPtr.Zero));
        Call("AndroidJavaClass.ctor", () => new AndroidJavaClass("com.unity3d.player.UnityPlayer"));
        Call("AndroidJavaClass.GetStatic", () =>
        {
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                return player.GetStatic<AndroidJavaObject>("currentActivity");
        });
        Call("AndroidJavaObject.ctor", () => new AndroidJavaObject("java.lang.String", "x"));
        Call("AndroidJavaObject.Call", () =>
        {
            using (var s = new AndroidJavaObject("java.lang.String", "x"))
                return s.Call<int>("length");
        });
        Call("AndroidJavaProxy.ctor", () => new Runnable().javaInterface != null);
        Call("AndroidJavaObject.Call<string>", () => { using (var s = new AndroidJavaObject("java.lang.String", "x")) return s.Call<string>("toString"); });
        Call("AndroidJavaObject.Call<AndroidJavaObject>", () => { using (var s = new AndroidJavaObject("java.lang.String", "x")) return s.Call<AndroidJavaObject>("getClass"); });
        Call("AndroidJavaObject.Call void", () => { using (var s = new AndroidJavaObject("java.lang.String", "x")) s.Call("notify"); });
        Call("AndroidJavaObject.Get<int>", () => { using (var s = new AndroidJavaObject("java.lang.String", "x")) return s.Get<int>("count"); });
        Call("AndroidJavaObject.Get<string>", () => { using (var s = new AndroidJavaObject("java.lang.String", "x")) return s.Get<string>("name"); });
        Call("AndroidJavaObject.Set<int>", () => { using (var s = new AndroidJavaObject("java.lang.String", "x")) s.Set("count", 3); });
        Call("AndroidJavaObject.GetRawObject", () => { using (var s = new AndroidJavaObject("java.lang.String", "x")) return s.GetRawObject(); });
        Call("AndroidJavaObject.GetRawClass", () => { using (var s = new AndroidJavaObject("java.lang.String", "x")) return s.GetRawClass(); });
        Call("AndroidJavaClass.CallStatic<string>", () => { using (var c = new AndroidJavaClass("java.lang.String")) return c.CallStatic<string>("valueOf", 3); });
        Call("AndroidJavaClass.CallStatic<AndroidJavaObject>", () => { using (var c = new AndroidJavaClass("java.lang.String")) return c.CallStatic<AndroidJavaObject>("valueOf", 3); });
        Call("AndroidJavaClass.CallStatic<int>", () => { using (var c = new AndroidJavaClass("java.lang.Integer")) return c.CallStatic<int>("parseInt", "3"); });
        Call("AndroidJavaClass.GetStatic<int>", () => { using (var c = new AndroidJavaClass("java.lang.Integer")) return c.GetStatic<int>("MAX_VALUE"); });
        Call("AndroidJavaClass.SetStatic<string>", () => { using (var c = new AndroidJavaClass("java.lang.Integer")) c.SetStatic("name", "x"); });
        Call("AndroidJNI.CallStringMethod", () => AndroidJNI.CallStringMethod(IntPtr.Zero, IntPtr.Zero, new jvalue[0]));
        Call("AndroidJNI.GetStringField", () => AndroidJNI.GetStringField(IntPtr.Zero, IntPtr.Zero));
        Call("AndroidJNI.FromIntArray", () => AndroidJNI.FromIntArray(IntPtr.Zero));
        Call("AndroidJNI.ToIntArray", () => AndroidJNI.ToIntArray(new[] { 1, 2 }));
        Call("AndroidJNIHelper.GetSignature(int)", () => AndroidJNIHelper.GetSignature(3));
        Call("AndroidJNIHelper.GetSignature(args)", () => AndroidJNIHelper.GetSignature(new object[] { 3, "x", true }));
        Call("Permission.HasUserAuthorizedPermission", () => UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera));
        Call("Permission.RequestUserPermission", () => UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Camera));
        Call("Permission.RequestUserPermission callbacks", () =>
        {
            var callbacks = new UnityEngine.Android.PermissionCallbacks();
            callbacks.PermissionGranted += p => Record("permission granted", p);
            callbacks.PermissionDenied += p => Record("permission denied", p);
            callbacks.PermissionDeniedAndDontAskAgain += p => Record("permission denied, don't ask", p);
            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone, callbacks);
        });
        Call("Permission.RequestUserPermissions", () => UnityEngine.Android.Permission.RequestUserPermissions(new[] { UnityEngine.Android.Permission.FineLocation, UnityEngine.Android.Permission.CoarseLocation }));
        Call("Permission.ShouldShowRequestPermissionRationale", () => UnityEngine.Android.Permission.ShouldShowRequestPermissionRationale(UnityEngine.Android.Permission.Camera));
        Call("AndroidApplication.currentActivity", () => UnityEngine.Android.AndroidApplication.currentActivity);
        Call("AndroidApplication.currentContext", () => UnityEngine.Android.AndroidApplication.currentContext);
        Call("AndroidApplication.InvokeOnUIThread", () => UnityEngine.Android.AndroidApplication.InvokeOnUIThread(() => Record("ui thread", "ran")));

        Call("AndroidJNI.InvokeAttached", () => AndroidJNI.InvokeAttached(() => Record("attached", "ran")));
        Call("AndroidJNIHelper.GetSignature(float)", () => AndroidJNIHelper.GetSignature(1.5f));
        Call("AndroidJNIHelper.GetSignature(byte)", () => AndroidJNIHelper.GetSignature((byte)1));
        Call("AndroidJNIHelper.GetSignature(sbyte,short,long,double,char)", () => AndroidJNIHelper.GetSignature(new object[] { (sbyte)1, (short)1, 1L, 1.0, 'c' }));
        Call("AndroidJNIHelper.GetSignature(null)", () => AndroidJNIHelper.GetSignature((object)null));
        Call("AndroidJNIHelper.GetSignature(int[])", () => AndroidJNIHelper.GetSignature(new int[0]));
        Call("AndroidJNIHelper.GetSignature(string[][])", () => AndroidJNIHelper.GetSignature(new string[0][]));
        Call("AndroidJNIHelper.GetSignature(typeof(string))", () => AndroidJNIHelper.GetSignature(typeof(string)));
        Call("AndroidJNIHelper.GetSignature(typeof(AndroidJavaObject))", () => AndroidJNIHelper.GetSignature(typeof(AndroidJavaObject)));
        Call("AndroidJNIHelper.GetSignature(AndroidJavaObject)", () => { using (var s = new AndroidJavaObject("java.lang.String", "x")) return AndroidJNIHelper.GetSignature(s); });
        Call("AndroidJNIHelper.GetSignature(AndroidJavaClass)", () => { using (var c = new AndroidJavaClass("java.lang.String")) return AndroidJNIHelper.GetSignature(c); });
        Call("AndroidJNIHelper.GetSignature(proxy)", () => AndroidJNIHelper.GetSignature(new Runnable()));
        Call("AndroidJNIHelper.GetSignature(runnable)", () => AndroidJNIHelper.GetSignature(new AndroidJavaRunnable(() => { })));
        Call("AndroidJNIHelper.GetSignature(Vector3)", () => AndroidJNIHelper.GetSignature(Vector3.zero));
        Call("AndroidJNIHelper.GetSignature<string>(args)", () => AndroidJNIHelper.GetSignature<string>(new object[] { 3 }));
        Call("AndroidJNIHelper.GetSignature<AndroidJavaObject>(args)", () => AndroidJNIHelper.GetSignature<AndroidJavaObject>(new object[0]));
        Call("AndroidJNIHelper.GetMethodID", () => AndroidJNIHelper.GetMethodID(IntPtr.Zero, "length", "()I"));
        Call("AndroidJNIHelper.GetFieldID", () => AndroidJNIHelper.GetFieldID(IntPtr.Zero, "count", "I"));
        Call("AndroidJNIHelper.GetFieldID<int>", () => AndroidJNIHelper.GetFieldID<int>(IntPtr.Zero, "count", false));
        Call("AndroidJNIHelper.CreateJNIArgArray", () => AndroidJNIHelper.CreateJNIArgArray(new object[] { 3, "x", 2.5f }));
        Call("AndroidJNIHelper.CreateJNIArgArray(i)", () => AndroidJNIHelper.CreateJNIArgArray(new object[] { 3, true })[0].i);
        Call("AndroidJNIHelper.CreateJNIArgArray(Vector3)", () => AndroidJNIHelper.CreateJNIArgArray(new object[] { Vector3.zero }));
        Call("AndroidJNIHelper.ConvertFromJNIArray<int[]>", () => AndroidJNIHelper.ConvertFromJNIArray<int[]>(IntPtr.Zero));
        Call("AndroidJNIHelper.ConvertToJNIArray", () => AndroidJNIHelper.ConvertToJNIArray(new[] { 1 }));
        Call("AndroidJNIHelper.Box", () => AndroidJNIHelper.Box(3));
        Call("AndroidJNIHelper.Unbox", () => { AndroidJNIHelper.Unbox(IntPtr.Zero, out int v); return v; });
        Call("AndroidJNIHelper.CreateJavaRunnable", () => AndroidJNIHelper.CreateJavaRunnable(() => { }));
        Call("AndroidJNIHelper.CreateJavaProxy", () => AndroidJNIHelper.CreateJavaProxy(new Runnable()));
        Call("AndroidJavaObject.ctor(IntPtr.Zero)", () => new AndroidJavaObject(IntPtr.Zero));
        Call("AndroidJavaObject.Call<int[]>", () => { using (var s = new AndroidJavaObject("java.lang.String", "x")) return s.Call<int[]>("chars"); });
        Call("AndroidJavaObject.Call<bool>", () => { using (var s = new AndroidJavaObject("java.lang.String", "x")) return s.Call<bool>("isEmpty"); });
        Call("AndroidJavaObject.Call<Vector3>", () => { using (var s = new AndroidJavaObject("java.lang.String", "x")) return s.Call<Vector3>("x"); });
        Call("AndroidJavaObject.Call<AndroidJavaClass>", () => { using (var s = new AndroidJavaObject("java.lang.String", "x")) return s.Call<AndroidJavaClass>("getClass"); });
        Call("AndroidJavaObject.Get<int>(IntPtr)", () => { using (var s = new AndroidJavaObject("java.lang.String", "x")) return s.Get<int>(IntPtr.Zero); });
        Call("AndroidJavaObject.CloneReference", () => { using (var s = new AndroidJavaObject("java.lang.String", "x")) return s.CloneReference(); });
        Call("AndroidJavaObject.Dispose twice", () => { var s = new AndroidJavaObject("java.lang.String", "x"); s.Dispose(); s.Dispose(); });
        Call("AndroidJavaObject.Call after Dispose", () => { var s = new AndroidJavaObject("java.lang.String", "x"); s.Dispose(); return s.Call<int>("length"); });
        Call("AndroidJavaClass.GetRawObject", () => { using (var c = new AndroidJavaClass("java.lang.String")) return c.GetRawObject(); });
        Call("AndroidJavaProxy.Invoke", () => new Runnable().Invoke("run", new object[0]));
        Call("AndroidJavaProxy.Invoke missing", () => new Runnable().Invoke("nothing", new object[0]));
        Call("AndroidJavaProxy.Invoke(IntPtr)", () => new Runnable().Invoke("run", IntPtr.Zero));
        Call("AndroidJavaProxy.toString", () => new Runnable().toString());
        Call("AndroidJavaProxy.hashCode", () => new Runnable().hashCode());
        Call("AndroidJavaProxy.equals", () => new Runnable().equals(null));
        Call("AndroidJavaProxy(AndroidJavaClass)", () => new AndroidJavaProxy(new AndroidJavaClass("java.lang.Runnable")).javaInterface != null);
        Call("PermissionCallbacks.javaInterface", () => new UnityEngine.Android.PermissionCallbacks().javaInterface != null);
        Call("AndroidApplication.InvokeOnUnityMainThread", () => UnityEngine.Android.AndroidApplication.InvokeOnUnityMainThread(() => Record("unity main thread", "ran")));
        Call("AndroidApplication.unityPlayer", () => UnityEngine.Android.AndroidApplication.unityPlayer);
        Call("AndroidApplication.currentConfiguration", () => UnityEngine.Android.AndroidApplication.currentConfiguration);
        Call("Handheld.SetActivityIndicatorStyle", () => { Handheld.SetActivityIndicatorStyle(AndroidActivityIndicatorStyle.Large); return Handheld.GetActivityIndicatorStyle(); });
        Call("Handheld.use32BitDisplayBuffer = false", () => { Handheld.use32BitDisplayBuffer = false; return Handheld.use32BitDisplayBuffer; });
        Call("Handheld.PlayFullScreenMovie", () => Handheld.PlayFullScreenMovie("movie.mp4"));
        Call("Handheld.ClearShaderCache", () => Handheld.ClearShaderCache());
        Call("iOS.Device.hideHomeButton = true", () => { UnityEngine.iOS.Device.hideHomeButton = true; return UnityEngine.iOS.Device.hideHomeButton; });
        Call("iOS.Device.wantsSoftwareDimming", () => UnityEngine.iOS.Device.wantsSoftwareDimming);
        Call("iOS.Device.deferSystemGesturesMode", () => UnityEngine.iOS.Device.deferSystemGesturesMode);
        Call("iOS.Device.iosAppOnMac", () => UnityEngine.iOS.Device.iosAppOnMac);
        Call("iOS.Device.runsOnSimulator", () => UnityEngine.iOS.Device.runsOnSimulator);
        Call("iOS.Device.ResetNoBackupFlag", () => UnityEngine.iOS.Device.ResetNoBackupFlag("/tmp/none"));
        Call("WebCamTexture.Play", () => { var w = new WebCamTexture(); w.Play(); return w.isPlaying; });
        Call("WebCamTexture.ctor values", () => { var w = new WebCamTexture("cam", 320, 240, 15); return w.deviceName + " " + w.requestedWidth + "x" + w.requestedHeight + "@" + w.requestedFPS + " " + w.width + "x" + w.height; });
        Call("WebCamTexture.didUpdateThisFrame", () => new WebCamTexture().didUpdateThisFrame);
        Call("Microphone.Start", () => Microphone.Start("no such device", false, 1, 44100));
        Call("Microphone.GetPosition", () => Microphone.GetPosition(null));
        Call("Microphone.GetDeviceCaps", () => { Microphone.GetDeviceCaps("no such device", out int lo, out int hi); return lo + " " + hi; });

        Call("AndroidJNIHelper.debug calls", () =>
        {
            AndroidJNIHelper.debug = true;
            try
            {
                using (var s = new AndroidJavaObject("java.lang.String", "x"))
                {
                    s.Call("foo", 1, "a");
                    s.Call<int>("bar");
                    s.Call("baz", new string[] { "a", "b" });
                    s.Call<int, int>("qux", new int[] { 1, 2 });
                    s.CallStatic<string>("valueOf", 2);
                }
                using (var c = new AndroidJavaClass("java.lang.Integer")) c.CallStatic("gc");
                return AndroidJNIHelper.debug;
            }
            finally { AndroidJNIHelper.debug = false; }
        });

        Call("Handheld.Vibrate", () => Handheld.Vibrate());
        Call("Handheld.GetActivityIndicatorStyle", () => Handheld.GetActivityIndicatorStyle());
        Call("Handheld.StartActivityIndicator", () => Handheld.StartActivityIndicator());
        Call("Handheld.StopActivityIndicator", () => Handheld.StopActivityIndicator());
        Call("Handheld.use32BitDisplayBuffer", () => Handheld.use32BitDisplayBuffer);

        Call("iOS.Device.generation", () => UnityEngine.iOS.Device.generation);
        Call("iOS.Device.systemVersion", () => UnityEngine.iOS.Device.systemVersion);
        Call("iOS.Device.vendorIdentifier", () => UnityEngine.iOS.Device.vendorIdentifier);
        Call("iOS.Device.advertisingIdentifier", () => UnityEngine.iOS.Device.advertisingIdentifier);
        Call("iOS.Device.advertisingTrackingEnabled", () => UnityEngine.iOS.Device.advertisingTrackingEnabled);
        Call("iOS.Device.lowPowerModeEnabled", () => UnityEngine.iOS.Device.lowPowerModeEnabled);
        Call("iOS.Device.hideHomeButton", () => UnityEngine.iOS.Device.hideHomeButton);
        Call("iOS.Device.SetNoBackupFlag", () => UnityEngine.iOS.Device.SetNoBackupFlag("/tmp/none"));
        Call("iOS.Device.RequestStoreReview", () => UnityEngine.iOS.Device.RequestStoreReview());

        Call("WebCamTexture.devices", () => WebCamTexture.devices);
        Call("WebCamTexture.ctor.isPlaying", () => new WebCamTexture().isPlaying);
        Call("Microphone.IsRecording", () => Microphone.IsRecording(null));
        Call("Input.location.isEnabledByUser", () => Input.location.isEnabledByUser);
        Call("Input.location.status", () => Input.location.status);
        Call("Input.location.Start", () => Input.location.Start());
        Call("Input.location.status after Start", () => Input.location.status);
        Call("Input.location.Stop", () => Input.location.Stop());
        Call("Input.compass.enabled", () => Input.compass.enabled);
        Call("Input.compass.trueHeading", () => Input.compass.trueHeading);
        Call("Input.gyro.enabled", () => Input.gyro.enabled);
        Call("Input.gyro.attitude", () => Input.gyro.attitude);
        Call("Input.acceleration", () => Input.acceleration);
        Call("Input.deviceOrientation", () => Input.deviceOrientation);
        Call("SystemInfo.supportsGyroscope", () => SystemInfo.supportsGyroscope);
        Call("SystemInfo.supportsLocationService", () => SystemInfo.supportsLocationService);
        Call("SystemInfo.supportsVibration", () => SystemInfo.supportsVibration);
        Call("SystemInfo.supportsAccelerometer", () => SystemInfo.supportsAccelerometer);
        Call("SystemInfo.deviceType", () => SystemInfo.deviceType);
        Call("Application.HasUserAuthorization", () => Application.HasUserAuthorization(UserAuthorization.WebCam));
        Call("Application.RequestUserAuthorization", () => Application.RequestUserAuthorization(UserAuthorization.Microphone) != null);
        Call("Application.RequestAdvertisingIdentifierAsync", () => Application.RequestAdvertisingIdentifierAsync((id, tracking, error) => Record("advertising callback", Text(id) + " " + tracking + " " + Text(error))));
        Call("Application.isMobilePlatform", () => Application.isMobilePlatform);
        Call("Application.platform", () => Application.platform);
        Call("Screen.cutouts", () => Screen.cutouts);
        Call("Screen.orientation", () => Screen.orientation);
        Call("Screen.orientation = LandscapeLeft", () => { Screen.orientation = ScreenOrientation.LandscapeLeft; return Screen.orientation; });
        Call("Screen.orientation = AutoRotation", () => { Screen.orientation = ScreenOrientation.AutoRotation; return Screen.orientation; });
        Call("TouchScreenKeyboard.isSupported", () => TouchScreenKeyboard.isSupported);
        Call("TouchScreenKeyboard.Open", () => TouchScreenKeyboard.Open("text") == null);
        Call("TouchScreenKeyboard.visible", () => TouchScreenKeyboard.visible);
        Call("TouchScreenKeyboard.area", () => TouchScreenKeyboard.area);

        m_Done = true;
    }

    bool m_Done;
    int m_Frames;

    // Callbacks (permissions, advertising, the UI thread) may come in the next frames.
    void Update()
    {
        if (!m_Done || ++m_Frames < 3) return;
        Application.logMessageReceived -= OnLog;
        var output = Environment.GetEnvironmentVariable("PLATFORM_PROBE_OUT");
        if (!string.IsNullOrEmpty(output)) File.WriteAllText(output, m_Log.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
