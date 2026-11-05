using System;
using System.Collections.Generic;
using Unbound.Runtime;

namespace UnityEngine
{
    /// <summary>Thrown by <see cref="GUIUtility.ExitGUI"/> to leave the current OnGUI call.</summary>
    public sealed partial class ExitGUIException : Exception
    {
        public ExitGUIException() { }

        internal ExitGUIException(string message) : base(message) { }
    }

    /// <summary>
    /// UnityGUI's state between controls: control IDs (stable across the events of a frame and
    /// between frames while the calls stay the same), the control holding the mouse
    /// (<see cref="hotControl"/>) and the one with keyboard focus, and per-control state objects.
    /// </summary>
    public partial class GUIUtility
    {
        static int s_HotControl;
        static int s_KeyboardControl;
        static int s_Scope;
        static int s_Count;
        static string s_CopyBuffer = "";
        static readonly Dictionary<int, object> s_StateObjects = new Dictionary<int, object>();

        internal static bool guiIsExiting { get; set; }

        /// <summary>Set when the mouse went to a group's background (it blocks what is below).</summary>
        internal static bool mouseUsed { get; set; }

        internal static int guiDepth => GUIClip.Depth;

        public static int hotControl
        {
            get => s_HotControl;
            set => s_HotControl = value;
        }

        public static int keyboardControl
        {
            get => s_KeyboardControl;
            set => s_KeyboardControl = value;
        }

        /// <summary>
        /// The system-wide clipboard's text (6000.0 Scripting API: "Get access to the system-wide
        /// clipboard."), through the host's <c>clipboard-get</c>/<c>clipboard-set</c> calls.
        /// <c>EditorGUIUtility.systemCopyBuffer</c> is this same property. Text and line ends pass
        /// unchanged. Unbound's choices (unmeasured): a clipboard holding no text (empty, another
        /// format, or not UTF-8) reads as ""; when the clipboard cannot be read or written (no
        /// display, an older host, an error) the game's in-process copy (<see cref="s_CopyBuffer"/>,
        /// the last text set or read) answers instead, with one warning each time the clipboard
        /// stops answering; null is stored as "". Without a native host (unit tests) only the
        /// in-process copy is used.
        /// </summary>
        public static string systemCopyBuffer
        {
            get => ReadSystemCopyBuffer();
            set => WriteSystemCopyBuffer(value ?? "");
        }

        /// <summary>Stands in for the host's control call in tests: (method, request) → answer, or
        /// throws as the native call does.</summary>
        internal static Func<string, string, string> s_ClipboardCallForTests;
        static readonly object s_ClipboardLock = new object();
        /// <summary>The last clipboard call failed and was warned about.</summary>
        static bool s_ClipboardFailing;

        internal static void ResetClipboardForTests()
        {
            lock (s_ClipboardLock)
            {
                s_CopyBuffer = "";
                s_ClipboardFailing = false;
            }
        }

        static bool ClipboardConnected => s_ClipboardCallForTests != null || Native.IsReady;

        static string ClipboardCall(string method, string request) =>
            s_ClipboardCallForTests != null ? s_ClipboardCallForTests(method, request) : Native.Call(method, request);

        static string ReadSystemCopyBuffer()
        {
            lock (s_ClipboardLock)
            {
                if (!ClipboardConnected) return s_CopyBuffer;
                string text;
                try
                {
                    using var answer = System.Text.Json.JsonDocument.Parse(ClipboardCall("clipboard-get", "{}"));
                    var value = answer.RootElement.GetProperty("text");
                    text = value.ValueKind switch
                    {
                        System.Text.Json.JsonValueKind.String => value.GetString(),
                        // No text on the clipboard: an answer, not a failure.
                        System.Text.Json.JsonValueKind.Null => "",
                        _ => throw new FormatException("the clipboard answer's text is " + value.ValueKind),
                    };
                }
                catch (Exception e)
                {
                    ClipboardFailed("read", e);
                    return s_CopyBuffer;
                }
                s_ClipboardFailing = false;
                s_CopyBuffer = text;
                return text;
            }
        }

