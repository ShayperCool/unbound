using System;
using System.Collections.Generic;
using System.Text;

namespace Unity.Properties
{
    /// <summary>One step of a property path: a property name, a list index or a dictionary/set key.</summary>
    public readonly partial struct PropertyPathPart : IEquatable<PropertyPathPart>
    {
        readonly PropertyPathPartKind m_Kind;
        readonly string m_Name;
        readonly int m_Index;
        readonly object m_Key;

        public PropertyPathPart(string name)
        {
            m_Kind = PropertyPathPartKind.Name;
            m_Name = name;
            m_Index = -1;
            m_Key = null;
        }

        public PropertyPathPart(int index)
        {
            m_Kind = PropertyPathPartKind.Index;
            m_Name = string.Empty;
            m_Index = index;
            m_Key = null;
        }

        public PropertyPathPart(object key)
        {
            m_Kind = PropertyPathPartKind.Key;
            m_Name = string.Empty;
            m_Index = -1;
            m_Key = key;
        }

        public PropertyPathPartKind Kind => m_Kind;
        public bool IsName => m_Kind == PropertyPathPartKind.Name;
        public bool IsIndex => m_Kind == PropertyPathPartKind.Index;
        public bool IsKey => m_Kind == PropertyPathPartKind.Key;

        public string Name => m_Kind == PropertyPathPartKind.Name ? m_Name : throw new InvalidOperationException();
        public int Index => m_Kind == PropertyPathPartKind.Index ? m_Index : throw new InvalidOperationException();
        public object Key => m_Kind == PropertyPathPartKind.Key ? m_Key : throw new InvalidOperationException();

        public override string ToString() => m_Kind switch
        {
            PropertyPathPartKind.Name => m_Name,
            PropertyPathPartKind.Index => "[" + m_Index + "]",
            PropertyPathPartKind.Key => "[\"" + m_Key?.ToString() + "\"]",
            _ => throw new ArgumentOutOfRangeException(),
        };

        public bool Equals(PropertyPathPart other) =>
            m_Kind == other.m_Kind && m_Name == other.m_Name && m_Index == other.m_Index && Equals(m_Key, other.m_Key);

        public override bool Equals(object obj) => obj is PropertyPathPart other && Equals(other);

        public override int GetHashCode() => m_Kind switch
        {
            PropertyPathPartKind.Name => ((int)m_Kind * 397) ^ (m_Name?.GetHashCode() ?? 0),
            PropertyPathPartKind.Index => ((int)m_Kind * 397) ^ m_Index,
            PropertyPathPartKind.Key => ((int)m_Kind * 397) ^ (m_Key?.GetHashCode() ?? 0),
            _ => throw new ArgumentOutOfRangeException(),
        };
    }

