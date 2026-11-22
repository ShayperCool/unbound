using System;
using System.Collections.Generic;
using System.Threading;
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
        static int s_NextControlId = 100;
        static long s_LastPermanentControlId;
        static string s_CopyBuffer = "";
        static readonly Dictionary<int, object> s_StateObjects = new Dictionary<int, object>();
        static readonly Dictionary<int, ControlIdList> s_IdLists = new Dictionary<int, ControlIdList>();
        static ControlIdList s_CurrentIdList = new ControlIdList();

        sealed class ControlIdList
        {
            struct Entry
            {
                public int Hint;
                public int Id;
                public bool Focusable;
                public Rect Rect;
            }

            readonly List<Entry> m_Entries = new List<Entry>();
            int m_SearchIndex;

            public int SearchIndex => m_SearchIndex;

            public void SetSearchIndex(int index)
            {
                if (index >= 0 && index <= m_Entries.Count)
                    m_SearchIndex = index;
            }

            public void BeginOnGUI() => m_SearchIndex = 0;

            public int GetNext(int hint, FocusType focus, Rect rect)
            {
                var currentEvent = Event.current;
                var eventType = currentEvent != null ? currentEvent.type : EventType.Ignore;
                if (eventType == EventType.Used) return -1;

                bool focusable = focus == FocusType.Keyboard;
                int id = FindOrAllocate(hint, focusable);
                if (eventType != EventType.Layout && focus != FocusType.Passive && m_SearchIndex > 0)
                {
                    var updated = m_Entries[m_SearchIndex - 1];
                    updated.Rect = rect;
                    m_Entries[m_SearchIndex - 1] = updated;
                }
                return id;
            }

            int FindOrAllocate(int hint, bool focusable)
            {
                for (int i = m_SearchIndex; i < m_Entries.Count; i++)
                {
                    if (m_Entries[i].Hint == hint)
                    {
                        m_SearchIndex = i + 1;
                        return m_Entries[i].Id;
                    }
                }
                int id = AllocateId();
                m_Entries.Add(new Entry { Hint = hint, Id = id, Focusable = focusable, Rect = Rect.zero });
                m_SearchIndex = m_Entries.Count;
                return id;
            }

            /// <summary>Whether a control of this list can take keyboard focus.</summary>
            public bool HasFocusable
            {
                get
                {
                    for (int i = 0; i < m_Entries.Count; i++)
                        if (m_Entries[i].Focusable) return true;
                    return false;
                }
            }

            public bool Owns(int id)
            {
                for (int i = 0; i < m_Entries.Count; i++)
                    if (m_Entries[i].Id == id) return true;
                return false;
            }

            /// <summary>The first (or last) control that can take keyboard focus, or 0.</summary>
            public int EdgeFocusable(bool last)
            {
                for (int n = 0; n < m_Entries.Count; n++)
                {
                    var entry = m_Entries[last ? m_Entries.Count - 1 - n : n];
                    if (entry.Focusable) return entry.Id;
                }
                return 0;
            }

            public bool CanHaveKeyboardFocus(int id)
            {
                for (int i = 0; i < m_Entries.Count; i++)
                    if (m_Entries[i].Id == id) return m_Entries[i].Focusable;
                return false;
            }
        }

        static int AllocateId()
        {
            int id = unchecked(++s_NextControlId);
            if (id <= 0) id = s_NextControlId = 1;
            return id;
        }

        static ControlIdList IdListFor(int scope)
        {
            if (!s_IdLists.TryGetValue(scope, out var list))
                s_IdLists[scope] = list = new ControlIdList();
            return list;
        }

        /// <summary>Whether the current OnGUI scope has a control that can take keyboard focus.</summary>
        internal static bool HasFocusableControls() => s_CurrentIdList.HasFocusable;

        /// <summary>Whether the current OnGUI scope made this control ID.</summary>
        internal static bool OwnsId(int id) => s_CurrentIdList.Owns(id);

        /// <summary>Gives keyboard focus to the current scope's first control that can take it.</summary>
        internal static void SetKeyboardControlToFirstControlId() => keyboardControl = s_CurrentIdList.EdgeFocusable(false);

        /// <summary>Gives keyboard focus to the current scope's last control that can take it.</summary>
        internal static void SetKeyboardControlToLastControlId() => keyboardControl = s_CurrentIdList.EdgeFocusable(true);

        /// <summary>Drops a scope's control IDs (an IMGUIContainer that left its panel).</summary>
        internal static void ForgetIdList(int scope) => s_IdLists.Remove(scope);

        /// <summary>The GUI pass a UI Toolkit IMGUIContainer's pass interrupts (a container
        /// measured or sent an event from inside an OnGUI).</summary>
        internal struct OuterGUI
        {
            internal bool Saved;
            ControlIdList m_IdList;
            GUIClip.State m_Clip;
            GUILayoutUtility.OuterLayout m_Layout;
            GUISkin m_Skin;
            bool m_Exiting;

            internal static OuterGUI Save() => new OuterGUI
            {
                Saved = true, m_IdList = s_CurrentIdList, m_Clip = GUIClip.Save(), m_Layout = GUILayoutUtility.SaveOuter(),
                m_Skin = GUI.UserSkin, m_Exiting = guiIsExiting,
            };

            internal void Restore()
            {
                if (!Saved) return;
                s_CurrentIdList = m_IdList;
                GUIClip.Restore(m_Clip);
                GUILayoutUtility.RestoreOuter(m_Layout);
                GUI.skin = m_Skin;
                guiIsExiting = m_Exiting;
            }
        }

        /// <summary>Starts an IMGUIContainer's handler call: default skin and colors, its own
        /// control IDs and layout cache, its coordinates from <paramref name="origin"/> and
        /// <paramref name="clip"/> as all it shows. Inside an OnGUI (<paramref name="nested"/>) that
        /// pass's state is kept for <see cref="EndContainerGUI"/>.</summary>
        internal static OuterGUI BeginContainerGUI(int scope, GUILayoutUtility.LayoutCache cache, Vector2 origin, Rect clip, bool nested)
        {
            var outer = nested ? OuterGUI.Save() : default;
            ResetGlobalState();
            guiIsExiting = false;
            s_CurrentIdList = IdListFor(scope);
            s_CurrentIdList.BeginOnGUI();
            GUIClip.BeginContainer(origin, clip);
            GUILayoutUtility.SetOwner(scope);
            GUILayoutUtility.BeginContainer(cache);
            return outer;
        }

        /// <summary>Ends an IMGUIContainer's handler call; after a Layout event its layout is
        /// computed over <paramref name="layoutSize"/>.</summary>
        internal static void EndContainerGUI(OuterGUI outer, Rect layoutSize, bool reportClips)
        {
            try
            {
                if (Event.current != null && Event.current.type == EventType.Layout)
                    GUILayoutUtility.LayoutFromContainer(layoutSize.width, layoutSize.height);
                if (reportClips) ReportUnpoppedClips();
            }
            finally
            {
                ResetAfterGUI();
                outer.Restore();
            }
        }

        internal static void ResetControlIdListsForTests()
        {
            s_IdLists.Clear();
            s_CurrentIdList = new ControlIdList();
        }

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

        /// <summary>The input method's composition, the IME mode and the IME window position: the
        /// same state as <see cref="Input.compositionString"/>, <see cref="Input.imeCompositionMode"/>
        /// and <see cref="Input.compositionCursorPos"/> (a focused UI Toolkit text field sets the mode
        /// and Input reads it back, tests/projects/textcommands), without Input's warning when the
        /// window reads no input method.</summary>
        internal static string compositionString => Input.CompositionString;

        internal static IMECompositionMode imeCompositionMode
        {
            get => Input.imeCompositionMode;
            set => Input.SetImeCompositionMode(value, warn: false);
        }

        internal static Vector2 compositionCursorPos
        {
            get => Input.compositionCursorPos;
            set => Input.SetCompositionCursorPos(value, warn: false);
        }

        /// <summary>
        /// The system-wide clipboard's text (6000.0 Scripting API: "Get access to the system-wide
        /// clipboard."), through the host's <c>clipboard-get</c>/<c>clipboard-set</c> calls.
        /// <c>EditorGUIUtility.systemCopyBuffer</c> is this same property. Text and line ends pass
        /// unchanged. Measured in tests/projects/inputplatform: null becomes empty text; calls
        /// require the main thread. A clipboard holding no text (empty, another
        /// format, or not UTF-8) reads as ""; when the clipboard cannot be read or written (no
        /// display, an older host, an error) the game's in-process copy (<see cref="s_CopyBuffer"/>,
        /// the last text set or read) answers instead, with one warning each time the clipboard
        /// stops answering; null is stored as "". Without a native host (unit tests) only the
        /// in-process copy is used.
        /// </summary>
        public static string systemCopyBuffer
        {
            get { MainThreadCheck.Check("get_systemCopyBuffer"); return ReadSystemCopyBuffer(); }
            set { MainThreadCheck.Check("set_systemCopyBuffer"); WriteSystemCopyBuffer(value ?? ""); }
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

        /// <summary>Runs <paramref name="body"/> with control IDs of their own (a window's
        /// controls keep their IDs whenever the window runs).</summary>
        internal static void WithScope(int scope, Action body)
        {
            var savedList = s_CurrentIdList;
            s_CurrentIdList = IdListFor(scope);
            s_CurrentIdList.BeginOnGUI();
            try
            {
                body();
            }
            finally
            {
                s_CurrentIdList = savedList;
            }
        }

        public static int GetControlID(FocusType focus) => GetControlID(0, focus, Rect.zero);

        public static int GetControlID(GUIContent contents, FocusType focus) =>
            GetControlID(contents != null ? contents.hash : 0, focus, Rect.zero);

        public static int GetControlID(FocusType focus, Rect position) => GetControlID(0, focus, position);

        public static int GetControlID(GUIContent contents, FocusType focus, Rect position) =>
            GetControlID(contents != null ? contents.hash : 0, focus, position);

        public static int GetControlID(int hint, FocusType focus) => GetControlID(hint, focus, Rect.zero);

        public static int GetControlID(int hint, FocusType focusType, Rect rect)
        {
            int id = s_CurrentIdList.GetNext(hint, focusType, rect);
            if (id != -1)
            {
                GUI.ApplyNextControlName(id);
                UnityEditor.EditorGUIControls.CompletePendingPrefixLabel(id);
            }
            return id;
        }

        internal static bool CanHaveKeyboardFocus(int id) => s_CurrentIdList.CanHaveKeyboardFocus(id);

        internal static int GetSearchIndexOfControlIDList() => s_CurrentIdList.SearchIndex;

        internal static void SetSearchIndexOfControlIDList(int index) => s_CurrentIdList.SetSearchIndex(index);

        // Scoped controls use positive IDs. Permanent controls keep a distinct ID across
        // GUI passes and do not advance the current pass's control sequence.
        internal static int GetPermanentControlID()
        {
            long id = Interlocked.Decrement(ref s_LastPermanentControlId);
            if (id < int.MinValue) throw new InvalidOperationException("Permanent IMGUI control IDs exhausted.");
            return (int)id;
        }

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

        internal static void CheckOnGUI() => CheckOnGUI(requireCurrentEvent: false);

        internal static void CheckOnGUI(bool requireCurrentEvent)
        {
            if (guiDepth <= 0 || (requireCurrentEvent && Event.current == null))
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
            s_CurrentIdList = IdListFor(instanceID);
            s_CurrentIdList.BeginOnGUI();
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
                ReportUnpoppedClips();
            }
            finally
            {
                ResetAfterGUI();
            }
        }

        static void ReportUnpoppedClips()
        {
            if (GUIClip.Depth > 1)
                Debug.LogError("GUI Error: You are pushing more GUIClips than you are popping. Make sure they are balanced.");
        }

        static void ResetAfterGUI()
        {
            GUIClip.Begin(new Rect(0, 0, Screen.width, Screen.height));
            GUILayoutUtility.End();
            GUIContent.ClearStaticCache();
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

        /// <summary>The pixels per point of the device a UI Toolkit panel draws to while it generates
        /// an element's content (0 outside): AlignRectToDevice aligns to that panel's pixels then.</summary>
        internal static float uaContentPixelsPerPoint;

        /// <summary>The rectangle on the device's pixel grid: its screen rectangle in pixels, each edge
        /// rounded. Inside a UI Toolkit panel's content generation the grid is the panel's (its
        /// scaled pixels per point; matches the Unity 6000.0.71f1 Editor, tests/projects/uitkvector).</summary>
        public static Rect AlignRectToDevice(Rect rect, out int widthInPixels, out int heightInPixels)
        {
            float ppp = uaContentPixelsPerPoint > 0f ? uaContentPixelsPerPoint : pixelsPerPoint;
            var screen = GUIToScreenRect(rect);
            var aligned = Rect.MinMaxRect(Mathf.Round(screen.xMin * ppp), Mathf.Round(screen.yMin * ppp), Mathf.Round(screen.xMax * ppp), Mathf.Round(screen.yMax * ppp));
            widthInPixels = (int)aligned.width;
            heightInPixels = (int)aligned.height;
            return ScreenToGUIRect(Rect.MinMaxRect(aligned.xMin / ppp, aligned.yMin / ppp, aligned.xMax / ppp, aligned.yMax / ppp));
        }

        internal static bool HitTest(Rect rect, Vector2 point, int offset) =>
            point.x >= rect.xMin - offset && point.x < rect.xMax + offset && point.y >= rect.yMin - offset && point.y < rect.yMax + offset;

        internal static bool HitTest(Rect rect, Vector2 point, bool isDirectManipulationDevice) => HitTest(rect, point, 0);

        internal static bool HitTest(Rect rect, Event evt) => HitTest(rect, evt.mousePosition, 0);

        internal static bool GetChanged() => GUI.changed;

        internal static void SetChanged(bool changed) => GUI.changed = changed;
    }
}
