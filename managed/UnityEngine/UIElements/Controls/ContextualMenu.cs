using System;
using System.Collections.Generic;

// Contextual menus: the menu data (DropdownMenu and its items), the event that asks an element to
// fill a menu (ContextualMenuPopulateEvent), and the manipulator that asks for one on a right
// button release or the Menu key (ContextualMenuManipulator). Documented (Unity 6000.0 Scripting
// API "ContextualMenuManipulator", "ContextualMenuPopulateEvent", "DropdownMenu",
// "DropdownMenuAction"): the manipulator's triggers, the menu's append/insert/remove members, an
// action's status callback read by PrepareForDisplay, Execute.
// A menu is asked for and shown by the panel's ContextualMenuManager. Runtime panels have none
// (IPanel.contextualMenuManager is null), so a ContextualMenuManipulator on a runtime panel shows
// nothing and sends no populate event; matches the Unity 6000.0.71f1 Editor,
// tests/projects/uitkcollections (header-menu section). A populate event a game sends itself still
// reaches the manipulator's menu builder.
namespace UnityEngine.UIElements
{
    public abstract partial class DropdownMenuItem
    {
        protected DropdownMenuItem() { }
    }

    public partial class DropdownMenuSeparator : DropdownMenuItem
    {
        public string subMenuPath { get; }

        public DropdownMenuSeparator(string subMenuPath) => this.subMenuPath = subMenuPath;
    }

    public partial class DropdownMenuEventInfo
    {
        public EventModifiers modifiers { get; }
        public Vector2 mousePosition { get; }
        public Vector2 localMousePosition { get; }

        public DropdownMenuEventInfo(EventBase e)
        {
            switch (e)
            {
                case IMouseEvent mouse:
                    mousePosition = mouse.mousePosition;
                    localMousePosition = mouse.localMousePosition;
                    modifiers = mouse.modifiers;
                    break;
                case IPointerEvent pointer:
                    mousePosition = pointer.position;
                    localMousePosition = pointer.localPosition;
                    modifiers = pointer.modifiers;
                    break;
                case IKeyboardEvent key:
                    modifiers = key.modifiers;
                    break;
            }
        }
    }

    public partial class DropdownMenuAction : DropdownMenuItem
    {
        [Flags]
        public enum Status
        {
            None = 0,
            Normal = 1,
            Disabled = 2,
            Checked = 4,
            Hidden = 8,
        }

        readonly Action<DropdownMenuAction> m_Action;
        readonly Func<DropdownMenuAction, Status> m_StatusCallback;

        public string name { get; }
        public Status status { get; private set; }
        public DropdownMenuEventInfo eventInfo { get; private set; }
        public object userData { get; private set; }

        public static Status AlwaysEnabled(DropdownMenuAction a) => Status.Normal;

        public static Status AlwaysDisabled(DropdownMenuAction a) => Status.Disabled;

        public DropdownMenuAction(string actionName, Action<DropdownMenuAction> actionCallback, Func<DropdownMenuAction, Status> actionStatusCallback, object userData = null)
        {
            name = actionName;
            m_Action = actionCallback;
            m_StatusCallback = actionStatusCallback;
            this.userData = userData;
        }

        /// <summary>Asks the status callback; an action without one is hidden.</summary>
        public void UpdateActionStatus(DropdownMenuEventInfo eventInfo)
        {
            this.eventInfo = eventInfo;
            status = m_StatusCallback != null ? m_StatusCallback(this) : Status.Hidden;
        }

        public void Execute() => m_Action?.Invoke(this);
    }

    public partial class DropdownMenu
    {
        readonly List<DropdownMenuItem> m_Items = new List<DropdownMenuItem>();
        DropdownMenuEventInfo m_EventInfo;

        internal int Count => m_Items.Count;

        public bool allowDuplicateNames { get; set; }

        public DropdownMenu() { }

        public List<DropdownMenuItem> MenuItems() => m_Items;

        static Func<DropdownMenuAction, DropdownMenuAction.Status> StatusCallback(DropdownMenuAction.Status status)
        {
            if (status == DropdownMenuAction.Status.Normal) return DropdownMenuAction.AlwaysEnabled;
            if (status == DropdownMenuAction.Status.Disabled) return DropdownMenuAction.AlwaysDisabled;
            return _ => status;
        }

        public void AppendAction(string actionName, Action<DropdownMenuAction> action, Func<DropdownMenuAction, DropdownMenuAction.Status> actionStatusCallback, object userData = null) =>
            m_Items.Add(new DropdownMenuAction(actionName, action, actionStatusCallback, userData));

        public void AppendAction(string actionName, Action<DropdownMenuAction> action, DropdownMenuAction.Status status = DropdownMenuAction.Status.Normal) =>
            AppendAction(actionName, action, StatusCallback(status));

