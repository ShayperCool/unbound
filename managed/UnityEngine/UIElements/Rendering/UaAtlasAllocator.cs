using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unbound.UIElements
{
    // Padded row packing measured in tests/projects/uitkvector, Unity 6000.0.71f1.
    // Areas grow alternately to the right and above; rows group power-of-two source heights.
    internal sealed class UaAtlasAllocator
    {
        internal sealed class Spans
        {
            struct Span { internal int Start, Length; }
            readonly List<Span> m_Free = new List<Span>();
            internal Spans(int length) => m_Free.Add(new Span { Length = length });
            internal int Allocate(int length)
            {
                int best = -1;
                for (int i = 0; i < m_Free.Count; i++)
                    if (m_Free[i].Length >= length && (best < 0 || m_Free[i].Length < m_Free[best].Length)) best = i;
                if (best < 0) return -1;
                var span = m_Free[best];
                int start = span.Start;
                if (span.Length == length) m_Free.RemoveAt(best);
                else m_Free[best] = new Span { Start = start + length, Length = span.Length - length };
                return start;
            }
            internal void Free(int start, int length)
            {
                int i = 0;
                while (i < m_Free.Count && m_Free[i].Start < start) i++;
                if (i > 0 && m_Free[i - 1].Start + m_Free[i - 1].Length == start)
                {
                    var previous = m_Free[--i];
                    start = previous.Start; length += previous.Length;
                    m_Free.RemoveAt(i);
                }
                if (i < m_Free.Count && start + length == m_Free[i].Start)
                {
                    length += m_Free[i].Length;
                    m_Free.RemoveAt(i);
                }
                m_Free.Insert(i, new Span { Start = start, Length = length });
            }
        }

        sealed class Area
        {
            internal RectInt Rect;
            internal Spans Free;
            internal Area(RectInt rect) { Rect = rect; Free = new Spans(rect.height); }
        }
        internal sealed class Row
        {
            internal RectInt Rect;
            internal Spans Free;
            internal int Count;
            internal Row Next;
            internal int AreaIndex;
        }
        internal struct Allocation
        {
            internal RectInt Rect;
            internal Row Row;
        }
        readonly List<Area> m_Areas = new List<Area>();
        readonly Row[] m_Rows = new Row[32];
        internal const int Border = 1;
        const int Padding = Border * 2;

        internal UaAtlasAllocator(int minimum, int maximum)
        {
            int width = minimum, height = minimum;
            m_Areas.Add(new Area(new RectInt(0, 0, width, height)));
            while (width < maximum || height < maximum)
            {
                if (width < maximum) { m_Areas.Add(new Area(new RectInt(width, 0, width, height))); width *= 2; }
                if (height < maximum) { m_Areas.Add(new Area(new RectInt(0, height, width, height))); height *= 2; }
            }
        }
        static int Class(int sourceHeight)
        {
            int c = 0;
            while ((1 << c) < Math.Max(1, sourceHeight)) c++;
            return c;
        }
        internal bool Allocate(int width, int height, out Allocation allocation)
        {
            allocation = default;
            if (width < 1 || height < 1 || width > int.MaxValue - Padding || height > (1 << 29)) return false;
            int paddedWidth = width + Padding, paddedHeight = height + Padding;
            int sizeClass = Class(height), rowHeight = (1 << sizeClass) + Padding;
            for (var row = m_Rows[sizeClass]; row != null; row = row.Next)
            {
                int x = row.Free.Allocate(paddedWidth);
                if (x >= 0) { row.Count++; allocation = Make(row, x, paddedWidth, paddedHeight); return true; }
            }
            for (int i = 0; i < m_Areas.Count; i++)
            {
                var area = m_Areas[i];
                if (area.Rect.width < paddedWidth || area.Rect.height < rowHeight) continue;
                int y = area.Free.Allocate(rowHeight);
                if (y < 0) continue;
                var row = new Row { Rect = new RectInt(area.Rect.x, area.Rect.y + y, area.Rect.width, rowHeight),
                    AreaIndex = i, Free = new Spans(area.Rect.width), Next = m_Rows[sizeClass], Count = 1 };
                m_Rows[sizeClass] = row;
                allocation = Make(row, row.Free.Allocate(paddedWidth), paddedWidth, paddedHeight);
                return true;
            }
            return false;
        }
        static Allocation Make(Row row, int x, int width, int height) => new Allocation
            { Row = row, Rect = new RectInt(row.Rect.x + x, row.Rect.y, width, height) };
        internal void Free(Allocation allocation)
        {
            var row = allocation.Row;
            if (row == null) return;
            row.Free.Free(allocation.Rect.x - row.Rect.x, allocation.Rect.width);
            if (--row.Count != 0) return;
            var area = m_Areas[row.AreaIndex];
            area.Free.Free(row.Rect.y - area.Rect.y, row.Rect.height);
            int sizeClass = Class(row.Rect.height - Padding);
            if (m_Rows[sizeClass] == row) m_Rows[sizeClass] = row.Next;
            else
            {
                var previous = m_Rows[sizeClass];
                while (previous.Next != row) previous = previous.Next;
                previous.Next = row.Next;
            }
        }
    }
}
