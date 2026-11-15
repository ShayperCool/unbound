using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Unbound.Runtime;

// The Editor's own tuple classes (UnityEditor.Tuple): editor code compiled inside the UnityEditor
// namespace binds to these instead of System.Tuple. They behave as System.Tuple does: structural
// equality, comparison and hashing over their items, "(a, b)" as text.
namespace UnityEditor
{
    internal partial interface IStructuralEquatable
    {
        bool Equals(object other, IEqualityComparer comparer);
        int GetHashCode(IEqualityComparer comparer);
    }

    internal partial interface IStructuralComparable
    {
        int CompareTo(object other, IComparer comparer);
    }

    internal partial interface ITuple
    {
        int Length { get; }
        object this[int index] { get; }
    }

    internal partial interface ITupleInternal : ITuple
    {
        string ToString(StringBuilder sb);
        int GetHashCode(IEqualityComparer comparer);
    }

    internal static partial class Tuple
    {
        public static Tuple<T1> Create<T1>(T1 item1) => new Tuple<T1>(item1);
        public static Tuple<T1, T2> Create<T1, T2>(T1 item1, T2 item2) => new Tuple<T1, T2>(item1, item2);
        public static Tuple<T1, T2, T3> Create<T1, T2, T3>(T1 item1, T2 item2, T3 item3) => new Tuple<T1, T2, T3>(item1, item2, item3);
        public static Tuple<T1, T2, T3, T4> Create<T1, T2, T3, T4>(T1 item1, T2 item2, T3 item3, T4 item4) => new Tuple<T1, T2, T3, T4>(item1, item2, item3, item4);
        public static Tuple<T1, T2, T3, T4, T5> Create<T1, T2, T3, T4, T5>(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5) => new Tuple<T1, T2, T3, T4, T5>(item1, item2, item3, item4, item5);
        public static Tuple<T1, T2, T3, T4, T5, T6> Create<T1, T2, T3, T4, T5, T6>(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5, T6 item6) => new Tuple<T1, T2, T3, T4, T5, T6>(item1, item2, item3, item4, item5, item6);
        public static Tuple<T1, T2, T3, T4, T5, T6, T7> Create<T1, T2, T3, T4, T5, T6, T7>(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5, T6 item6, T7 item7) => new Tuple<T1, T2, T3, T4, T5, T6, T7>(item1, item2, item3, item4, item5, item6, item7);

        public static Tuple<T1, T2, T3, T4, T5, T6, T7, Tuple<T8>> Create<T1, T2, T3, T4, T5, T6, T7, T8>(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5, T6 item6, T7 item7, T8 item8)
            => new Tuple<T1, T2, T3, T4, T5, T6, T7, Tuple<T8>>(item1, item2, item3, item4, item5, item6, item7, new Tuple<T8>(item8));

        // Balanced tuple hash fold, confirmed by editortuples integer and comparer controls.
        internal static int CombineHashCodes(int h1, int h2) => unchecked(((h1 << 5) + h1) ^ h2);
        internal static int CombineHashCodes(int h1, int h2, int h3)
            => Combine(stackalloc int[] { h1, h2, h3 });
        internal static int CombineHashCodes(int h1, int h2, int h3, int h4)
            => Combine(stackalloc int[] { h1, h2, h3, h4 });
        internal static int CombineHashCodes(int h1, int h2, int h3, int h4, int h5)
            => Combine(stackalloc int[] { h1, h2, h3, h4, h5 });
        internal static int CombineHashCodes(int h1, int h2, int h3, int h4, int h5, int h6)
            => Combine(stackalloc int[] { h1, h2, h3, h4, h5, h6 });
        internal static int CombineHashCodes(int h1, int h2, int h3, int h4, int h5, int h6, int h7)
            => Combine(stackalloc int[] { h1, h2, h3, h4, h5, h6, h7 });
        internal static int CombineHashCodes(int h1, int h2, int h3, int h4, int h5, int h6, int h7, int h8)
            => Combine(stackalloc int[] { h1, h2, h3, h4, h5, h6, h7, h8 });

        internal static int Combine(ReadOnlySpan<int> hashes)
        {
            if (hashes.Length == 1) return hashes[0];
            int split = 1;
            while (split * 2 < hashes.Length) split *= 2;
            return CombineHashCodes(Combine(hashes[..split]), Combine(hashes[split..]));
        }

    }

