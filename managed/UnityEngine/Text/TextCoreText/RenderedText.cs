using System;

namespace UnityEngine.TextCore.Text
{
    /// <summary>The text a generator renders without building a string: a slice of a string, or a
    /// character repeated (password fields), followed by an optional suffix (ellipsis).</summary>
    internal readonly partial struct RenderedText : IEquatable<RenderedText>, IEquatable<string>
    {
        public readonly string value;
        public readonly int valueStart;
        public readonly int valueLength;
        public readonly string suffix;
        public readonly char repeat;
        public readonly int repeatCount;

        public RenderedText(string value) : this(value, 0, value?.Length ?? 0) { }

        public RenderedText(string value, string suffix) : this(value, 0, value?.Length ?? 0, suffix) { }

        public RenderedText(string value, int start, int length, string suffix = null)
        {
            if (string.IsNullOrEmpty(value))
            {
                start = 0;
                length = 0;
            }
            else
            {
                if (start < 0) start = 0;
                else if (start >= value.Length)
                {
                    start = value.Length;
                    length = 0;
                }
                length = Math.Clamp(length, 0, value.Length - start);
            }
            this.value = value;
            valueStart = start;
            valueLength = length;
            this.suffix = suffix;
            repeat = '\0';
            repeatCount = 0;
        }

        public RenderedText(char repeat, int repeatCount, string suffix = null)
        {
            value = null;
            valueStart = 0;
            valueLength = 0;
            this.suffix = suffix;
            this.repeat = repeat;
            this.repeatCount = Math.Max(0, repeatCount);
        }

        public int CharacterCount => valueLength + repeatCount + (suffix?.Length ?? 0);

        public Enumerator GetEnumerator() => new Enumerator(this);

        public string CreateString()
        {
            var chars = new char[CharacterCount];
            int i = 0;
            foreach (var c in this) chars[i++] = c;
            return new string(chars);
        }

        public bool Equals(RenderedText other) =>
            value == other.value && valueStart == other.valueStart && valueLength == other.valueLength &&
            suffix == other.suffix && repeat == other.repeat && repeatCount == other.repeatCount;

        public bool Equals(string other)
        {
            int length = other?.Length ?? 0;
            if (length != CharacterCount) return false;
            int i = 0;
            foreach (var c in this)
                if (c != other[i++]) return false;
            return true;
        }

        public override bool Equals(object obj) => (obj is string s && Equals(s)) || (obj is RenderedText r && Equals(r));

        public override int GetHashCode() => HashCode.Combine(value, valueStart, valueLength, suffix, repeat, repeatCount);

        /// <summary>The characters: the value's slice, the repeats, then the suffix.</summary>
        public partial struct Enumerator
        {
            readonly RenderedText m_Source;
            int m_Stage;
            int m_StageIndex;
            char m_Current;

            public Enumerator(in RenderedText source)
            {
                m_Source = source;
                m_Stage = 0;
                m_StageIndex = 0;
                m_Current = '\0';
            }

            public char Current => m_Current;

            public bool MoveNext()
            {
                if (m_Stage == 0)
                {
                    if (m_Source.value != null && m_StageIndex < m_Source.valueLength)
                    {
                        m_Current = m_Source.value[m_Source.valueStart + m_StageIndex++];
                        return true;
                    }
                    m_Stage = 1;
                    m_StageIndex = 0;
                }
                if (m_Stage == 1)
                {
                    if (m_StageIndex < m_Source.repeatCount)
                    {
                        m_Current = m_Source.repeat;
                        m_StageIndex++;
                        return true;
                    }
                    m_Stage = 2;
                    m_StageIndex = 0;
                }
                if (m_Stage == 2)
                {
                    if (m_Source.suffix != null && m_StageIndex < m_Source.suffix.Length)
                    {
                        m_Current = m_Source.suffix[m_StageIndex++];
                        return true;
                    }
                    m_Stage = 3;
                }
                return false;
            }

            public void Reset()
            {
                m_Stage = 0;
                m_StageIndex = 0;
                m_Current = '\0';
            }
        }
    }
}