        static void WriteSystemCopyBuffer(string text)
        {
            lock (s_ClipboardLock)
            {
                s_CopyBuffer = text;
                if (!ClipboardConnected) return;
                try
                {
                    var request = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string> { ["text"] = text });
                    ClipboardCall("clipboard-set", request);
                }
                catch (Exception e)
                {
                    ClipboardFailed("write", e);
                    return;
                }
                s_ClipboardFailing = false;
            }
        }

        /// <summary>Warns once until the clipboard answers again. The message names the cause,
        /// never the text.</summary>
        static void ClipboardFailed(string action, Exception e)
        {
            if (s_ClipboardFailing) return;
            s_ClipboardFailing = true;
            Debug.LogWarning(Unbound.Runtime.Log.DiagnosticPrefix + "GUIUtility.systemCopyBuffer could not " + action +
                " the system clipboard (" + e.Message + "); the game's in-process copy is used until it answers again");
        }

        public static bool hasModalWindow => GUI.HasModalWindow;

        internal static float pixelsPerPoint { get; set; } = 1f;

        /// <summary>The next ID of the current scope: an OnGUI call, or a window inside it.</summary>
        static int NextId()
        {
            int id = unchecked((s_Scope * 397) ^ (++s_Count * 7919)) & 0x7FFFFFFF;
            return id == 0 ? 1 : id;
        }

        /// <summary>Runs <paramref name="body"/> with control IDs of their own (a window's
        /// controls keep their IDs whenever the window runs).</summary>
        internal static void WithScope(int scope, Action body)
        {
            int savedScope = s_Scope, savedCount = s_Count;
            s_Scope = scope;
            s_Count = 0;
            try
            {
                body();
            }
            finally
            {
                s_Scope = savedScope;
                s_Count = savedCount;
            }
        }

        public static int GetControlID(FocusType focus) => NextId();

        public static int GetControlID(GUIContent contents, FocusType focus) => NextId();

        public static int GetControlID(FocusType focus, Rect position) => NextId();

        public static int GetControlID(GUIContent contents, FocusType focus, Rect position) => NextId();

        public static int GetControlID(int hint, FocusType focus) => NextId();

        public static int GetControlID(int hint, FocusType focusType, Rect rect) => NextId();

        internal static int GetPermanentControlID() => NextId();

        public static object GetStateObject(Type t, int controlID)
        {
            if (s_StateObjects.TryGetValue(controlID, out var state) && state != null && state.GetType() == t) return state;
            state = Activator.CreateInstance(t);
            s_StateObjects[controlID] = state;
            return state;
        }

        public static object QueryStateObject(Type t, int controlID) =>
            s_StateObjects.TryGetValue(controlID, out var state) && state != null && state.GetType() == t ? state : null;

        internal static bool HasKeyFocus(int controlID) => controlID == s_KeyboardControl;

        public static void ExitGUI()
        {
            guiIsExiting = true;
            throw new ExitGUIException();
        }

        internal static bool IsExitGUIException(Exception exception)
        {
            while (exception is System.Reflection.TargetInvocationException && exception.InnerException != null)
                exception = exception.InnerException;
            return exception is ExitGUIException;
        }

        internal static bool ShouldRethrowException(Exception exception) => IsExitGUIException(exception);

        internal static void CheckOnGUI()
        {
            if (guiDepth <= 0)
                throw new ArgumentException("You can only call GUI functions from inside OnGUI.");
        }

        internal static GUISkin GetDefaultSkin() => BuiltinGuiSkin.Skin;

        internal static GUISkin GetDefaultSkin(int skinMode) => BuiltinGuiSkin.Skin;

        internal static GUISkin GetBuiltinSkin(int skin) => BuiltinGuiSkin.Skin;

        /// <summary>Starts an OnGUI call: default skin and colors, the screen as the only clip,
        /// the behaviour's control IDs and its layout.</summary>
        internal static void BeginGUI(int skinMode, int instanceID, int useGUILayout)
        {
            ResetGlobalState();
            guiIsExiting = false;
            s_Scope = instanceID;
            s_Count = 0;
            GUIClip.Begin(new Rect(0, 0, Screen.width, Screen.height));
            if (useGUILayout != 0) GUILayoutUtility.Begin(instanceID);
        }