    /// <summary>
    /// A path from a container to a property: <c>name.other[3]["key"]</c>. Immutable; the
    /// static methods make new paths.
    /// </summary>
    public readonly partial struct PropertyPath : IEquatable<PropertyPath>
    {
        internal const int k_InlineCount = 4;

        readonly PropertyPathPart[] m_Parts;

        public PropertyPath(string path)
        {
            m_Parts = Parse(path);
        }

        internal PropertyPath(List<PropertyPathPart> parts)
        {
            m_Parts = parts == null || parts.Count == 0 ? null : parts.ToArray();
        }

        PropertyPath(PropertyPathPart[] parts)
        {
            m_Parts = parts == null || parts.Length == 0 ? null : parts;
        }

        public int Length => m_Parts?.Length ?? 0;

        public bool IsEmpty => Length == 0;

        public PropertyPathPart this[int index]
        {
            get
            {
                if (m_Parts == null || (uint)index >= (uint)m_Parts.Length) throw new IndexOutOfRangeException();
                return m_Parts[index];
            }
        }

        public static PropertyPath FromPart(in PropertyPathPart part) => new PropertyPath(new[] { part });

        public static PropertyPath FromName(string name) => FromPart(new PropertyPathPart(name));

        public static PropertyPath FromIndex(int index) => FromPart(new PropertyPathPart(index));

        public static PropertyPath FromKey(object key) => FromPart(new PropertyPathPart(key));

        public static PropertyPath Combine(in PropertyPath path, in PropertyPath pathToAppend)
        {
            if (path.IsEmpty) return pathToAppend;
            if (pathToAppend.IsEmpty) return path;
            var parts = new PropertyPathPart[path.Length + pathToAppend.Length];
            path.m_Parts.CopyTo(parts, 0);
            pathToAppend.m_Parts.CopyTo(parts, path.Length);
            return new PropertyPath(parts);
        }

        public static PropertyPath Combine(in PropertyPath path, string pathToAppend)
        {
            if (string.IsNullOrEmpty(pathToAppend)) return path;
            return Combine(path, new PropertyPath(pathToAppend));
        }

        public static PropertyPath AppendPart(in PropertyPath path, in PropertyPathPart part)
        {
            var parts = new PropertyPathPart[path.Length + 1];
            path.m_Parts?.CopyTo(parts, 0);
            parts[^1] = part;
            return new PropertyPath(parts);
        }

        public static PropertyPath AppendName(in PropertyPath path, string name) => AppendPart(path, new PropertyPathPart(name));

        public static PropertyPath AppendIndex(in PropertyPath path, int index) => AppendPart(path, new PropertyPathPart(index));

        public static PropertyPath AppendKey(in PropertyPath path, object key) => AppendPart(path, new PropertyPathPart(key));

        /// <summary>Appends a property as a path names it: list elements by index, set and
        /// dictionary elements by key, others by name.</summary>
        public static PropertyPath AppendProperty(in PropertyPath path, IProperty property) => property switch
        {
            IListElementProperty element => AppendPart(path, new PropertyPathPart(element.Index)),
            ISetElementProperty element => AppendPart(path, new PropertyPathPart(element.ObjectKey)),
            IDictionaryElementProperty element => AppendPart(path, new PropertyPathPart(element.ObjectKey)),
            _ => AppendPart(path, new PropertyPathPart(property.Name)),
        };

        public static PropertyPath Pop(in PropertyPath path) => SubPath(path, 0, path.Length - 1);

        public static PropertyPath SubPath(in PropertyPath path, int startIndex) => SubPath(path, startIndex, path.Length - startIndex);

        public static PropertyPath SubPath(in PropertyPath path, int startIndex, int length)
        {
            int total = path.Length;
            if (startIndex < 0 || startIndex > total) throw new ArgumentOutOfRangeException(nameof(startIndex));
            if (length < 0 || startIndex > total - length) throw new ArgumentOutOfRangeException(nameof(length));
            if (length == 0) return default;
            if (startIndex == 0 && length == total) return path;
            var parts = new PropertyPathPart[length];
            Array.Copy(path.m_Parts, startIndex, parts, 0, length);
            return new PropertyPath(parts);
        }

        public override string ToString()
        {
            if (IsEmpty) return string.Empty;
            if (Length == 1 && m_Parts[0].IsName) return m_Parts[0].Name;
            var builder = new StringBuilder(32);
            foreach (var part in m_Parts)
            {
                if (part.IsName && builder.Length > 0) builder.Append('.');
                builder.Append(part.ToString());
            }
            return builder.ToString();
        }

        public bool Equals(PropertyPath other)
        {
            if (Length != other.Length) return false;
            for (int i = 0; i < Length; i++)
                if (!m_Parts[i].Equals(other.m_Parts[i])) return false;
            return true;
        }

        public override bool Equals(object obj) => obj is PropertyPath other && Equals(other);

        public override int GetHashCode()
        {
            int hash = 19;
            for (int i = 0; i < Length; i++) hash = hash * 31 + m_Parts[i].GetHashCode();
            return hash;
        }

        public static bool operator ==(PropertyPath lhs, PropertyPath rhs) => lhs.Equals(rhs);

        public static bool operator !=(PropertyPath lhs, PropertyPath rhs) => !lhs.Equals(rhs);

        /// <summary>Reads `a.b[2]["key"]`: names up to a dot or bracket, numeric indices, quoted
        /// keys; leading spaces of a name are skipped.</summary>
        static PropertyPathPart[] Parse(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            var parts = new List<PropertyPathPart>();
            int i = 0, n = path.Length;
            ArgumentException Invalid() => new ArgumentException($"PropertyPath: Invalid '{path[i]}' character encountered at index '{i}'.");

            // After a part: the end, a dot (then a name or bracket), or a bracket.
            bool AfterPart()
            {
                if (i == n) return false;
                if (path[i] == '.')
                {
                    i++;
                    return false;
                }
                if (path[i] == '[') return true;
                throw Invalid();
            }

            bool bracket = false;
            while (i < n)
            {
                if (!bracket)
                {
                    while (i < n && path[i] == ' ') i++;
                    if (i == n) break;
                    if (path[i] == '.' || path[i] == '"') throw Invalid();
                    if (path[i] == '[')
                    {
                        bracket = true;
                        continue;
                    }
                    int start = i;
                    while (i < n && path[i] != '.' && path[i] != '[') i++;
                    parts.Add(new PropertyPathPart(path.Substring(start, i - start)));
                    bracket = AfterPart();
                    continue;
                }
                // At '['.
                if (i + 1 < n && path[i + 1] == '"')
                {
                    i += 2;
                    int start = i;
                    while (i < n && path[i] != '"') i++;
                    // A key left open reads past the end, as Unity's parser does.
                    if (i >= n) throw new IndexOutOfRangeException("Index was outside the bounds of the array.");
                    if (!(i + 1 < n && path[i + 1] == ']')) throw new ArgumentException("Invalid PropertyPath: No matching end quote for key.");
                    parts.Add(new PropertyPathPart((object)path.Substring(start, i - start)));
                    i += 2;
                    bracket = AfterPart();
                }
                else
                {
                    i++;
                    int start = i;
                    while (i < n && path[i] != ']') i++;
                    if (i >= n)
                    {
                        i = n - 1;
                        throw Invalid();
                    }
                    if (!int.TryParse(path.Substring(start, i - start), out var index)) throw new ArgumentException("Indices in PropertyPath must be a numeric value.");
                    if (index < 0) throw new ArgumentException("Invalid PropertyPath: Negative indices are not supported.");
                    parts.Add(new PropertyPathPart(index));
                    i++;
                    bracket = AfterPart();
                }
            }
            return parts.ToArray();
        }
    }
}
