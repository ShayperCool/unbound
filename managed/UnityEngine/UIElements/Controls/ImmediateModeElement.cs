using System;
using Unity.Properties;

namespace UnityEngine.UIElements
{
    /// <summary>
    /// An element that draws with GL or Graphics.DrawMeshNow from <see cref="ImmediateRepaint"/>, at
    /// its place among the panel's draws: each time the panel renders, with GL's modelview set to the
    /// element's world transform (its local space in points) and drawing scissored to its clip.
    /// <see cref="cullingEnabled"/> skips the callback while the element is outside the target.
    /// Matches the Unity 6000.0.71f1 Editor (tests/projects/uitkimmediate).
    /// </summary>
    public abstract partial class ImmediateModeElement : VisualElement
    {
        internal static readonly BindingId cullingEnabledProperty = nameof(cullingEnabled);

        bool m_CullingEnabled;
        readonly Action m_ImmediateRepaint;

        /// <summary>Whether ImmediateRepaint is skipped while the element's bounds are outside the
        /// panel's target.</summary>
        [CreateProperty]
        public bool cullingEnabled
        {
            get => m_CullingEnabled;
            set
            {
                if (m_CullingEnabled == value) return;
                m_CullingEnabled = value;
                IncrementVersion(VersionChangeType.Repaint);
                NotifyPropertyChanged(cullingEnabledProperty);
            }
        }

        public ImmediateModeElement()
        {
            m_ImmediateRepaint = ImmediateRepaint;
            generateVisualContent += OnGenerateVisualContent;
        }

        void OnGenerateVisualContent(MeshGenerationContext mgc)
        {
            if (elementPanel is BaseRuntimePanel { drawsInCameras: true })
            {
                Debug.LogError("ImmediateModeElement cannot be used in a panel drawn by cameras.");
                return;
            }
            mgc.uaPainter.DrawImmediate(this, m_ImmediateRepaint, cullingEnabled);
        }

        /// <summary>Draws the element with immediate-mode graphics, in its local space.</summary>
        protected abstract void ImmediateRepaint();
    }

    /// <summary>What a panel's rendering throws after it finished when an ImmediateRepaint threw
    /// (the first such exception, inside it).</summary>
    internal partial class ImmediateModeException : Exception
    {
        public ImmediateModeException(Exception inner) : base("", inner) { }
    }
}
