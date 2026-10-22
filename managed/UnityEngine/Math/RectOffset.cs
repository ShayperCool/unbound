using System;

namespace UnityEngine
{
    /// <summary>
    /// Offsets for rectangles, borders, etc. Unity backs this class with a native object; in Unbound
    /// it holds its four values directly.
    /// </summary>
    [Serializable]
    public partial class RectOffset : IFormattable
    {
        private int m_Left;
        private int m_Right;
        private int m_Top;
        private int m_Bottom;

        public RectOffset() { }

        public RectOffset(int left, int right, int top, int bottom)
        {
            m_Left = left;
            m_Right = right;
            m_Top = top;
            m_Bottom = bottom;
        }

        public int left
        {
            get => m_Left;
            set => m_Left = value;
        }

        public int right
        {
            get => m_Right;
            set => m_Right = value;
        }

        public int top
        {
            get => m_Top;
            set => m_Top = value;
        }

        public int bottom
        {
            get => m_Bottom;
            set => m_Bottom = value;
        }

        public int horizontal => m_Left + m_Right;

        public int vertical => m_Top + m_Bottom;

        /// <summary>Grows <paramref name="rect"/> by the offsets.</summary>
        public Rect Add(Rect rect) =>
            new Rect(rect.x - m_Left, rect.y - m_Top, rect.width + m_Left + m_Right, rect.height + m_Top + m_Bottom);

        /// <summary>Shrinks <paramref name="rect"/> by the offsets.</summary>
        public Rect Remove(Rect rect) =>
            new Rect(rect.x + m_Left, rect.y + m_Top, rect.width - m_Left - m_Right, rect.height - m_Top - m_Bottom);

        public override string ToString() => ToString(null, null);

        public string ToString(string format) => ToString(format, null);

        public string ToString(string format, IFormatProvider formatProvider)
        {
            formatProvider = MathFormat.Provider(formatProvider);
            return "RectOffset (l:" + left.ToString(format, formatProvider) + " r:" + right.ToString(format, formatProvider) +
                   " t:" + top.ToString(format, formatProvider) + " b:" + bottom.ToString(format, formatProvider) + ")";
        }
    }
}
