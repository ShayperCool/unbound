using System;
using UnityEngine;

namespace UnityEditor
{
    internal partial class GUILayoutFadeGroup : GUILayoutGroup
    {
        public float fadeValue;
        public bool wasGUIEnabled;
        public Color guiColor;

        public GUILayoutFadeGroup() { }

        public override void CalcHeight()
        {
            base.CalcHeight();
            minHeight *= fadeValue;
            maxHeight *= fadeValue;
        }
    }

    internal sealed partial class EditorGUILayoutUtilityInternal : GUILayoutUtility
    {
        public EditorGUILayoutUtilityInternal() { }

        internal static GUILayoutGroup topLevel => GUILayoutUtility.topLevel;

        internal static GUILayoutGroup BeginLayoutArea(GUIStyle style, Type LayoutType) =>
            GUILayoutUtility.DoBeginLayoutArea(style, LayoutType);
    }

    internal sealed partial class EditorGUIInternal : GUI
    {
        internal static Vector2 DoBeginScrollViewForward(Rect position, Vector2 scrollPosition, Rect viewRect,
            bool alwaysShowHorizontal, bool alwaysShowVertical, GUIStyle horizontalScrollbar,
            GUIStyle verticalScrollbar, GUIStyle background) =>
            GUI.BeginScrollView(position, scrollPosition, viewRect, alwaysShowHorizontal, alwaysShowVertical,
                horizontalScrollbar, verticalScrollbar, background);
    }

    public sealed partial class EditorGUILayout
    {
        public static bool BeginFadeGroup(float value)
        {
            EditorGUI.RequireGUI();
            var g = (GUILayoutFadeGroup)GUILayoutUtility.BeginLayoutGroup(GUIStyle.none, null, typeof(GUILayoutFadeGroup));
            g.isVertical = true;
            g.resetCoords = false;
            g.fadeValue = value;
            g.wasGUIEnabled = GUI.enabled;
            g.guiColor = GUI.color;
            g.consideredForMargin = value > 0f;
            EditorGUIUtility.LockContextWidth();
            if (value != 0f && value != 1f)
            {
                g.resetCoords = true;
                GUI.BeginGroup(g.rect);
                if (Event.current.type == EventType.MouseDown)
                    Event.current.Use();
            }
            return value != 0f;
        }

        public static void EndFadeGroup()
        {
            var g = EditorGUILayoutUtilityInternal.topLevel as GUILayoutFadeGroup;
            if (g == null)
            {
                Debug.LogWarning("Unexpected call to EndFadeGroup! Make sure to call EndFadeGroup the same number of times as BeginFadeGroup.");
                return;
            }
            if (g.fadeValue != 0f && g.fadeValue != 1f)
                GUI.EndGroup();
            EditorGUIUtility.UnlockContextWidth();
            GUI.enabled = g.wasGUIEnabled;
            GUI.color = g.guiColor;
            GUILayoutUtility.EndLayoutGroup();
        }

        public partial class FadeGroupScope : GUI.Scope
        {
            public bool visible { get; protected set; }

            public FadeGroupScope(float value) => visible = BeginFadeGroup(value);

            protected override void CloseScope() => EndFadeGroup();
        }

        public static Vector2 BeginScrollView(Vector2 scrollPosition, params GUILayoutOption[] options)
        {
            GUIUtility.CheckOnGUI(requireCurrentEvent: true);
            return BeginScrollView(scrollPosition, false, false, GUI.skin.horizontalScrollbar, GUI.skin.verticalScrollbar, GUI.skin.scrollView, options);
        }

        public static Vector2 BeginScrollView(Vector2 scrollPosition, bool alwaysShowHorizontal, bool alwaysShowVertical, params GUILayoutOption[] options)
        {
            GUIUtility.CheckOnGUI(requireCurrentEvent: true);
            return BeginScrollView(scrollPosition, alwaysShowHorizontal, alwaysShowVertical, GUI.skin.horizontalScrollbar, GUI.skin.verticalScrollbar, GUI.skin.scrollView, options);
        }

        public static Vector2 BeginScrollView(Vector2 scrollPosition, GUIStyle horizontalScrollbar, GUIStyle verticalScrollbar, params GUILayoutOption[] options)
        {
            GUIUtility.CheckOnGUI(requireCurrentEvent: true);
            return BeginScrollView(scrollPosition, false, false, horizontalScrollbar, verticalScrollbar, GUI.skin.scrollView, options);
        }

