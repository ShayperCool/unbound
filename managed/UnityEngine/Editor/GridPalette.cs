using UnityEngine;

namespace UnityEditor
{
    /// <summary>The settings a Tile Palette asset keeps: how its cells are sized and how its
    /// tiles sort (by default, along z), as Unity's own C# declares them.</summary>
    public partial class GridPalette : ScriptableObject
    {
        [SerializeField] public CellSizing cellSizing;
        [SerializeField] TransparencySortMode m_TransparencySortMode = TransparencySortMode.Default;
        [SerializeField] Vector3 m_TransparencySortAxis = new Vector3(0f, 0f, 1f);

        public GridPalette() { }

        public TransparencySortMode transparencySortMode
        {
            get => m_TransparencySortMode;
            set => m_TransparencySortMode = value;
        }

        public Vector3 transparencySortAxis
        {
            get => m_TransparencySortAxis;
            set => m_TransparencySortAxis = value;
        }

        public enum CellSizing
        {
            Automatic = 0,
            Manual = 100,
        }
    }
}
