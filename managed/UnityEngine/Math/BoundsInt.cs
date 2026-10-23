using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace UnityEngine
{
    /// <summary>An axis-aligned integer box, stored as a position (min corner) and a size.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public partial struct BoundsInt : IEquatable<BoundsInt>, IFormattable
    {
        private Vector3Int m_Position;
        private Vector3Int m_Size;

        public BoundsInt(int xMin, int yMin, int zMin, int sizeX, int sizeY, int sizeZ)
        {
            m_Position = new Vector3Int(xMin, yMin, zMin);
            m_Size = new Vector3Int(sizeX, sizeY, sizeZ);
        }

        public BoundsInt(Vector3Int position, Vector3Int size)
        {
            m_Position = position;
            m_Size = size;
        }

        public int x
        {
            get => m_Position.x;
            set => m_Position.x = value;
        }

        public int y
        {
            get => m_Position.y;
            set => m_Position.y = value;
        }

        public int z
        {
            get => m_Position.z;
            set => m_Position.z = value;
        }

        public Vector3 center => new Vector3(x + m_Size.x / 2f, y + m_Size.y / 2f, z + m_Size.z / 2f);

        public Vector3Int min
        {
            get => new Vector3Int(xMin, yMin, zMin);
            set
            {
                xMin = value.x;
                yMin = value.y;
                zMin = value.z;
            }
        }

        public Vector3Int max
        {
            get => new Vector3Int(xMax, yMax, zMax);
            set
            {
                xMax = value.x;
                yMax = value.y;
                zMax = value.z;
            }
        }

        public int xMin
        {
            get => Math.Min(m_Position.x, m_Position.x + m_Size.x);
            set
            {
                int oldXMax = xMax;
                m_Position.x = value;
                m_Size.x = oldXMax - m_Position.x;
            }
        }

        public int yMin
        {
            get => Math.Min(m_Position.y, m_Position.y + m_Size.y);
            set
            {
                int oldYMax = yMax;
                m_Position.y = value;
                m_Size.y = oldYMax - m_Position.y;
            }
        }

        public int zMin
        {
            get => Math.Min(m_Position.z, m_Position.z + m_Size.z);
            set
            {
                int oldZMax = zMax;
                m_Position.z = value;
                m_Size.z = oldZMax - m_Position.z;
            }
        }

        public int xMax
        {
            get => Math.Max(m_Position.x, m_Position.x + m_Size.x);
            set => m_Size.x = value - m_Position.x;
        }

        public int yMax
        {
            get => Math.Max(m_Position.y, m_Position.y + m_Size.y);
            set => m_Size.y = value - m_Position.y;
        }

        public int zMax
        {
            get => Math.Max(m_Position.z, m_Position.z + m_Size.z);
            set => m_Size.z = value - m_Position.z;
        }

        public Vector3Int position
        {
            get => m_Position;
            set => m_Position = value;
        }

        public Vector3Int size
        {
            get => m_Size;
            set => m_Size = value;
        }

        public void SetMinMax(Vector3Int minPosition, Vector3Int maxPosition)
        {
            min = minPosition;
            max = maxPosition;
        }

        /// <summary>Clamps the position and size so the box lies inside <paramref name="bounds"/>.</summary>
        public void ClampToBounds(BoundsInt bounds)
        {
            position = new Vector3Int(
                Math.Max(Math.Min(bounds.xMax, position.x), bounds.xMin),
                Math.Max(Math.Min(bounds.yMax, position.y), bounds.yMin),
                Math.Max(Math.Min(bounds.zMax, position.z), bounds.zMin));
            size = new Vector3Int(
                Math.Min(bounds.xMax - position.x, size.x),
                Math.Min(bounds.yMax - position.y, size.y),
                Math.Min(bounds.zMax - position.z, size.z));
        }

        /// <summary>True if the position is inside; min faces inclusive, max faces exclusive.</summary>
        public bool Contains(Vector3Int position) =>
            position.x >= xMin && position.y >= yMin && position.z >= zMin &&
            position.x < xMax && position.y < yMax && position.z < zMax;

        public override string ToString() => ToString(null, null);

        public string ToString(string format) => ToString(format, null);

        public string ToString(string format, IFormatProvider formatProvider)
        {
            formatProvider = MathFormat.Provider(formatProvider);
            return "Position: " + m_Position.ToString(format, formatProvider) + ", Size: " + m_Size.ToString(format, formatProvider);
        }

        public static bool operator ==(BoundsInt lhs, BoundsInt rhs) => lhs.m_Position == rhs.m_Position && lhs.m_Size == rhs.m_Size;

        public static bool operator !=(BoundsInt lhs, BoundsInt rhs) => !(lhs == rhs);

        public override bool Equals(object other) => other is BoundsInt b && Equals(b);

        public bool Equals(BoundsInt other) => m_Position.Equals(other.m_Position) && m_Size.Equals(other.m_Size);

        public override int GetHashCode() => m_Position.GetHashCode() ^ (m_Size.GetHashCode() << 2);

        public PositionEnumerator allPositionsWithin => new PositionEnumerator(min, max);

        /// <summary>Enumerates the integer positions in [min, max), x fastest, then y, then z.</summary>
        public partial struct PositionEnumerator : IEnumerator<Vector3Int>
        {
            private readonly Vector3Int _min;
            private readonly Vector3Int _max;
            private Vector3Int _current;

            public PositionEnumerator(Vector3Int min, Vector3Int max)
            {
                _min = _current = min;
                _max = max;
                Reset();
            }

            public PositionEnumerator GetEnumerator() => this;

            public bool MoveNext()
            {
                if (_current.z >= _max.z || _current.y >= _max.y)
                    return false;

                _current.x++;
                if (_current.x >= _max.x)
                {
                    _current.x = _min.x;
                    if (_current.x >= _max.x)
                        return false;
                    _current.y++;
                    if (_current.y >= _max.y)
                    {
                        _current.y = _min.y;
                        _current.z++;
                        if (_current.z >= _max.z)
                            return false;
                    }
                }
                return true;
            }

            public void Reset()
            {
                _current = _min;
                _current.x--;
            }

            public Vector3Int Current => _current;

            object IEnumerator.Current => Current;

            void IDisposable.Dispose() { }
        }
    }
}
