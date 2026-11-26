using System;

namespace UnityEngine
{
    /// <summary>
    /// The base of the Tile Palette's brushes, as Unity's own C# declares it: every tool does
    /// nothing until a brush overrides it, and a box fill or erase paints or erases each cell of
    /// the box, z slowest, then rows, then columns.
    /// </summary>
    public abstract partial class GridBrushBase : ScriptableObject
    {
        /// <summary>Why a tool of the base class does nothing: Unity's own GridBrushBase leaves each
        /// to the brush that overrides it.</summary>
        const string BaseToolDoesNothing = "Unity's GridBrushBase leaves this tool to the brush that overrides it";

        protected GridBrushBase() { }

        [Unbound.EditorFaithful(BaseToolDoesNothing)]
        public virtual void Paint(GridLayout gridLayout, GameObject brushTarget, Vector3Int position) { }

        [Unbound.EditorFaithful(BaseToolDoesNothing)]
        public virtual void Erase(GridLayout gridLayout, GameObject brushTarget, Vector3Int position) { }

        public virtual void BoxFill(GridLayout gridLayout, GameObject brushTarget, BoundsInt position)
        {
            for (int z = position.zMin; z < position.zMax; z++)
                for (int y = position.yMin; y < position.yMax; y++)
                    for (int x = position.xMin; x < position.xMax; x++)
                        Paint(gridLayout, brushTarget, new Vector3Int(x, y, z));
        }

        public virtual void BoxErase(GridLayout gridLayout, GameObject brushTarget, BoundsInt position)
        {
            for (int z = position.zMin; z < position.zMax; z++)
                for (int y = position.yMin; y < position.yMax; y++)
                    for (int x = position.xMin; x < position.xMax; x++)
                        Erase(gridLayout, brushTarget, new Vector3Int(x, y, z));
        }

        [Unbound.EditorFaithful(BaseToolDoesNothing)]
        public virtual void Select(GridLayout gridLayout, GameObject brushTarget, BoundsInt position) { }

        [Unbound.EditorFaithful(BaseToolDoesNothing)]
        public virtual void FloodFill(GridLayout gridLayout, GameObject brushTarget, Vector3Int position) { }

        [Unbound.EditorFaithful(BaseToolDoesNothing)]
        public virtual void Rotate(RotationDirection direction, GridLayout.CellLayout layout) { }

        [Unbound.EditorFaithful(BaseToolDoesNothing)]
        public virtual void Flip(FlipAxis flip, GridLayout.CellLayout layout) { }

        [Unbound.EditorFaithful(BaseToolDoesNothing)]
        public virtual void Pick(GridLayout gridLayout, GameObject brushTarget, BoundsInt position, Vector3Int pivot) { }

        [Unbound.EditorFaithful(BaseToolDoesNothing)]
        public virtual void Move(GridLayout gridLayout, GameObject brushTarget, BoundsInt from, BoundsInt to) { }

        [Unbound.EditorFaithful(BaseToolDoesNothing)]
        public virtual void MoveStart(GridLayout gridLayout, GameObject brushTarget, BoundsInt position) { }

        [Unbound.EditorFaithful(BaseToolDoesNothing)]
        public virtual void MoveEnd(GridLayout gridLayout, GameObject brushTarget, BoundsInt position) { }

        [Unbound.EditorFaithful(BaseToolDoesNothing)]
        public virtual void ChangeZPosition(int change) { }

        [Unbound.EditorFaithful(BaseToolDoesNothing)]
        public virtual void ResetZPosition() { }

        public enum Tool
        {
            Select = 0,
            Move = 1,
            Paint = 2,
            Box = 3,
            Pick = 4,
            Erase = 5,
            FloodFill = 6,
            Other = 7,
        }

        public enum RotationDirection
        {
            Clockwise = 0,
            CounterClockwise = 1,
        }

        public enum FlipAxis
        {
            X = 0,
            Y = 1,
        }
    }

    /// <summary>Marks a brush class for the Tile Palette: whether its asset instances and its
    /// default instance are hidden, whether it is the default brush and the name it is listed by.</summary>
    [AttributeUsage(AttributeTargets.Class)]
    public partial class CustomGridBrushAttribute : Attribute
    {
        readonly bool m_HideAssetInstances;
        readonly bool m_HideDefaultInstance;
        readonly bool m_DefaultBrush;
        readonly string m_DefaultName;

        public CustomGridBrushAttribute() : this(false, false, false, "") { }

        public CustomGridBrushAttribute(bool hideAssetInstances, bool hideDefaultInstance, bool defaultBrush, string defaultName)
        {
            m_HideAssetInstances = hideAssetInstances;
            m_HideDefaultInstance = hideDefaultInstance;
            m_DefaultBrush = defaultBrush;
            m_DefaultName = defaultName;
        }

        public bool hideAssetInstances => m_HideAssetInstances;
        public bool hideDefaultInstance => m_HideDefaultInstance;
        public bool defaultBrush => m_DefaultBrush;
        public string defaultName => m_DefaultName;
    }
}
