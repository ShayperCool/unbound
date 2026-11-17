namespace UnityEngine.UIElements.Experimental
{
    // Pointer events over the <link> tags of a text element's rich text: sent to the text element
    // (bubbling and trickling down) with the link's id and text, as the Unity 6000.0.71f1 Editor does
    // (tests/projects/uitklinks).

    public sealed partial class PointerDownLinkTagEvent : PointerEventBase<PointerDownLinkTagEvent>
    {
        public PointerDownLinkTagEvent() { LocalInit(); }

        /// <summary>The link's id (its tag's value).</summary>
        public string linkID { get; private set; }

        /// <summary>The text between the link's tags.</summary>
        public string linkText { get; private set; }

        protected override void Init()
        {
            base.Init();
            LocalInit();
        }

        void LocalInit() => propagation = EventPropagation.BubblesOrTricklesDown;

        public static PointerDownLinkTagEvent GetPooled(IPointerEvent evt, string linkID, string linkText)
        {
            var e = GetPooled(evt);
            e.linkID = linkID;
            e.linkText = linkText;
            return e;
        }
    }

    public partial class PointerUpLinkTagEvent : PointerEventBase<PointerUpLinkTagEvent>
    {
        public PointerUpLinkTagEvent() { LocalInit(); }

        public string linkID { get; private set; }

        public string linkText { get; private set; }

        protected override void Init()
        {
            base.Init();
            LocalInit();
        }

        void LocalInit() => propagation = EventPropagation.BubblesOrTricklesDown;

        public static PointerUpLinkTagEvent GetPooled(IPointerEvent evt, string linkID, string linkText)
        {
            var e = GetPooled(evt);
            e.linkID = linkID;
            e.linkText = linkText;
            return e;
        }
    }

    [EventCategory(EventCategory.PointerMove)]
    public partial class PointerMoveLinkTagEvent : PointerEventBase<PointerMoveLinkTagEvent>
    {
        public PointerMoveLinkTagEvent() { LocalInit(); }

        public string linkID { get; private set; }

        public string linkText { get; private set; }

        protected override void Init()
        {
            base.Init();
            LocalInit();
        }

        void LocalInit() => propagation = EventPropagation.BubblesOrTricklesDown;

        public static PointerMoveLinkTagEvent GetPooled(IPointerEvent evt, string linkID, string linkText)
        {
            var e = GetPooled(evt);
            e.linkID = linkID;
            e.linkText = linkText;
            return e;
        }
    }

    [EventCategory(EventCategory.EnterLeave)]
    public partial class PointerOverLinkTagEvent : PointerEventBase<PointerOverLinkTagEvent>
    {
        public PointerOverLinkTagEvent() { LocalInit(); }

        public string linkID { get; private set; }

        public string linkText { get; private set; }

        protected override void Init()
        {
            base.Init();
            LocalInit();
        }

        void LocalInit() => propagation = EventPropagation.BubblesOrTricklesDown;

        public static PointerOverLinkTagEvent GetPooled(IPointerEvent evt, string linkID, string linkText)
        {
            var e = GetPooled(evt);
            e.linkID = linkID;
            e.linkText = linkText;
            return e;
        }
    }

    /// <summary>The pointer left the link it was over (the event names no link).</summary>
    [EventCategory(EventCategory.EnterLeave)]
    public partial class PointerOutLinkTagEvent : PointerEventBase<PointerOutLinkTagEvent>
    {
        public PointerOutLinkTagEvent() { LocalInit(); }

        protected override void Init()
        {
            base.Init();
            LocalInit();
        }

        void LocalInit() => propagation = EventPropagation.BubblesOrTricklesDown;

        public static PointerOutLinkTagEvent GetPooled(IPointerEvent evt, string linkID) => GetPooled(evt);
    }
}
