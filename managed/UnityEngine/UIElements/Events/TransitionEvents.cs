using System.Collections;
using System.Collections.Generic;

namespace UnityEngine.UIElements
{
    public partial interface ITransitionEvent
    {
        StylePropertyNameCollection stylePropertyNames { get; }
        double elapsedTime { get; }
    }

    public partial struct StylePropertyNameCollection : IEnumerable<StylePropertyName>
    {
        internal List<StylePropertyName> propertiesList;
        internal StylePropertyNameCollection(List<StylePropertyName> list) { propertiesList = list; }
        public Enumerator GetEnumerator() => new Enumerator(propertiesList.GetEnumerator());
        IEnumerator<StylePropertyName> IEnumerable<StylePropertyName>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public bool Contains(StylePropertyName stylePropertyName) => propertiesList.Contains(stylePropertyName);

        public partial struct Enumerator : IEnumerator<StylePropertyName>
        {
            List<StylePropertyName>.Enumerator m_Enumerator;
            internal Enumerator(List<StylePropertyName>.Enumerator enumerator) { m_Enumerator = enumerator; }
            public bool MoveNext() => m_Enumerator.MoveNext();
            public StylePropertyName Current => m_Enumerator.Current;
            object IEnumerator.Current => Current;
            [Unbound.EditorFaithful("Unity's transition property enumerator does not reset its position")]
            public void Reset() { }
            public void Dispose() => m_Enumerator.Dispose();
        }
    }

    public abstract partial class TransitionEventBase<T> : EventBase<T>, ITransitionEvent where T : TransitionEventBase<T>, new()
    {
        public StylePropertyNameCollection stylePropertyNames { get; } = new StylePropertyNameCollection(new List<StylePropertyName>());
        public double elapsedTime { get; protected set; }
        protected TransitionEventBase() { bubbles = true; }
        protected override void Init()
        {
            base.Init();
            bubbles = true;
            stylePropertyNames.propertiesList.Clear();
            elapsedTime = 0;
        }
        public static T GetPooled(StylePropertyName stylePropertyName, double elapsedTime)
        {
            var e = GetPooled();
            e.stylePropertyNames.propertiesList.Add(stylePropertyName);
            e.elapsedTime = elapsedTime;
            return e;
        }
        public bool AffectsProperty(StylePropertyName stylePropertyName) => stylePropertyNames.Contains(stylePropertyName);
    }
    public sealed partial class TransitionRunEvent : TransitionEventBase<TransitionRunEvent> { public TransitionRunEvent() { } }
    public sealed partial class TransitionStartEvent : TransitionEventBase<TransitionStartEvent> { public TransitionStartEvent() { } }
    public sealed partial class TransitionEndEvent : TransitionEventBase<TransitionEndEvent> { public TransitionEndEvent() { } }
    public sealed partial class TransitionCancelEvent : TransitionEventBase<TransitionCancelEvent> { public TransitionCancelEvent() { } }
}
