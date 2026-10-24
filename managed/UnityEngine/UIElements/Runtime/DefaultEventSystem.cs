using System;
using System.Collections.Generic;

namespace UnityEngine.UIElements
{
    /// <summary>
    /// Feeds player panels with input when no EventSystem component does: pointer events go to the
    /// topmost panel under the pointer (or the one holding the pointer), keyboard and navigation
    /// events to the focused panel, as Unity's default event system sends them.
    /// </summary>
    internal sealed partial class DefaultEventSystem
    {
        public enum UpdateMode
        {
            Always = 0,
            IgnoreIfAppNotFocused = 1,
        }

        BaseRuntimePanel m_FocusedPanel;
        internal BaseRuntimePanel m_PreviousFocusedPanel;
        internal Focusable m_PreviousFocusedElement;

        bool m_IsInputReady;

        /// <summary>Whether player panels take input from this event system (it subscribes to InputForUI then).</summary>
        internal bool isInputReady
        {
            get => m_IsInputReady;
            set
            {
                if (m_IsInputReady == value) return;
                m_IsInputReady = value;
                Unbound.UIElements.UaInput.SetInputReady(this, value);
            }
        }

        public void Reset()
        {
            Unbound.UIElements.UaInput.ResetProcessor();
            m_FocusedPanel = null;
        }

        public BaseRuntimePanel focusedPanel
        {
            get => m_FocusedPanel;
            set
            {
                if (m_FocusedPanel == value) return;
                m_FocusedPanel?.Blur();
                m_FocusedPanel = value;
                m_FocusedPanel?.Focus();
            }
        }

        internal void OnFocusEvent(RuntimePanel panel, FocusEvent evt) => focusedPanel = panel;

        /// <summary>Sends the input of the frame (desktop players ignore it while the application is not focused).</summary>
        public void Update(UpdateMode updateMode = UpdateMode.Always)
        {
            if (!isInputReady) return;
            bool desktop = SystemInfo.operatingSystemFamily == OperatingSystemFamily.MacOSX || SystemInfo.operatingSystemFamily == OperatingSystemFamily.Windows ||
                SystemInfo.operatingSystemFamily == OperatingSystemFamily.Linux;
            if (!Application.isFocused && desktop && updateMode == UpdateMode.IgnoreIfAppNotFocused) return;
            Unbound.UIElements.UaInput.Process(this);
        }

        internal void SendFocusBasedEvent<TArg>(Func<TArg, EventBase> factory, TArg arg)
        {
            if (m_PreviousFocusedPanel != null)
            {
                using (var e = factory(arg))
                {
                    e.elementTarget = (VisualElement)m_PreviousFocusedElement ?? m_PreviousFocusedPanel.visualTree;
                    m_PreviousFocusedPanel.visualTree.SendEvent(e);
                    UpdateFocusedPanel(m_PreviousFocusedPanel);
                }
                return;
            }
            var panels = UIElementsRuntimeUtility.GetSortedPlayerPanels();
            for (int i = panels.Count - 1; i >= 0; i--)
            {
                if (!(panels[i] is BaseRuntimePanel runtime)) continue;
                using (var e = factory(arg))
                {
                    e.elementTarget = runtime.visualTree;
                    runtime.visualTree.SendEvent(e);
                    if (runtime.focusController.focusedElement != null)
                    {
                        focusedPanel = runtime;
                        break;
                    }
                    if (e.isPropagationStopped) break;
                }
            }
        }

        internal void SendPositionBasedEvent<TArg>(Vector3 mousePosition, Vector3 delta, int pointerId, int? targetDisplay, Func<Vector3, Vector3, TArg, EventBase> factory, TArg arg, bool deselectIfNoTarget = false)
        {
            if (focusedPanel != null) UpdateFocusedPanel(focusedPanel);
            IPanel softCapture = PointerDeviceState.GetPlayerPanelWithSoftPointerCapture(pointerId);
            var capturing = RuntimePanel.s_EventDispatcher.m_UaPointerState.GetCapturingElement(pointerId);
            if (capturing is VisualElement ve) softCapture = ve.panel;
            BaseRuntimePanel target = null;
            Vector2 panelPosition = Vector2.zero, panelDelta = Vector2.zero;
            if (softCapture is BaseRuntimePanel captured)
            {
                target = captured;
                target.ScreenToPanel(mousePosition, delta, out panelPosition, out panelDelta);
            }
            else
            {
                var panels = UIElementsRuntimeUtility.GetSortedPlayerPanels();
                for (int i = panels.Count - 1; i >= 0; i--)
                {
                    if (panels[i] is BaseRuntimePanel runtime && (!targetDisplay.HasValue || runtime.targetDisplay == targetDisplay) &&
                        runtime.ScreenToPanel(mousePosition, delta, out panelPosition, out panelDelta) && runtime.Pick(panelPosition) != null)
                    {
                        target = runtime;
                        break;
                    }
                }
            }
            var previous = PointerDeviceState.GetPanel(pointerId, ContextType.Player) as BaseRuntimePanel;
            if (previous != target)
            {
                previous?.PointerLeavesPanel(pointerId, previous.ScreenToPanel(mousePosition));
                target?.PointerEntersPanel(pointerId, panelPosition);
            }
            if (target != null)
            {
                using (var e = factory(panelPosition, panelDelta, arg))
                {
                    target.visualTree.SendEvent(e);
                    if (e.processedByFocusController) UpdateFocusedPanel(target);
                    if (e.eventTypeId == PointerDownEvent.TypeId()) PointerDeviceState.SetPlayerPanelWithSoftPointerCapture(pointerId, target);
                    else if (e.eventTypeId == PointerUpEvent.TypeId() && ((PointerUpEvent)e).pressedButtons == 0) PointerDeviceState.SetPlayerPanelWithSoftPointerCapture(pointerId, null);
                }
                return;
            }
            if (deselectIfNoTarget) focusedPanel = null;
        }

        void UpdateFocusedPanel(BaseRuntimePanel runtimePanel)
        {
            if (runtimePanel.focusController.focusedElement != null) focusedPanel = runtimePanel;
            else if (focusedPanel == runtimePanel) focusedPanel = null;
        }

        /// <summary>The focused panel and element captured for one batch of input events.</summary>
        internal IDisposable UaFocusSequence()
        {
            m_PreviousFocusedPanel = focusedPanel;
            m_PreviousFocusedElement = focusedPanel?.focusController.GetLeafFocusedElement();
            return new Sequence(this);
        }

        sealed class Sequence : IDisposable
        {
            readonly DefaultEventSystem m_System;
            public Sequence(DefaultEventSystem system) { m_System = system; }
            public void Dispose()
            {
                m_System.m_PreviousFocusedPanel = null;
                m_System.m_PreviousFocusedElement = null;
            }
        }
    }

    internal partial class Panel
    {
        /// <summary>The panel's name (its panel settings' name for runtime panels).</summary>
        internal string name { get; set; }

        internal void Focus() { }

        internal void Blur() => focusController?.BlurLastFocusedElement();
    }
}