    [Serializable]
    internal partial class Tuple<T1> : IStructuralEquatable, IStructuralComparable, IComparable, ITupleInternal, ITuple
    {
        readonly T1 m_Item1;

        public Tuple(T1 item1)
        {
            m_Item1 = item1;
        }

        public T1 Item1 => m_Item1;

        public override bool Equals(object obj) => ((IStructuralEquatable)this).Equals(obj, EqualityComparer<object>.Default);

        bool IStructuralEquatable.Equals(object other, IEqualityComparer comparer)
        {
            return other is Tuple<T1> tuple
                && comparer.Equals(m_Item1, tuple.m_Item1);
        }

        int IComparable.CompareTo(object obj) => ((IStructuralComparable)this).CompareTo(obj, Comparer<object>.Default);

        int IStructuralComparable.CompareTo(object other, IComparer comparer)
        {
            if (other == null) return 1;
            if (!(other is Tuple<T1> tuple)) throw new ArgumentException(Compat.EditorTupleResourceErrors ? "ArgumentException_TupleIncorrectType" : "Argument must be of type " + GetType() + ".", nameof(other));
            int c;
            return comparer.Compare(m_Item1, tuple.m_Item1);
        }

        public override int GetHashCode() => ((IStructuralEquatable)this).GetHashCode(EqualityComparer<object>.Default);

        int IStructuralEquatable.GetHashCode(IEqualityComparer comparer)
            => Tuple.Combine(stackalloc int[] { comparer.GetHashCode(m_Item1) });

        int ITupleInternal.GetHashCode(IEqualityComparer comparer) => ((IStructuralEquatable)this).GetHashCode(comparer);

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append('(');
            return ((ITupleInternal)this).ToString(sb);
        }

        string ITupleInternal.ToString(StringBuilder sb)
        {
            sb.Append(m_Item1);
            sb.Append(')');
            return sb.ToString();
        }

        int ITuple.Length => 1;