        public void InsertAction(int atIndex, string actionName, Action<DropdownMenuAction> action, Func<DropdownMenuAction, DropdownMenuAction.Status> actionStatusCallback, object userData = null) =>
            m_Items.Insert(atIndex, new DropdownMenuAction(actionName, action, actionStatusCallback, userData));

        public void InsertAction(int atIndex, string actionName, Action<DropdownMenuAction> action, DropdownMenuAction.Status status = DropdownMenuAction.Status.Normal) =>
            InsertAction(atIndex, actionName, action, StatusCallback(status));

        /// <summary>Adds a separator after the last item, unless the menu is empty, already ends
        /// with a separator of this sub-menu, or holds no action of this sub-menu.</summary>
        public void AppendSeparator(string subMenuPath = null)
        {
            subMenuPath ??= string.Empty;
            if (m_Items.Count == 0) return;
            if (m_Items[m_Items.Count - 1] is DropdownMenuSeparator last && last.subMenuPath == subMenuPath) return;
            foreach (var item in m_Items)
            {
                if (item is DropdownMenuAction action && action.name.StartsWith(subMenuPath, StringComparison.Ordinal))
                {
                    m_Items.Add(new DropdownMenuSeparator(subMenuPath));
                    return;
                }
            }
        }

        /// <summary>Inserts a separator, unless the index is not after an item or follows a separator.</summary>
        public void InsertSeparator(string subMenuPath, int atIndex)
        {
            if (atIndex <= 0 || atIndex > m_Items.Count || m_Items[atIndex - 1] is DropdownMenuSeparator) return;
            m_Items.Insert(atIndex, new DropdownMenuSeparator(subMenuPath ?? string.Empty));
        }

        public void RemoveItemAt(int index) => m_Items.RemoveAt(index);

        public void ClearItems() => m_Items.Clear();

        /// <summary>Updates every action's status for the event and drops a trailing separator.</summary>
        public void PrepareForDisplay(EventBase e)
        {
            m_EventInfo = e != null ? new DropdownMenuEventInfo(e) : null;
            if (m_Items.Count == 0) return;
            foreach (var item in m_Items)
                if (item is DropdownMenuAction action) action.UpdateActionStatus(m_EventInfo);
            if (m_Items[m_Items.Count - 1] is DropdownMenuSeparator) m_Items.RemoveAt(m_Items.Count - 1);
        }
    }

    public abstract partial class ContextualMenuManager
    {
        public void DisplayMenu(EventBase triggerEvent, IEventHandler target) => DisplayMenu(triggerEvent, target, new DropdownMenu());

        /// <summary>Sends the populate event to the target; the event shows the filled menu
        /// through DoDisplayMenu once dispatched.</summary>
        internal void DisplayMenu(EventBase triggerEvent, IEventHandler target, DropdownMenu menu)
        {
            int pointerId = triggerEvent is IPointerEvent pointerEvent ? pointerEvent.pointerId : PointerId.mousePointerId;
            int button;
            using (var populate = ContextualMenuPopulateEvent.GetPooled(triggerEvent, menu, target, this))
            {
                button = populate.button;
                target?.SendEvent(populate);
            }
            if (!ContextualMenuManipulator.isOSXContextualMenuPlatform) return;
            // The press showed the menu: its release does not show another, and the button the OS
            // menu swallowed the release of is no longer held.
            displayMenuHandledOSX = true;
            if (button >= 0) PointerDeviceState.ReleaseButton(pointerId, button);
        }

        protected internal abstract void DoDisplayMenu(DropdownMenu menu, EventBase triggerEvent);
    }

    public partial class ContextualMenuPopulateEvent : MouseEventBase<ContextualMenuPopulateEvent>
    {
        ContextualMenuManager m_Manager;

        public DropdownMenu menu { get; private set; }

        public EventBase triggerEvent { get; private set; }

        /// <summary>An event for the target carrying the trigger's pointer or mouse data; the
        /// trigger stays acquired until this event is released.</summary>
        public static ContextualMenuPopulateEvent GetPooled(EventBase triggerEvent, DropdownMenu menu, IEventHandler target, ContextualMenuManager menuManager)
        {
            var e = GetPooled(triggerEvent);
            if (triggerEvent != null)
            {
                triggerEvent.Acquire();
                e.triggerEvent = triggerEvent;
                if (triggerEvent is IMouseEvent mouse)
                {
                    e.modifiers = mouse.modifiers;
                    e.mousePosition = mouse.mousePosition;
                    e.localMousePosition = mouse.mousePosition;
                    e.mouseDelta = mouse.mouseDelta;
                    e.button = mouse.button;
                    e.clickCount = mouse.clickCount;
                }
                else if (triggerEvent is IPointerEvent pointer)
                {
                    e.modifiers = pointer.modifiers;
                    e.mousePosition = pointer.position;
                    e.localMousePosition = pointer.position;
                    e.mouseDelta = pointer.deltaPosition;
                    e.button = pointer.button;
                    e.clickCount = pointer.clickCount;
                }
                if (triggerEvent is IMouseEventInternal mouseInternal) ((IMouseEventInternal)e).triggeredByOS = mouseInternal.triggeredByOS;
                else if (triggerEvent is IPointerEventInternal pointerInternal) ((IMouseEventInternal)e).triggeredByOS = pointerInternal.triggeredByOS;
            }
            e.elementTarget = (VisualElement)target;
            e.menu = menu;
            e.m_Manager = menuManager;
            return e;
        }