        /// <summary>Ends an OnGUI call; after a Layout event the layout is computed.</summary>
        internal static void EndGUI(int layoutType)
        {
            try
            {
                if (layoutType != 0 && Event.current != null && Event.current.type == EventType.Layout)
                    GUILayoutUtility.Layout();
                if (GUIClip.Depth > 1)
                    Debug.LogError("GUI Error: You are pushing more GUIClips than you are popping. Make sure they are balanced.");
            }
            finally
            {
                GUIClip.Begin(new Rect(0, 0, Screen.width, Screen.height));
                GUILayoutUtility.End();
                GUIContent.ClearStaticCache();
            }
        }

        internal static void EndGUIAfterException()
        {
            GUIClip.Begin(new Rect(0, 0, Screen.width, Screen.height));
            GUILayoutUtility.End();
            GUI.ResetScrollViews();
            GuiRender.ClearClips();
        }

        internal static void ResetGlobalState()
        {
            GUI.skin = null;
            GUI.ResetState();
        }

        internal static float RoundToPixelGrid(float v) => Mathf.Floor(v * pixelsPerPoint + 0.48f) / pixelsPerPoint;

        internal static float RoundToPixelGrid(float v, float scale) => Mathf.Floor(v * scale + 0.48f) / scale;

        public static Vector2 GUIToScreenPoint(Vector2 guiPoint) => GUIClip.GUIToScreen(guiPoint);

        public static Rect GUIToScreenRect(Rect guiRect)
        {
            var a = GUIToScreenPoint(guiRect.min);
            var b = GUIToScreenPoint(guiRect.max);
            return Rect.MinMaxRect(Math.Min(a.x, b.x), Math.Min(a.y, b.y), Math.Max(a.x, b.x), Math.Max(a.y, b.y));
        }

        public static Vector2 ScreenToGUIPoint(Vector2 screenPoint) => GUIClip.ScreenToGUI(screenPoint);

        public static Rect ScreenToGUIRect(Rect screenRect)
        {
            var a = ScreenToGUIPoint(screenRect.min);
            var b = ScreenToGUIPoint(screenRect.max);
            return Rect.MinMaxRect(Math.Min(a.x, b.x), Math.Min(a.y, b.y), Math.Max(a.x, b.x), Math.Max(a.y, b.y));
        }

        public static void RotateAroundPivot(float angle, Vector2 pivotPoint)
        {
            var m = GUI.matrix;
            GUI.matrix = Matrix4x4.identity;
            var point = GUIClip.Unclip(pivotPoint);
            var rotation = Matrix4x4.TRS(point, Quaternion.Euler(0f, 0f, angle), Vector3.one) * Matrix4x4.TRS(-point, Quaternion.identity, Vector3.one);
            GUI.matrix = rotation * m;
        }

        public static void ScaleAroundPivot(Vector2 scale, Vector2 pivotPoint)
        {
            var m = GUI.matrix;
            GUI.matrix = Matrix4x4.identity;
            var point = GUIClip.Unclip(pivotPoint);
            var scaling = Matrix4x4.TRS(point, Quaternion.identity, new Vector3(scale.x, scale.y, 1f)) * Matrix4x4.TRS(-point, Quaternion.identity, Vector3.one);
            GUI.matrix = scaling * m;
        }

        public static Rect AlignRectToDevice(Rect rect) => AlignRectToDevice(rect, out _, out _);

        public static Rect AlignRectToDevice(Rect rect, out int widthInPixels, out int heightInPixels)
        {
            var screen = GUIToScreenRect(rect);
            var aligned = Rect.MinMaxRect(Mathf.Round(screen.xMin), Mathf.Round(screen.yMin), Mathf.Round(screen.xMax), Mathf.Round(screen.yMax));
            widthInPixels = (int)aligned.width;
            heightInPixels = (int)aligned.height;
            return ScreenToGUIRect(aligned);
        }

        internal static bool HitTest(Rect rect, Vector2 point, int offset) =>
            point.x >= rect.xMin - offset && point.x < rect.xMax + offset && point.y >= rect.yMin - offset && point.y < rect.yMax + offset;

        internal static bool HitTest(Rect rect, Vector2 point, bool isDirectManipulationDevice) => HitTest(rect, point, 0);

        internal static bool HitTest(Rect rect, Event evt) => HitTest(rect, evt.mousePosition, 0);

        internal static bool GetChanged() => GUI.changed;

        internal static void SetChanged(bool changed) => GUI.changed = changed;
    }
}
