using System;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityEditor
{
    public abstract partial class PopupWindowContent
    {
        const float DefaultWindowExtent = 200f;

        protected PopupWindowContent() { }

        public EditorWindow editorWindow { get; internal set; }

        public virtual void OnGUI(Rect rect) { }

        public virtual VisualElement CreateGUI() => null;

        public virtual Vector2 GetWindowSize() => Compat.EditorPopupWindowMeasured
            ? new Vector2(DefaultWindowExtent, DefaultWindowExtent)
            : Vector2.zero;

        public virtual void OnOpen() { }

        public virtual void OnClose() { }
    }

    public partial class PopupWindow : EditorWindow
    {
        const double SameActivatorReopenDelay = 0.2;
        static double s_LastClosedTime = double.NegativeInfinity;
        static Rect s_LastActivatorRect;
        PopupWindowContent m_Content;

        internal PopupWindow() { }

        public static void Show(Rect activatorRect, PopupWindowContent windowContent)
        {
            if (!Compat.EditorPopupWindowMeasured)
            {
                Stubs.NotImplemented("UnityEditor.PopupWindow.Show");
                return;
            }

            if (windowContent != null)
            {
                var contentType = windowContent.GetType();
                foreach (var popup in Resources.FindObjectsOfTypeAll<PopupWindow>())
                {
                    if (popup == null || popup.m_Content?.GetType() != contentType) continue;
                    popup.Close();
                    return;
                }
            }

            if (!ShouldShowWindow(activatorRect)) return;
            if (windowContent == null) throw new NullReferenceException("Object reference not set to an instance of an object");

            var window = CreateInstance<PopupWindow>();
            try { window.OpenContent(activatorRect, windowContent); }
            catch
            {
                if (window != null) window.Close();
                throw;
            }

            if (Event.current != null) GUIUtility.ExitGUI();
        }

        internal static bool ShouldShowWindow(Rect activatorRect)
        {
            if (activatorRect == s_LastActivatorRect && EditorApplication.timeSinceStartup - s_LastClosedTime < SameActivatorReopenDelay)
                return false;
            s_LastActivatorRect = activatorRect;
            return true;
        }

        void OpenContent(Rect activatorRect, PopupWindowContent content)
        {
            hideFlags = HideFlags.DontSave;
            wantsMouseMove = true;
            m_Content = content;
            content.editorWindow = this;
            content.OnOpen();
            if (this == null) return;

            ShowAsDropDown(GUIUtility.GUIToScreenRect(activatorRect), Vector2.zero);
            Focus();
            if (this == null) return;

            var retainedContent = content.CreateGUI();
            if (this == null) return;
            if (retainedContent != null)
            {
                var popupRoot = new VisualElement();
                popupRoot.Add(retainedContent);
                return;
            }

            var size = content.GetWindowSize();
            if (this != null) position = new Rect(position.position, size);
        }

        protected virtual void OnEnable() => AssemblyReloadEvents.beforeAssemblyReload += Close;

        protected virtual void OnDisable()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= Close;
            s_LastClosedTime = EditorApplication.timeSinceStartup;
            CloseContent();
        }

        private protected void CloseContent()
        {
            var content = m_Content;
            m_Content = null;
            content?.OnClose();
        }
    }
}