        public static Vector2 BeginScrollView(Vector2 scrollPosition, GUIStyle style, params GUILayoutOption[] options)
        {
            GUIUtility.CheckOnGUI(requireCurrentEvent: true);
            string name = style.name;
            var vertical = GUI.skin.FindStyle(name + "VerticalScrollbar") ?? GUI.skin.verticalScrollbar;
            var horizontal = GUI.skin.FindStyle(name + "HorizontalScrollbar") ?? GUI.skin.horizontalScrollbar;
            return BeginScrollView(scrollPosition, false, false, horizontal, vertical, style, options);
        }

        internal static Vector2 BeginScrollView(Vector2 scrollPosition, bool alwaysShowHorizontal, bool alwaysShowVertical,
            GUIStyle horizontalScrollbar, GUIStyle verticalScrollbar, params GUILayoutOption[] options)
        {
            GUIUtility.CheckOnGUI(requireCurrentEvent: true);
            return BeginScrollView(scrollPosition, alwaysShowHorizontal, alwaysShowVertical, horizontalScrollbar, verticalScrollbar, GUI.skin.scrollView, options);
        }

        public static Vector2 BeginScrollView(Vector2 scrollPosition, bool alwaysShowHorizontal, bool alwaysShowVertical,
            GUIStyle horizontalScrollbar, GUIStyle verticalScrollbar, GUIStyle background, params GUILayoutOption[] options)
        {
            EditorGUI.RequireGUI();
            var g = (GUIScrollGroup)GUILayoutUtility.BeginLayoutGroup(background, null, typeof(GUIScrollGroup));
            if (Event.current.type == EventType.Layout)
            {
                g.resetCoords = true;
                g.isVertical = true;
                g.stretchWidth = 1;
                g.stretchHeight = 1;
                g.verticalScrollbar = verticalScrollbar;
                g.horizontalScrollbar = horizontalScrollbar;
                g.ApplyOptions(options);
            }
            return EditorGUIInternal.DoBeginScrollViewForward(g.rect, scrollPosition, new Rect(0f, 0f, g.clientWidth, g.clientHeight),
                alwaysShowHorizontal, alwaysShowVertical, horizontalScrollbar, verticalScrollbar, background);
        }

        internal static Vector2 BeginVerticalScrollView(Vector2 scrollPosition, params GUILayoutOption[] options)
        {
            GUIUtility.CheckOnGUI(requireCurrentEvent: true);
            return BeginVerticalScrollView(scrollPosition, false, GUI.skin.verticalScrollbar, GUI.skin.scrollView, options);
        }

        internal static Vector2 BeginVerticalScrollView(Vector2 scrollPosition, bool alwaysShowVertical,
            GUIStyle verticalScrollbar, GUIStyle background, params GUILayoutOption[] options)
        {
            EditorGUI.RequireGUI();
            var g = (GUIScrollGroup)GUILayoutUtility.BeginLayoutGroup(background, null, typeof(GUIScrollGroup));
            if (Event.current.type == EventType.Layout)
            {
                g.resetCoords = true;
                g.isVertical = true;
                g.stretchWidth = 1;
                g.stretchHeight = 1;
                g.verticalScrollbar = verticalScrollbar;
                g.horizontalScrollbar = GUIStyle.none;
                g.allowHorizontalScroll = false;
                g.ApplyOptions(options);
            }
            return EditorGUIInternal.DoBeginScrollViewForward(g.rect, scrollPosition, new Rect(0f, 0f, g.clientWidth, g.clientHeight),
                false, alwaysShowVertical, GUI.skin.horizontalScrollbar, verticalScrollbar, background);
        }

        internal static Vector2 BeginHorizontalScrollView(Vector2 scrollPosition, params GUILayoutOption[] options)
        {
            GUIUtility.CheckOnGUI(requireCurrentEvent: true);
            return BeginHorizontalScrollView(scrollPosition, false, GUI.skin.horizontalScrollbar, GUI.skin.scrollView, options);
        }

        internal static Vector2 BeginHorizontalScrollView(Vector2 scrollPosition, bool alwaysShowHorizontal,
            GUIStyle horizontalScrollbar, GUIStyle background, params GUILayoutOption[] options)
        {
            EditorGUI.RequireGUI();
            var g = (GUIScrollGroup)GUILayoutUtility.BeginLayoutGroup(background, null, typeof(GUIScrollGroup));
            if (Event.current.type == EventType.Layout)
            {
                g.resetCoords = true;
                g.isVertical = true;
                g.stretchWidth = 1;
                g.stretchHeight = 1;
                g.verticalScrollbar = GUIStyle.none;
                g.horizontalScrollbar = horizontalScrollbar;
                g.allowHorizontalScroll = true;
                g.allowVerticalScroll = false;
                g.ApplyOptions(options);
            }
            return EditorGUIInternal.DoBeginScrollViewForward(g.rect, scrollPosition, new Rect(0f, 0f, g.clientWidth, g.clientHeight),
                alwaysShowHorizontal, false, horizontalScrollbar, GUI.skin.verticalScrollbar, background);
        }

