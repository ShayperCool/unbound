using System.Collections.Generic;
using UnityEngine.UIElements;
using Xunit;

namespace UnityEngine.Tests
{
    // tests/projects/columnnotify: two identical Unity 6000.0.71f1 captures.
    public class ColumnBindingNotificationTests
    {
        static List<string> Listen(List<string> events, INotifyBindablePropertyChanged source, string name)
        {
            source.propertyChanged += (sender, args) => events.Add(name + ":" + args.propertyName);
            return events;
        }

        [Fact]
        public void ChangedPropertiesNotifyAndColumnsForwardIndicesAboveZero()
        {
            var events = new List<string>();
            var columns = new Columns();
            var first = new Column();
            var second = new Column();
            Listen(events, columns, "columns");
            Listen(events, first, "first");
            Listen(events, second, "second");
            columns.Add(first);
            columns.Add(second);
            Assert.Empty(events);

            second.title = "B";
            second.width = 120;
            second.width = 120;
            second.makeCell = () => new Label();
            first.visible = false;
            Assert.Equal(new[] { "second:title", "columns:columns[1].title", "second:width", "columns:columns[1].width", "first:visible" }, events);

            events.Clear();
            columns.Insert(0, new Column());
            first.visible = true;
            columns.Remove(second);
            second.title = "C";
            columns.stretchMode = Columns.StretchMode.Grow;
            columns.stretchMode = Columns.StretchMode.Grow;
            Assert.Equal(new[] { "first:visible", "columns:columns[1].visible", "second:title", "columns:stretchMode" }, events);

            events.Clear();
            var sort = new SortColumnDescription(3, SortDirection.Descending);
            Listen(events, sort, "sort");
            sort.columnIndex = 4;
            sort.columnIndex = 4;
            sort.direction = SortDirection.Ascending;
            sort.columnName = "B";
            Assert.Equal(new[] { "sort:columnIndex", "sort:direction", "sort:columnName" }, events);
        }
    }
}
