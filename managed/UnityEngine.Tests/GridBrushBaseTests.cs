using System.Collections.Generic;
using UnityEngine;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The Tile Palette's brush base and settings types, as Unity's own C# declares them
    /// (defaults recorded by tests/projects/defaultstate).</summary>
    public class GridBrushBaseTests
    {
        sealed class RecordingBrush : GridBrushBase
        {
            internal readonly List<string> Calls = new List<string>();
            public override void Paint(GridLayout gridLayout, GameObject brushTarget, Vector3Int position) => Calls.Add("paint " + position);
            public override void Erase(GridLayout gridLayout, GameObject brushTarget, Vector3Int position) => Calls.Add("erase " + position);
        }

        [Fact]
        public void BoxToolsVisitCellsZSlowestThenRowsThenColumns()
        {
            var brush = ScriptableObject.CreateInstance<RecordingBrush>();
            brush.BoxFill(null, null, new BoundsInt(new Vector3Int(1, 2, 0), new Vector3Int(2, 2, 2)));
            Assert.Equal(new[]
            {
                "paint (1, 2, 0)", "paint (2, 2, 0)", "paint (1, 3, 0)", "paint (2, 3, 0)",
                "paint (1, 2, 1)", "paint (2, 2, 1)", "paint (1, 3, 1)", "paint (2, 3, 1)",
            }, brush.Calls);
            brush.Calls.Clear();
            brush.BoxErase(null, null, new BoundsInt(new Vector3Int(0, 0, 0), new Vector3Int(1, 2, 1)));
            Assert.Equal(new[] { "erase (0, 0, 0)", "erase (0, 1, 0)" }, brush.Calls);
            brush.Calls.Clear();
            brush.BoxFill(null, null, new BoundsInt(Vector3Int.zero, new Vector3Int(0, 3, 1)));
            Assert.Empty(brush.Calls);
            // The other tools do nothing until a brush overrides them.
            brush.Select(null, null, default);
            brush.FloodFill(null, null, Vector3Int.zero);
            brush.Rotate(GridBrushBase.RotationDirection.Clockwise, GridLayout.CellLayout.Rectangle);
            brush.ChangeZPosition(1);
            Assert.Empty(brush.Calls);
            Object.DestroyImmediate(brush);
        }

        [Fact]
        public void AttributeAndPaletteKeepTheirValues()
        {
            var plain = new CustomGridBrushAttribute();
            Assert.False(plain.hideAssetInstances || plain.hideDefaultInstance || plain.defaultBrush);
            Assert.Equal("", plain.defaultName);
            var named = new CustomGridBrushAttribute(true, false, true, "Line");
            Assert.True(named.hideAssetInstances);
            Assert.False(named.hideDefaultInstance);
            Assert.True(named.defaultBrush);
            Assert.Equal("Line", named.defaultName);

            var palette = ScriptableObject.CreateInstance<UnityEditor.GridPalette>();
            Assert.Equal(UnityEditor.GridPalette.CellSizing.Automatic, palette.cellSizing);
            Assert.Equal(TransparencySortMode.Default, palette.transparencySortMode);
            Assert.Equal(new Vector3(0f, 0f, 1f), palette.transparencySortAxis);
            palette.transparencySortAxis = Vector3.up;
            Assert.Equal(Vector3.up, palette.transparencySortAxis);
            Object.DestroyImmediate(palette);
        }
    }
}