        public static void EndScrollView() => GUILayout.EndScrollView(true);

        internal static void EndScrollView(bool handleScrollWheel) => GUILayout.EndScrollView(handleScrollWheel);

        public partial class ScrollViewScope : GUI.Scope
        {
            public Vector2 scrollPosition { get; protected set; }
            public bool handleScrollWheel { get; set; }

            public ScrollViewScope(Vector2 scrollPosition, params GUILayoutOption[] options)
            {
                handleScrollWheel = true;
                this.scrollPosition = BeginScrollView(scrollPosition, options);
            }

            public ScrollViewScope(Vector2 scrollPosition, bool alwaysShowHorizontal, bool alwaysShowVertical, params GUILayoutOption[] options)
            {
                handleScrollWheel = true;
                this.scrollPosition = BeginScrollView(scrollPosition, alwaysShowHorizontal, alwaysShowVertical, options);
            }

            public ScrollViewScope(Vector2 scrollPosition, GUIStyle horizontalScrollbar, GUIStyle verticalScrollbar, params GUILayoutOption[] options)
            {
                handleScrollWheel = true;
                this.scrollPosition = BeginScrollView(scrollPosition, horizontalScrollbar, verticalScrollbar, options);
            }

            public ScrollViewScope(Vector2 scrollPosition, GUIStyle style, params GUILayoutOption[] options)
            {
                handleScrollWheel = true;
                this.scrollPosition = BeginScrollView(scrollPosition, style, options);
            }

            public ScrollViewScope(Vector2 scrollPosition, bool alwaysShowHorizontal, bool alwaysShowVertical,
                GUIStyle horizontalScrollbar, GUIStyle verticalScrollbar, GUIStyle background, params GUILayoutOption[] options)
            {
                handleScrollWheel = true;
                this.scrollPosition = BeginScrollView(scrollPosition, alwaysShowHorizontal, alwaysShowVertical,
                    horizontalScrollbar, verticalScrollbar, background, options);
            }

            internal ScrollViewScope(Vector2 scrollPosition, bool alwaysShowHorizontal, bool alwaysShowVertical,
                GUIStyle horizontalScrollbar, GUIStyle verticalScrollbar, params GUILayoutOption[] options)
            {
                handleScrollWheel = true;
                this.scrollPosition = BeginScrollView(scrollPosition, alwaysShowHorizontal, alwaysShowVertical,
                    horizontalScrollbar, verticalScrollbar, options);
            }

            protected override void CloseScope() => EndScrollView(handleScrollWheel);
        }

        internal partial class VerticalScrollViewScope : GUI.Scope
        {
            public Vector2 scrollPosition { get; protected set; }
            public bool handleScrollWheel { get; set; }

            public VerticalScrollViewScope(Vector2 scrollPosition, params GUILayoutOption[] options)
            {
                handleScrollWheel = true;
                this.scrollPosition = BeginVerticalScrollView(scrollPosition, options);
            }

            public VerticalScrollViewScope(Vector2 scrollPosition, bool alwaysShowVertical,
                GUIStyle verticalScrollbar, GUIStyle background, params GUILayoutOption[] options)
            {
                handleScrollWheel = true;
                this.scrollPosition = BeginVerticalScrollView(scrollPosition, alwaysShowVertical, verticalScrollbar, background, options);
            }

            protected override void CloseScope() => EndScrollView(handleScrollWheel);
        }

        internal partial class HorizontalScrollViewScope : GUI.Scope
        {
            public Vector2 scrollPosition { get; protected set; }
            public bool handleScrollWheel { get; set; }

            public HorizontalScrollViewScope(Vector2 scrollPosition, params GUILayoutOption[] options)
            {
                handleScrollWheel = true;
                this.scrollPosition = BeginHorizontalScrollView(scrollPosition, options);
            }

            public HorizontalScrollViewScope(Vector2 scrollPosition, bool alwaysShowHorizontal,
                GUIStyle horizontalScrollbar, GUIStyle background, params GUILayoutOption[] options)
            {
                handleScrollWheel = true;
                this.scrollPosition = BeginHorizontalScrollView(scrollPosition, alwaysShowHorizontal, horizontalScrollbar, background, options);
            }

            protected override void CloseScope() => EndScrollView(handleScrollWheel);
        }
    }
}
