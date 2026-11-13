using System;
using Unbound.Runtime;

namespace UnityEngine.UIElements
{
    public delegate bool DynamicAtlasCustomFilter(Texture2D texture, ref DynamicAtlasFilters filtersToApply);

    [Serializable]
    public partial class DynamicAtlasSettings
    {
        [SerializeField, HideInInspector] int m_MinAtlasSize, m_MaxAtlasSize, m_MaxSubTextureSize;
        [SerializeField, HideInInspector] DynamicAtlasFilters m_ActiveFilters;
        DynamicAtlasCustomFilter m_CustomFilter;

        // The default factory and independent panel ownership are recorded by defaultstate, 6000.0.71f1.
        const int DefaultMinimumAtlasSize = 64, DefaultMaximumAtlasSize = 4096, DefaultMaximumSubTextureSize = 64;
        const DynamicAtlasFilters MeasuredFilters = DynamicAtlasFilters.Readability | DynamicAtlasFilters.Size
            | DynamicAtlasFilters.Format | DynamicAtlasFilters.ColorSpace | DynamicAtlasFilters.FilterMode;

        [Unbound.EditorFaithful("new settings contain zero-initialized storage; defaults are supplied by the separate factory (defaultstate)")]
        public DynamicAtlasSettings() { }

        public int minAtlasSize { get => m_MinAtlasSize; set => m_MinAtlasSize = value; }
        public int maxAtlasSize { get => m_MaxAtlasSize; set => m_MaxAtlasSize = value; }
        public int maxSubTextureSize { get => m_MaxSubTextureSize; set => m_MaxSubTextureSize = value; }
        public DynamicAtlasFilters activeFilters { get => m_ActiveFilters; set => m_ActiveFilters = value; }
        public DynamicAtlasCustomFilter customFilter { get => m_CustomFilter; set => m_CustomFilter = value; }

        public static DynamicAtlasFilters defaultFilters
        {
            get
            {
                if (Compat.DefaultConstructorStateMeasured) return MeasuredFilters;
                Stubs.NotImplemented("UnityEngine.UIElements.DynamicAtlasSettings.defaultFilters"); return default;
            }
        }

        public static DynamicAtlasSettings defaults
        {
            get
            {
                if (!Compat.DefaultConstructorStateMeasured)
                { Stubs.NotImplemented("UnityEngine.UIElements.DynamicAtlasSettings.defaults"); return null; }
                return new DynamicAtlasSettings
                {
                    minAtlasSize = DefaultMinimumAtlasSize, maxAtlasSize = DefaultMaximumAtlasSize,
                    maxSubTextureSize = DefaultMaximumSubTextureSize, activeFilters = defaultFilters
                };
            }
        }
    }
}