        object ITuple.this[int index]
        {
            get
            {
                switch (index)
                {
                    case 0: return m_Item1;
                    default: throw new IndexOutOfRangeException();
                }
            }
        }
    }

    [Serializable]
    internal partial class Tuple<T1, T2> : IStructuralEquatable, IStructuralComparable, IComparable, ITupleInternal, ITuple
    {
        readonly T1 m_Item1;
        readonly T2 m_Item2;

        public Tuple(T1 item1, T2 item2)
        {
            m_Item1 = item1;
            m_Item2 = item2;
        }

        public T1 Item1 => m_Item1;
        public T2 Item2 => m_Item2;

        public override bool Equals(object obj) => ((IStructuralEquatable)this).Equals(obj, EqualityComparer<object>.Default);

        bool IStructuralEquatable.Equals(object other, IEqualityComparer comparer)
        {
            return other is Tuple<T1, T2> tuple
                && comparer.Equals(m_Item1, tuple.m_Item1)
                && comparer.Equals(m_Item2, tuple.m_Item2);
        }

        int IComparable.CompareTo(object obj) => ((IStructuralComparable)this).CompareTo(obj, Comparer<object>.Default);

        int IStructuralComparable.CompareTo(object other, IComparer comparer)
        {
            if (other == null) return 1;
            if (!(other is Tuple<T1, T2> tuple)) throw new ArgumentException(Compat.EditorTupleResourceErrors ? "ArgumentException_TupleIncorrectType" : "Argument must be of type " + GetType() + ".", nameof(other));
            int c;
            if ((c = comparer.Compare(m_Item1, tuple.m_Item1)) != 0) return c;
            return comparer.Compare(m_Item2, tuple.m_Item2);
        }

        public override int GetHashCode() => ((IStructuralEquatable)this).GetHashCode(EqualityComparer<object>.Default);

        int IStructuralEquatable.GetHashCode(IEqualityComparer comparer)
            => Tuple.Combine(stackalloc int[] { comparer.GetHashCode(m_Item1), comparer.GetHashCode(m_Item2) });

        int ITupleInternal.GetHashCode(IEqualityComparer comparer) => ((IStructuralEquatable)this).GetHashCode(comparer);

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append('(');
            return ((ITupleInternal)this).ToString(sb);
        }

        string ITupleInternal.ToString(StringBuilder sb)
        {
            sb.Append(m_Item1);
            sb.Append(", ");
            sb.Append(m_Item2);
            sb.Append(')');
            return sb.ToString();
        }

        int ITuple.Length => 2;

        object ITuple.this[int index]
        {
            get
            {
                switch (index)
                {
                    case 0: return m_Item1;
                    case 1: return m_Item2;
                    default: throw new IndexOutOfRangeException();
                }
            }
        }
    }

    [Serializable]
    internal partial class Tuple<T1, T2, T3> : IStructuralEquatable, IStructuralComparable, IComparable, ITupleInternal, ITuple
    {
        readonly T1 m_Item1;
        readonly T2 m_Item2;
        readonly T3 m_Item3;

        public Tuple(T1 item1, T2 item2, T3 item3)
        {
            m_Item1 = item1;
            m_Item2 = item2;
            m_Item3 = item3;
        }

        public T1 Item1 => m_Item1;
        public T2 Item2 => m_Item2;
        public T3 Item3 => m_Item3;

        public override bool Equals(object obj) => ((IStructuralEquatable)this).Equals(obj, EqualityComparer<object>.Default);

        bool IStructuralEquatable.Equals(object other, IEqualityComparer comparer)
        {
            return other is Tuple<T1, T2, T3> tuple
                && comparer.Equals(m_Item1, tuple.m_Item1)
                && comparer.Equals(m_Item2, tuple.m_Item2)
                && comparer.Equals(m_Item3, tuple.m_Item3);
        }

        int IComparable.CompareTo(object obj) => ((IStructuralComparable)this).CompareTo(obj, Comparer<object>.Default);

        int IStructuralComparable.CompareTo(object other, IComparer comparer)
        {
            if (other == null) return 1;
            if (!(other is Tuple<T1, T2, T3> tuple)) throw new ArgumentException(Compat.EditorTupleResourceErrors ? "ArgumentException_TupleIncorrectType" : "Argument must be of type " + GetType() + ".", nameof(other));
            int c;
            if ((c = comparer.Compare(m_Item1, tuple.m_Item1)) != 0) return c;
            if ((c = comparer.Compare(m_Item2, tuple.m_Item2)) != 0) return c;
            return comparer.Compare(m_Item3, tuple.m_Item3);
        }

        public override int GetHashCode() => ((IStructuralEquatable)this).GetHashCode(EqualityComparer<object>.Default);

        int IStructuralEquatable.GetHashCode(IEqualityComparer comparer)
            => Tuple.Combine(stackalloc int[] { comparer.GetHashCode(m_Item1), comparer.GetHashCode(m_Item2), comparer.GetHashCode(m_Item3) });

        int ITupleInternal.GetHashCode(IEqualityComparer comparer) => ((IStructuralEquatable)this).GetHashCode(comparer);

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append('(');
            return ((ITupleInternal)this).ToString(sb);
        }

        string ITupleInternal.ToString(StringBuilder sb)
        {
            sb.Append(m_Item1);
            sb.Append(", ");
            sb.Append(m_Item2);
            sb.Append(", ");
            sb.Append(m_Item3);
            sb.Append(')');
            return sb.ToString();
        }

        int ITuple.Length => 3;

        object ITuple.this[int index]
        {
            get
            {
                switch (index)
                {
                    case 0: return m_Item1;
                    case 1: return m_Item2;
                    case 2: return m_Item3;
                    default: throw new IndexOutOfRangeException();
                }
            }
        }
    }

    [Serializable]
    internal partial class Tuple<T1, T2, T3, T4> : IStructuralEquatable, IStructuralComparable, IComparable, ITupleInternal, ITuple
    {
        readonly T1 m_Item1;
        readonly T2 m_Item2;
        readonly T3 m_Item3;
        readonly T4 m_Item4;

        public Tuple(T1 item1, T2 item2, T3 item3, T4 item4)
        {
            m_Item1 = item1;
            m_Item2 = item2;
            m_Item3 = item3;
            m_Item4 = item4;
        }

        public T1 Item1 => m_Item1;
        public T2 Item2 => m_Item2;
        public T3 Item3 => m_Item3;
        public T4 Item4 => m_Item4;

        public override bool Equals(object obj) => ((IStructuralEquatable)this).Equals(obj, EqualityComparer<object>.Default);

        bool IStructuralEquatable.Equals(object other, IEqualityComparer comparer)
        {
            return other is Tuple<T1, T2, T3, T4> tuple
                && comparer.Equals(m_Item1, tuple.m_Item1)
                && comparer.Equals(m_Item2, tuple.m_Item2)
                && comparer.Equals(m_Item3, tuple.m_Item3)
                && comparer.Equals(m_Item4, tuple.m_Item4);
        }

        int IComparable.CompareTo(object obj) => ((IStructuralComparable)this).CompareTo(obj, Comparer<object>.Default);

        int IStructuralComparable.CompareTo(object other, IComparer comparer)
        {
            if (other == null) return 1;
            if (!(other is Tuple<T1, T2, T3, T4> tuple)) throw new ArgumentException(Compat.EditorTupleResourceErrors ? "ArgumentException_TupleIncorrectType" : "Argument must be of type " + GetType() + ".", nameof(other));
            int c;
            if ((c = comparer.Compare(m_Item1, tuple.m_Item1)) != 0) return c;
            if ((c = comparer.Compare(m_Item2, tuple.m_Item2)) != 0) return c;
            if ((c = comparer.Compare(m_Item3, tuple.m_Item3)) != 0) return c;
            return comparer.Compare(m_Item4, tuple.m_Item4);
        }

        public override int GetHashCode() => ((IStructuralEquatable)this).GetHashCode(EqualityComparer<object>.Default);

        int IStructuralEquatable.GetHashCode(IEqualityComparer comparer)
            => Tuple.Combine(stackalloc int[] { comparer.GetHashCode(m_Item1), comparer.GetHashCode(m_Item2), comparer.GetHashCode(m_Item3), comparer.GetHashCode(m_Item4) });

        int ITupleInternal.GetHashCode(IEqualityComparer comparer) => ((IStructuralEquatable)this).GetHashCode(comparer);

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append('(');
            return ((ITupleInternal)this).ToString(sb);
        }

        string ITupleInternal.ToString(StringBuilder sb)
        {
            sb.Append(m_Item1);
            sb.Append(", ");
            sb.Append(m_Item2);
            sb.Append(", ");
            sb.Append(m_Item3);
            sb.Append(", ");
            sb.Append(m_Item4);
            sb.Append(')');
            return sb.ToString();
        }

        int ITuple.Length => 4;

        object ITuple.this[int index]
        {
            get
            {
                switch (index)
                {
                    case 0: return m_Item1;
                    case 1: return m_Item2;
                    case 2: return m_Item3;
                    case 3: return m_Item4;
                    default: throw new IndexOutOfRangeException();
                }
            }
        }
    }

    [Serializable]
    internal partial class Tuple<T1, T2, T3, T4, T5> : IStructuralEquatable, IStructuralComparable, IComparable, ITupleInternal, ITuple
    {
        readonly T1 m_Item1;
        readonly T2 m_Item2;
        readonly T3 m_Item3;
        readonly T4 m_Item4;
        readonly T5 m_Item5;

        public Tuple(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5)
        {
            m_Item1 = item1;
            m_Item2 = item2;
            m_Item3 = item3;
            m_Item4 = item4;
            m_Item5 = item5;
        }

        public T1 Item1 => m_Item1;
        public T2 Item2 => m_Item2;
        public T3 Item3 => m_Item3;
        public T4 Item4 => m_Item4;
        public T5 Item5 => m_Item5;

        public override bool Equals(object obj) => ((IStructuralEquatable)this).Equals(obj, EqualityComparer<object>.Default);

        bool IStructuralEquatable.Equals(object other, IEqualityComparer comparer)
        {
            return other is Tuple<T1, T2, T3, T4, T5> tuple
                && comparer.Equals(m_Item1, tuple.m_Item1)
                && comparer.Equals(m_Item2, tuple.m_Item2)
                && comparer.Equals(m_Item3, tuple.m_Item3)
                && comparer.Equals(m_Item4, tuple.m_Item4)
                && comparer.Equals(m_Item5, tuple.m_Item5);
        }

        int IComparable.CompareTo(object obj) => ((IStructuralComparable)this).CompareTo(obj, Comparer<object>.Default);

        int IStructuralComparable.CompareTo(object other, IComparer comparer)
        {
            if (other == null) return 1;
            if (!(other is Tuple<T1, T2, T3, T4, T5> tuple)) throw new ArgumentException(Compat.EditorTupleResourceErrors ? "ArgumentException_TupleIncorrectType" : "Argument must be of type " + GetType() + ".", nameof(other));
            int c;
            if ((c = comparer.Compare(m_Item1, tuple.m_Item1)) != 0) return c;
            if ((c = comparer.Compare(m_Item2, tuple.m_Item2)) != 0) return c;
            if ((c = comparer.Compare(m_Item3, tuple.m_Item3)) != 0) return c;
            if ((c = comparer.Compare(m_Item4, tuple.m_Item4)) != 0) return c;
            return comparer.Compare(m_Item5, tuple.m_Item5);
        }

        public override int GetHashCode() => ((IStructuralEquatable)this).GetHashCode(EqualityComparer<object>.Default);

        int IStructuralEquatable.GetHashCode(IEqualityComparer comparer)
            => Tuple.Combine(stackalloc int[] { comparer.GetHashCode(m_Item1), comparer.GetHashCode(m_Item2), comparer.GetHashCode(m_Item3), comparer.GetHashCode(m_Item4), comparer.GetHashCode(m_Item5) });

        int ITupleInternal.GetHashCode(IEqualityComparer comparer) => ((IStructuralEquatable)this).GetHashCode(comparer);

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append('(');
            return ((ITupleInternal)this).ToString(sb);
        }

        string ITupleInternal.ToString(StringBuilder sb)
        {
            sb.Append(m_Item1);
            sb.Append(", ");
            sb.Append(m_Item2);
            sb.Append(", ");
            sb.Append(m_Item3);
            sb.Append(", ");
            sb.Append(m_Item4);
            sb.Append(", ");
            sb.Append(m_Item5);
            sb.Append(')');
            return sb.ToString();
        }

        int ITuple.Length => 5;

        object ITuple.this[int index]
        {
            get
            {
                switch (index)
                {
                    case 0: return m_Item1;
                    case 1: return m_Item2;
                    case 2: return m_Item3;
                    case 3: return m_Item4;
                    case 4: return m_Item5;
                    default: throw new IndexOutOfRangeException();
                }
            }
        }
    }

    [Serializable]
    internal partial class Tuple<T1, T2, T3, T4, T5, T6> : IStructuralEquatable, IStructuralComparable, IComparable, ITupleInternal, ITuple
    {
        readonly T1 m_Item1;
        readonly T2 m_Item2;
        readonly T3 m_Item3;
        readonly T4 m_Item4;
        readonly T5 m_Item5;
        readonly T6 m_Item6;

        public Tuple(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5, T6 item6)
        {
            m_Item1 = item1;
            m_Item2 = item2;
            m_Item3 = item3;
            m_Item4 = item4;
            m_Item5 = item5;
            m_Item6 = item6;
        }

        public T1 Item1 => m_Item1;
        public T2 Item2 => m_Item2;
        public T3 Item3 => m_Item3;
        public T4 Item4 => m_Item4;
        public T5 Item5 => m_Item5;
        public T6 Item6 => m_Item6;

        public override bool Equals(object obj) => ((IStructuralEquatable)this).Equals(obj, EqualityComparer<object>.Default);

        bool IStructuralEquatable.Equals(object other, IEqualityComparer comparer)
        {
            return other is Tuple<T1, T2, T3, T4, T5, T6> tuple
                && comparer.Equals(m_Item1, tuple.m_Item1)
                && comparer.Equals(m_Item2, tuple.m_Item2)
                && comparer.Equals(m_Item3, tuple.m_Item3)
                && comparer.Equals(m_Item4, tuple.m_Item4)
                && comparer.Equals(m_Item5, tuple.m_Item5)
                && comparer.Equals(m_Item6, tuple.m_Item6);
        }

        int IComparable.CompareTo(object obj) => ((IStructuralComparable)this).CompareTo(obj, Comparer<object>.Default);

        int IStructuralComparable.CompareTo(object other, IComparer comparer)
        {
            if (other == null) return 1;
            if (!(other is Tuple<T1, T2, T3, T4, T5, T6> tuple)) throw new ArgumentException(Compat.EditorTupleResourceErrors ? "ArgumentException_TupleIncorrectType" : "Argument must be of type " + GetType() + ".", nameof(other));
            int c;
            if ((c = comparer.Compare(m_Item1, tuple.m_Item1)) != 0) return c;
            if ((c = comparer.Compare(m_Item2, tuple.m_Item2)) != 0) return c;
            if ((c = comparer.Compare(m_Item3, tuple.m_Item3)) != 0) return c;
            if ((c = comparer.Compare(m_Item4, tuple.m_Item4)) != 0) return c;
            if ((c = comparer.Compare(m_Item5, tuple.m_Item5)) != 0) return c;
            return comparer.Compare(m_Item6, tuple.m_Item6);
        }

        public override int GetHashCode() => ((IStructuralEquatable)this).GetHashCode(EqualityComparer<object>.Default);

        int IStructuralEquatable.GetHashCode(IEqualityComparer comparer)
            => Tuple.Combine(stackalloc int[] { comparer.GetHashCode(m_Item1), comparer.GetHashCode(m_Item2), comparer.GetHashCode(m_Item3), comparer.GetHashCode(m_Item4), comparer.GetHashCode(m_Item5), comparer.GetHashCode(m_Item6) });

        int ITupleInternal.GetHashCode(IEqualityComparer comparer) => ((IStructuralEquatable)this).GetHashCode(comparer);

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append('(');
            return ((ITupleInternal)this).ToString(sb);
        }

        string ITupleInternal.ToString(StringBuilder sb)
        {
            sb.Append(m_Item1);
            sb.Append(", ");
            sb.Append(m_Item2);
            sb.Append(", ");
            sb.Append(m_Item3);
            sb.Append(", ");
            sb.Append(m_Item4);
            sb.Append(", ");
            sb.Append(m_Item5);
            sb.Append(", ");
            sb.Append(m_Item6);
            sb.Append(')');
            return sb.ToString();
        }

        int ITuple.Length => 6;

        object ITuple.this[int index]
        {
            get
            {
                switch (index)
                {
                    case 0: return m_Item1;
                    case 1: return m_Item2;
                    case 2: return m_Item3;
                    case 3: return m_Item4;
                    case 4: return m_Item5;
                    case 5: return m_Item6;
                    default: throw new IndexOutOfRangeException();
                }
            }
        }
    }

    [Serializable]
    internal partial class Tuple<T1, T2, T3, T4, T5, T6, T7> : IStructuralEquatable, IStructuralComparable, IComparable, ITupleInternal, ITuple
    {
        readonly T1 m_Item1;
        readonly T2 m_Item2;
        readonly T3 m_Item3;
        readonly T4 m_Item4;
        readonly T5 m_Item5;
        readonly T6 m_Item6;
        readonly T7 m_Item7;

        public Tuple(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5, T6 item6, T7 item7)
        {
            m_Item1 = item1;
            m_Item2 = item2;
            m_Item3 = item3;
            m_Item4 = item4;
            m_Item5 = item5;
            m_Item6 = item6;
            m_Item7 = item7;
        }

        public T1 Item1 => m_Item1;
        public T2 Item2 => m_Item2;
        public T3 Item3 => m_Item3;
        public T4 Item4 => m_Item4;
        public T5 Item5 => m_Item5;
        public T6 Item6 => m_Item6;
        public T7 Item7 => m_Item7;

        public override bool Equals(object obj) => ((IStructuralEquatable)this).Equals(obj, EqualityComparer<object>.Default);

        bool IStructuralEquatable.Equals(object other, IEqualityComparer comparer)
        {
            return other is Tuple<T1, T2, T3, T4, T5, T6, T7> tuple
                && comparer.Equals(m_Item1, tuple.m_Item1)
                && comparer.Equals(m_Item2, tuple.m_Item2)
                && comparer.Equals(m_Item3, tuple.m_Item3)
                && comparer.Equals(m_Item4, tuple.m_Item4)
                && comparer.Equals(m_Item5, tuple.m_Item5)
                && comparer.Equals(m_Item6, tuple.m_Item6)
                && comparer.Equals(m_Item7, tuple.m_Item7);
        }

        int IComparable.CompareTo(object obj) => ((IStructuralComparable)this).CompareTo(obj, Comparer<object>.Default);

        int IStructuralComparable.CompareTo(object other, IComparer comparer)
        {
            if (other == null) return 1;
            if (!(other is Tuple<T1, T2, T3, T4, T5, T6, T7> tuple)) throw new ArgumentException(Compat.EditorTupleResourceErrors ? "ArgumentException_TupleIncorrectType" : "Argument must be of type " + GetType() + ".", nameof(other));
            int c;
            if ((c = comparer.Compare(m_Item1, tuple.m_Item1)) != 0) return c;
            if ((c = comparer.Compare(m_Item2, tuple.m_Item2)) != 0) return c;
            if ((c = comparer.Compare(m_Item3, tuple.m_Item3)) != 0) return c;
            if ((c = comparer.Compare(m_Item4, tuple.m_Item4)) != 0) return c;
            if ((c = comparer.Compare(m_Item5, tuple.m_Item5)) != 0) return c;
            if ((c = comparer.Compare(m_Item6, tuple.m_Item6)) != 0) return c;
            return comparer.Compare(m_Item7, tuple.m_Item7);
        }

        public override int GetHashCode() => ((IStructuralEquatable)this).GetHashCode(EqualityComparer<object>.Default);

        int IStructuralEquatable.GetHashCode(IEqualityComparer comparer)
            => Tuple.Combine(stackalloc int[] { comparer.GetHashCode(m_Item1), comparer.GetHashCode(m_Item2), comparer.GetHashCode(m_Item3), comparer.GetHashCode(m_Item4), comparer.GetHashCode(m_Item5), comparer.GetHashCode(m_Item6), comparer.GetHashCode(m_Item7) });

        int ITupleInternal.GetHashCode(IEqualityComparer comparer) => ((IStructuralEquatable)this).GetHashCode(comparer);

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append('(');
            return ((ITupleInternal)this).ToString(sb);
        }

        string ITupleInternal.ToString(StringBuilder sb)
        {
            sb.Append(m_Item1);
            sb.Append(", ");
            sb.Append(m_Item2);
            sb.Append(", ");
            sb.Append(m_Item3);
            sb.Append(", ");
            sb.Append(m_Item4);
            sb.Append(", ");
            sb.Append(m_Item5);
            sb.Append(", ");
            sb.Append(m_Item6);
            sb.Append(", ");
            sb.Append(m_Item7);
            sb.Append(')');
            return sb.ToString();
        }

        int ITuple.Length => 7;

        object ITuple.this[int index]
        {
            get
            {
                switch (index)
                {
                    case 0: return m_Item1;
                    case 1: return m_Item2;
                    case 2: return m_Item3;
                    case 3: return m_Item4;
                    case 4: return m_Item5;
                    case 5: return m_Item6;
                    case 6: return m_Item7;
                    default: throw new IndexOutOfRangeException();
                }
            }
        }
    }

    [Serializable]
    internal partial class Tuple<T1, T2, T3, T4, T5, T6, T7, TRest> : IStructuralEquatable, IStructuralComparable, IComparable, ITupleInternal, ITuple
    {
        readonly T1 m_Item1;
        readonly T2 m_Item2;
        readonly T3 m_Item3;
        readonly T4 m_Item4;
        readonly T5 m_Item5;
        readonly T6 m_Item6;
        readonly T7 m_Item7;
        readonly TRest m_Rest;

        public Tuple(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5, T6 item6, T7 item7, TRest rest)
        {
            if (!(rest is ITupleInternal)) throw new ArgumentException(Compat.EditorTupleResourceErrors ? "ArgumentException_TupleLastArgumentNotATuple" : "The last element of an eight element tuple must be a Tuple.");
            m_Item1 = item1;
            m_Item2 = item2;
            m_Item3 = item3;
            m_Item4 = item4;
            m_Item5 = item5;
            m_Item6 = item6;
            m_Item7 = item7;
            m_Rest = rest;
        }

        public T1 Item1 => m_Item1;
        public T2 Item2 => m_Item2;
        public T3 Item3 => m_Item3;
        public T4 Item4 => m_Item4;
        public T5 Item5 => m_Item5;
        public T6 Item6 => m_Item6;
        public T7 Item7 => m_Item7;
        public TRest Rest => m_Rest;

        public override bool Equals(object obj) => ((IStructuralEquatable)this).Equals(obj, EqualityComparer<object>.Default);

        bool IStructuralEquatable.Equals(object other, IEqualityComparer comparer)
        {
            return other is Tuple<T1, T2, T3, T4, T5, T6, T7, TRest> tuple
                && comparer.Equals(m_Item1, tuple.m_Item1)
                && comparer.Equals(m_Item2, tuple.m_Item2)
                && comparer.Equals(m_Item3, tuple.m_Item3)
                && comparer.Equals(m_Item4, tuple.m_Item4)
                && comparer.Equals(m_Item5, tuple.m_Item5)
                && comparer.Equals(m_Item6, tuple.m_Item6)
                && comparer.Equals(m_Item7, tuple.m_Item7)
                && comparer.Equals(m_Rest, tuple.m_Rest);
        }

        int IComparable.CompareTo(object obj) => ((IStructuralComparable)this).CompareTo(obj, Comparer<object>.Default);

        int IStructuralComparable.CompareTo(object other, IComparer comparer)
        {
            if (other == null) return 1;
            if (!(other is Tuple<T1, T2, T3, T4, T5, T6, T7, TRest> tuple)) throw new ArgumentException(Compat.EditorTupleResourceErrors ? "ArgumentException_TupleIncorrectType" : "Argument must be of type " + GetType() + ".", nameof(other));
            int c;
            if ((c = comparer.Compare(m_Item1, tuple.m_Item1)) != 0) return c;
            if ((c = comparer.Compare(m_Item2, tuple.m_Item2)) != 0) return c;
            if ((c = comparer.Compare(m_Item3, tuple.m_Item3)) != 0) return c;
            if ((c = comparer.Compare(m_Item4, tuple.m_Item4)) != 0) return c;
            if ((c = comparer.Compare(m_Item5, tuple.m_Item5)) != 0) return c;
            if ((c = comparer.Compare(m_Item6, tuple.m_Item6)) != 0) return c;
            if ((c = comparer.Compare(m_Item7, tuple.m_Item7)) != 0) return c;
            return comparer.Compare(m_Rest, tuple.m_Rest);
        }

        public override int GetHashCode() => ((IStructuralEquatable)this).GetHashCode(EqualityComparer<object>.Default);

        int IStructuralEquatable.GetHashCode(IEqualityComparer comparer)
        {
            var tail = (ITupleInternal)m_Rest;
            int length = tail.Length;
            if (length >= 8) return tail.GetHashCode(comparer);
            // Only the last eight items contribute, with the rest kept as one hash node.
            int prefixCount = 8 - length;
            Span<int> hashes = stackalloc int[prefixCount + 1];
            var tuple = (ITuple)this;
            for (int i = 0; i < prefixCount; i++)
                hashes[i] = comparer.GetHashCode(tuple[7 - prefixCount + i]);
            hashes[prefixCount] = tail.GetHashCode(comparer);
            return Tuple.Combine(hashes);
        }

        int ITupleInternal.GetHashCode(IEqualityComparer comparer) => ((IStructuralEquatable)this).GetHashCode(comparer);

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append('(');
            return ((ITupleInternal)this).ToString(sb);
        }

        string ITupleInternal.ToString(StringBuilder sb)
        {
            sb.Append(m_Item1);
            sb.Append(", ");
            sb.Append(m_Item2);
            sb.Append(", ");
            sb.Append(m_Item3);
            sb.Append(", ");
            sb.Append(m_Item4);
            sb.Append(", ");
            sb.Append(m_Item5);
            sb.Append(", ");
            sb.Append(m_Item6);
            sb.Append(", ");
            sb.Append(m_Item7);
            sb.Append(", ");
            return ((ITupleInternal)m_Rest).ToString(sb);
        }

        int ITuple.Length => 7 + ((ITuple)m_Rest).Length;

        object ITuple.this[int index]
        {
            get
            {
                switch (index)
                {
                    case 0: return m_Item1;
                    case 1: return m_Item2;
                    case 2: return m_Item3;
                    case 3: return m_Item4;
                    case 4: return m_Item5;
                    case 5: return m_Item6;
                    case 6: return m_Item7;
                    default: return ((ITuple)m_Rest)[index - 7];
                }
            }
        }
    }
}
