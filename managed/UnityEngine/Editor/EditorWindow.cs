using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine;

namespace UnityEditor
{
    /// <summary>
    /// Editor windows without an Editor: windows opened by editor code (setup wizards, EULA prompts)
    /// exist as objects with their lifecycle (OnEnable, OnFocus, OnDestroy on Close) and state, but
    /// Unbound has no editor UI, so they are never drawn. Opening one is reported once per type.
    /// </summary>
    public partial class EditorWindow : ScriptableObject
    {
        static readonly List<EditorWindow> s_Open = new List<EditorWindow>();
        static readonly HashSet<Type> s_Reported = new HashSet<Type>();
        static EditorWindow s_Focused;

        Rect m_Position = new Rect(100, 100, 400, 300);
        Vector2 m_MinSize = new Vector2(100, 100);
        Vector2 m_MaxSize = new Vector2(4000, 4000);
        internal GUIContent m_TitleContent = new GUIContent();
        bool m_Shown;

        public EditorWindow() { }

        public static event Action windowFocusChanged;

        public static EditorWindow focusedWindow => s_Focused;

        public static EditorWindow mouseOverWindow => null;

        public Rect position
        {
            get => m_Position;
            set => m_Position = value;
        }

        public Vector2 minSize
        {
            get => m_MinSize;
            set => m_MinSize = value;
        }

        public Vector2 maxSize
        {
            get => m_MaxSize;
            set => m_MaxSize = value;
        }

        public GUIContent titleContent
        {
            get => m_TitleContent;
            set => m_TitleContent = value ?? new GUIContent();
        }

        [Obsolete("Use titleContent instead (it supports setting a title icon as well).")]
        public string title
        {
            get => m_TitleContent.text;
            set => m_TitleContent = new GUIContent(value);
        }

        public bool wantsMouseMove { get; set; }
        public bool wantsMouseEnterLeaveWindow { get; set; }
        public bool wantsLessLayoutEvents { get; set; }
        public bool autoRepaintOnSceneChange { get; set; }
        public bool maximized { get; set; }
        public bool hasFocus => s_Focused == this;
        public bool docked => false;
        public int depthBufferBits { get; set; }
        public int antiAlias { get; set; }
        public bool hasUnsavedChanges { get; protected set; }
        public string saveChangesMessage { get; protected set; }

        /// <summary>The open window of a type, or a new one.</summary>
        public static EditorWindow GetWindow(Type windowType, bool utility, string title, bool focus) =>
            GetOrCreate(windowType, title, focus, null);

        public static EditorWindow GetWindow(Type windowType, bool utility, string title) => GetWindow(windowType, utility, title, true);
        public static EditorWindow GetWindow(Type windowType, bool utility) => GetWindow(windowType, utility, null, true);
        public static EditorWindow GetWindow(Type windowType) => GetWindow(windowType, false, null, true);

        public static EditorWindow GetWindowWithRect(Type windowType, Rect rect, bool utility, string title) =>
            GetOrCreate(windowType, title, true, rect);

        public static EditorWindow GetWindowWithRect(Type windowType, Rect rect, bool utility) => GetWindowWithRect(windowType, rect, utility, null);
        public static EditorWindow GetWindowWithRect(Type windowType, Rect rect) => GetWindowWithRect(windowType, rect, false, null);

        public static T GetWindow<T>() where T : EditorWindow => GetWindow<T>(false, null, true);
        public static T GetWindow<T>(bool utility) where T : EditorWindow => GetWindow<T>(utility, null, true);
        public static T GetWindow<T>(bool utility, string title) where T : EditorWindow => GetWindow<T>(utility, title, true);
        public static T GetWindow<T>(string title) where T : EditorWindow => GetWindow<T>(false, title, true);
        public static T GetWindow<T>(string title, bool focus) where T : EditorWindow => GetWindow<T>(false, title, focus);
        public static T GetWindow<T>(bool utility, string title, bool focus) where T : EditorWindow => (T)GetOrCreate(typeof(T), title, focus, null);
        public static T GetWindow<T>(params Type[] desiredDockNextTo) where T : EditorWindow => GetWindow<T>(null, true, desiredDockNextTo);
        public static T GetWindow<T>(string title, params Type[] desiredDockNextTo) where T : EditorWindow => GetWindow<T>(title, true, desiredDockNextTo);
        public static T GetWindow<T>(string title, bool focus, params Type[] desiredDockNextTo) where T : EditorWindow => (T)GetOrCreate(typeof(T), title, focus, null);

        public static T CreateWindow<T>(params Type[] desiredDockNextTo) where T : EditorWindow => CreateWindow<T>(null, desiredDockNextTo);

