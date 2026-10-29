using System;
using System.Collections.Generic;
using Unbound.UIElements;

// The runtime side of UI Toolkit: the player panels, their per-frame update in the player loop
// (PreLateUpdate.UIElementsUpdatePanels), their rendering, and the default event system feeding
// them input.

namespace UnityEngine.UIElements
{
    internal static partial class UIElementsRuntimeUtility
    {
        public delegate BaseRuntimePanel CreateRuntimePanelDelegate(ScriptableObject ownerObject);

        static readonly Dictionary<int, Panel> s_Panels = new Dictionary<int, Panel>();
        static readonly List<Panel> s_SortedRuntimePanels = new List<Panel>();
        static bool s_PanelOrderingDirty = true;
        static bool s_RegisteredPlayerloopCallback;
        static DefaultEventSystem s_DefaultEventSystem;
        static readonly List<PanelSettings> s_PotentiallyEmptyPanelSettings = new List<PanelSettings>();

        internal static DefaultEventSystem defaultEventSystem => s_DefaultEventSystem ??= new DefaultEventSystem();
        internal static Object activeEventSystem { get; private set; }
        internal static bool useDefaultEventSystem => activeEventSystem == null;

        public static BaseRuntimePanel FindOrCreateRuntimePanel(ScriptableObject ownerObject, CreateRuntimePanelDelegate createDelegate)
        {
            int id = ownerObject.GetInstanceID();
            if (s_Panels.TryGetValue(id, out var existing))
            {
                if (existing is BaseRuntimePanel runtime) return runtime;
                s_Panels.Remove(id);
            }
            var panel = createDelegate(ownerObject);
            s_Panels[id] = panel;
            s_PanelOrderingDirty = true;
            if (!s_RegisteredPlayerloopCallback)
            {
                s_RegisteredPlayerloopCallback = true;
                RegisterPlayerloopCallback();
            }
            return panel;
        }

        public static void DisposeRuntimePanel(ScriptableObject ownerObject)
        {
            int id = ownerObject.GetInstanceID();
            if (s_Panels.TryGetValue(id, out var panel))
            {
                panel.Dispose();
                s_Panels.Remove(id);
                s_PanelOrderingDirty = true;
            }
        }

        public static void RegisterPlayerloopCallback() => UaRuntimeHooks.Install();

        // The update hooks stay installed and update only the registered panels.
        public static void UnregisterPlayerloopCallback() =>
            Unbound.Runtime.Stubs.Ignored("UIElementsRuntimeUtility.UnregisterPlayerloopCallback", "does not stop the panels' updates");

        public static void RegisterEventSystem(Object eventSystem)
        {
            if (activeEventSystem != null && activeEventSystem != eventSystem && eventSystem.GetType().Name == "EventSystem")
                Debug.LogWarning("There can be only one active Event System.");
            activeEventSystem = eventSystem;
        }

        public static void UnregisterEventSystem(Object eventSystem)
        {
            if (activeEventSystem == eventSystem) activeEventSystem = null;
        }

        internal static void SetPanelOrderingDirty() => s_PanelOrderingDirty = true;

        internal static List<Panel> GetSortedPlayerPanels()
        {
            if (s_PanelOrderingDirty) SortPanels();
            return s_SortedRuntimePanels;
        }

        static void SortPanels()
        {
            s_SortedRuntimePanels.Clear();
            foreach (var panel in s_Panels.Values)
                if (panel.contextType == ContextType.Player && !panel.disposed) s_SortedRuntimePanels.Add(panel);
            s_SortedRuntimePanels.Sort((a, b) =>
            {
                var ra = a as BaseRuntimePanel;
                var rb = b as BaseRuntimePanel;
                if (ra == null || rb == null) return 0;
                float d = ra.sortingPriority - rb.sortingPriority;
                if (Mathf.Approximately(d, 0f)) return ra.m_RuntimePanelCreationIndex.CompareTo(rb.m_RuntimePanelCreationIndex);
                return d < 0 ? -1 : 1;
            });
            for (int i = 0; i < s_SortedRuntimePanels.Count; i++)
                if (s_SortedRuntimePanels[i] is BaseRuntimePanel r) r.resolvedSortingIndex = i;
            s_PanelOrderingDirty = false;
        }

        internal static void MarkPotentiallyEmpty(PanelSettings settings)
        {
            if (!s_PotentiallyEmptyPanelSettings.Contains(settings)) s_PotentiallyEmptyPanelSettings.Add(settings);
        }

        internal static void RemoveUnusedPanels()
        {
            foreach (var settings in s_PotentiallyEmptyPanelSettings)
                if (settings != null && settings.m_AttachedUIDocuments.Count == 0) settings.DisposePanel();
            s_PotentiallyEmptyPanelSettings.Clear();
        }

        public static void UpdatePanels()
        {
            RemoveUnusedPanels();
            foreach (var p in GetSortedPlayerPanels().ToArray())
                if (p is BaseRuntimePanel runtime && !runtime.disposed) runtime.Update();
            if (useDefaultEventSystem)
            {
                defaultEventSystem.isInputReady = true;
                defaultEventSystem.Update(DefaultEventSystem.UpdateMode.IgnoreIfAppNotFocused);
            }
            else if (s_DefaultEventSystem != null)
            {
                s_DefaultEventSystem.isInputReady = false;
            }
        }

        public static void RepaintPanels(bool onlyOffscreen)
        {
            foreach (var p in GetSortedPlayerPanels())
                if (p is BaseRuntimePanel runtime && (!onlyOffscreen || runtime.targetTexture != null)) runtime.Repaint(Event.current);
        }

        public static void RenderOffscreenPanels()
        {
            foreach (var p in GetSortedPlayerPanels())
                if (p is BaseRuntimePanel runtime && !runtime.drawsInCameras && runtime.targetTexture != null) runtime.Render();
        }

        public static void RepaintPanel(BaseRuntimePanel panel) => panel.Repaint(Event.current);

        public static void RenderPanel(BaseRuntimePanel panel, bool restoreState = true) => panel.Render();

        internal static Vector2 ScreenBottomLeftToPanelPosition(Vector2 position, int targetDisplay) =>
            new Vector2(position.x, Screen.height - position.y);

        internal static Vector2 ScreenBottomLeftToPanelDelta(Vector2 delta) => new Vector2(delta.x, -delta.y);
    }
}

namespace Unbound.UIElements
{
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>Hooks UI Toolkit into the frame: panels update in PreLateUpdate.UIElementsUpdatePanels
    /// (as in Unity) and the screen panels draw over the scene each frame.</summary>
    internal static class UaRuntimeHooks
    {
        static bool s_Installed;

        internal static void Install()
        {
            if (s_Installed) return;
            s_Installed = true;
            Runtime.PlayerLoopDriver.Register(typeof(UnityEngine.PlayerLoop.PreLateUpdate.UIElementsUpdatePanels), UIElementsRuntimeUtility.UpdatePanels);
            PanelRendering.InstallFrameHook();
        }
    }
}