        protected override void Init()
        {
            base.Init();
            LocalInit();
        }

        void LocalInit()
        {
            menu = null;
            m_Manager = null;
            triggerEvent?.Dispose();
            triggerEvent = null;
        }

        public ContextualMenuPopulateEvent() => LocalInit();

        /// <summary>A menu that got items is shown by the manager that asked for it.</summary>
        protected internal override void PostDispatch(IPanel panel)
        {
            if (menu.Count > 0 && m_Manager != null)
            {
                menu.PrepareForDisplay(triggerEvent);
                m_Manager.DoDisplayMenu(menu, triggerEvent);
            }
            base.PostDispatch(panel);
        }
    }

    /// <summary>Asks the panel's contextual menu manager for a menu when the right button is
    /// released over the target or the Menu key is released on it, and fills the menu through the
    /// builder. On macOS the press asks (also the left button with Control), and the release only
    /// when the press did not.</summary>
    public partial class ContextualMenuManipulator : PointerManipulator
    {
        readonly Action<ContextualMenuPopulateEvent> m_MenuBuilder;

        internal static bool isOSXContextualMenuPlatform =>
            Application.platform == RuntimePlatform.OSXEditor || Application.platform == RuntimePlatform.OSXPlayer;

        public ContextualMenuManipulator(Action<ContextualMenuPopulateEvent> menuBuilder)
        {
            m_MenuBuilder = menuBuilder;
            activators.Add(new ManipulatorActivationFilter { button = MouseButton.RightMouse });
            if (IsOSXContextualMenuPlatform())
                activators.Add(new ManipulatorActivationFilter { button = MouseButton.LeftMouse, modifiers = EventModifiers.Control });
        }

        protected override void RegisterCallbacksOnTarget()
        {
            if (IsOSXContextualMenuPlatform()) target.RegisterCallback<PointerDownEvent>(OnPointerDown);
            target.RegisterCallback<PointerUpEvent>(OnPointerUp);
            target.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            target.RegisterCallback<KeyUpEvent>(OnKeyUp);
            target.RegisterCallback<ContextualMenuPopulateEvent>(OnPopulate);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            if (IsOSXContextualMenuPlatform()) target.UnregisterCallback<PointerDownEvent>(OnPointerDown);
            target.UnregisterCallback<PointerUpEvent>(OnPointerUp);
            target.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
            target.UnregisterCallback<KeyUpEvent>(OnKeyUp);
            target.UnregisterCallback<ContextualMenuPopulateEvent>(OnPopulate);
        }

        protected bool IsOSXContextualMenuPlatform() => isOSXContextualMenuPlatform;

        ContextualMenuManager manager => target.panel?.contextualMenuManager;

        void OnPointerDown(PointerDownEvent e) => Pressed(e);

        void OnPointerUp(PointerUpEvent e) => Released(e);

        // A move may carry a button change of a second button.
        void OnPointerMove(PointerMoveEvent e)
        {
            if (e.isPointerUp) Released(e);
            else if (e.isPointerDown && IsOSXContextualMenuPlatform()) Pressed(e);
        }

        void Pressed<T>(T e) where T : EventBase, IPointerEvent
        {
            var m = manager;
            if (m != null) m.displayMenuHandledOSX = false;
            if (CanStartManipulation(e)) Display(e);
        }

        void Released<T>(T e) where T : EventBase, IPointerEvent
        {
            if (IsOSXContextualMenuPlatform() && manager is ContextualMenuManager m && m.displayMenuHandledOSX) return;
            if (CanStartManipulation(e)) Display(e);
        }

        void OnKeyUp(KeyUpEvent e)
        {
            if (e.keyCode == KeyCode.Menu) Display(e);
        }

        void Display(EventBase e)
        {
            var m = manager;
            if (m == null) return;
            m.DisplayMenu(e, target);
            e.StopPropagation();
        }

        void OnPopulate(ContextualMenuPopulateEvent e) => m_MenuBuilder?.Invoke(e);
    }
}