        public static T CreateWindow<T>(string title, params Type[] desiredDockNextTo) where T : EditorWindow
        {
            var window = (T)Create(typeof(T), title);
            window.Show();
            window.Focus();
            return window;
        }

        public static T GetWindowWithRect<T>(Rect rect) where T : EditorWindow => GetWindowWithRect<T>(rect, false, null, true);
        public static T GetWindowWithRect<T>(Rect rect, bool utility) where T : EditorWindow => GetWindowWithRect<T>(rect, utility, null, true);
        public static T GetWindowWithRect<T>(Rect rect, bool utility, string title) where T : EditorWindow => GetWindowWithRect<T>(rect, utility, title, true);
        public static T GetWindowWithRect<T>(Rect rect, bool utility, string title, bool focus) where T : EditorWindow => (T)GetOrCreate(typeof(T), title, focus, rect);

        public static bool HasOpenInstances<T>() where T : EditorWindow
        {
            s_Open.RemoveAll(w => w == null);
            foreach (var w in s_Open)
                if (w is T) return true;
            return false;
        }

        public static void FocusWindowIfItsOpen(Type t)
        {
            s_Open.RemoveAll(w => w == null);
            foreach (var w in s_Open)
                if (t.IsInstanceOfType(w))
                {
                    w.Focus();
                    return;
                }
        }

        public static void FocusWindowIfItsOpen<T>() where T : EditorWindow => FocusWindowIfItsOpen(typeof(T));

        static EditorWindow GetOrCreate(Type type, string title, bool focus, Rect? rect)
        {
            if (type == null || !typeof(EditorWindow).IsAssignableFrom(type))
                throw new ArgumentException($"{type} is not an EditorWindow type");
            s_Open.RemoveAll(w => w == null);
            EditorWindow window = null;
            foreach (var w in s_Open)
                if (w.GetType() == type)
                {
                    window = w;
                    break;
                }
            if (window == null) window = Create(type, title);
            else if (title != null) window.titleContent = new GUIContent(title);
            if (rect.HasValue)
            {
                window.position = rect.Value;
                window.minSize = window.maxSize = rect.Value.size;
            }
            window.Show();
            if (focus) window.Focus();
            return window;
        }

        static EditorWindow Create(Type type, string title)
        {
            var window = (EditorWindow)CreateInstance(type);
            window.titleContent = new GUIContent(title ?? type.Name);
            return window;
        }

        void Open()
        {
            if (!s_Open.Contains(this)) s_Open.Add(this);
            if (m_Shown) return;
            m_Shown = true;
            var type = GetType();
            if (s_Reported.Add(type))
                Native.Log((int)LogType.Log, $"[Unbound] editor window {type.FullName} (\"{m_TitleContent.text}\") opened; Unbound has no editor UI, so it is not shown");
        }

        public void Show() => Open();
        public void Show(bool immediateDisplay) => Open();
        public void ShowUtility() => Open();
        public void ShowPopup() => Open();
        public void ShowTab() => Open();
        public void ShowAuxWindow() => Open();
        public void ShowModalUtility() => Open();

        /// <summary>Unity blocks until a modal window closes; with nothing shown it returns at once.</summary>
        public void ShowModal() => Open();

        public void ShowAsDropDown(Rect buttonRect, Vector2 windowSize)
        {
            m_Position = new Rect(buttonRect.x, buttonRect.yMax, windowSize.x, windowSize.y);
            Open();
        }

        public void Focus()
        {
            if (s_Focused == this) return;
            var previous = s_Focused;
            s_Focused = this;
            if (previous != null) Message(previous, "OnLostFocus");
            Message(this, "OnFocus");
            windowFocusChanged?.Invoke();
        }

        static void Message(EditorWindow window, string name)
        {
            var method = ScriptMessages.Find(window.GetType(), name, Type.EmptyTypes);
            if (method == null) return;
            try { method.Invoke(window, null); }
            catch (System.Reflection.TargetInvocationException e) { Debug.LogException(e.InnerException ?? e, window); }
        }

        public void Close()
        {
            s_Open.Remove(this);
            if (s_Focused == this)
            {
                s_Focused = null;
                windowFocusChanged?.Invoke();
            }
            DestroyImmediate(this, true);
        }

        public void Repaint() { }

        public bool SendEvent(Event e) => false;

        public void ShowNotification(GUIContent notification) { }

        public void ShowNotification(GUIContent notification, double fadeoutWait) { }

        public void RemoveNotification() { }

        public void BeginWindows() { }

        public void EndWindows() { }

        public virtual void SaveChanges() => hasUnsavedChanges = false;

        public virtual void DiscardChanges() => hasUnsavedChanges = false;

        public virtual IEnumerable<Type> GetExtraPaneTypes() => Array.Empty<Type>();
    }
}
