using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Unity.Properties;
using Xunit;

namespace UnityEngine.Tests;

public class PropertyEnumeratorStateTests
{
    sealed class Named<T> : Property<T, int>
    {
        public override string Name { get; }
        public Named(string name) { Name = name; }
        public override bool IsReadOnly => false;
        public override int GetValue(ref T container) => 0;
        public override void SetValue(ref T container, int value) { }
    }

    sealed class Source : IEnumerable<IProperty<int>>, IEnumerator<IProperty<int>>
    {
        public IProperty<int> value = new Named<int>("initial");
        public int reads, resets, disposals, moves;
        public bool throwCurrent;
        public IProperty<int> Current
        {
            get
            {
                reads++;
                if (throwCurrent) throw new InvalidOperationException();
                return value;
            }
        }
        object IEnumerator.Current => Current;
        public bool MoveNext() => ++moves < 4;
        public void Reset() { resets++; moves = 0; }
        public void Dispose() { disposals++; }
        public IEnumerator<IProperty<int>> GetEnumerator() => this;
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    static void Inject<T>(ref PropertyCollection<T>.Enumerator iterator, IProperty<T> value)
    {
        object boxed = iterator;
        var setter = typeof(PropertyCollection<T>.Enumerator).GetProperty("Current").GetSetMethod(true);
        Assert.NotNull(setter);
        Assert.True(setter.IsPrivate);
        setter.Invoke(boxed, new object[] { value });
        iterator = (PropertyCollection<T>.Enumerator)boxed;
    }

    [Fact]
    public void EnumerableReadsCurrentOncePerAdvanceIncludingFalseAndRetainsCacheWhenReadingThrows()
    {
        var source = new Source();
        var iterator = new PropertyCollection<int>((IEnumerable<IProperty<int>>)source).GetEnumerator();
        Assert.Null(iterator.Current);
        Assert.Equal(0, source.reads);
        for (int i = 0; i < 6; i++)
        {
            var next = new Named<int>("item-"+i);
            source.value = next;
            Assert.Equal(i < 3, iterator.MoveNext());
            Assert.Equal(i+1, source.reads);
            source.value = new Named<int>("unread");
            for (int j = 0; j < 5; j++) Assert.Same(next, iterator.Current);
            Assert.Same(next, ((IEnumerator)iterator).Current);
            Assert.Equal(i+1, source.reads);
        }
        var retained = iterator.Current;
        source.throwCurrent = true;
        Assert.Throws<InvalidOperationException>(() => iterator.MoveNext());
        Assert.Same(retained, iterator.Current);
        Assert.Equal(7, source.reads);
        iterator.Reset();
        Assert.Equal(1, source.resets);
        Assert.Same(retained, iterator.Current);
        iterator.Dispose();
        Assert.Equal(1, source.disposals);
        Assert.Same(retained, iterator.Current);
    }

    [Fact]
    public void CacheSetterAndCopiesAreIndependentOfListAndDefaultSourceState()
    {
        var first = new Named<int>("first"); var second = new Named<int>("second");
        var injected = new Named<int>("injected");
        var iterator = new PropertyCollection<int>(new List<IProperty<int>> { first, second }).GetEnumerator();
        Assert.Null(iterator.Current);
        Assert.True(iterator.MoveNext());
        var copy = iterator;
        Inject(ref iterator, injected);
        Assert.Same(first, copy.Current);
        Assert.Same(injected, iterator.Current);
        iterator.Reset();
        Assert.Same(injected, iterator.Current);
        Assert.True(iterator.MoveNext());
        Assert.Same(second, iterator.Current);
        Assert.False(iterator.MoveNext());
        Assert.Null(iterator.Current);
        Inject(ref iterator, injected);
        iterator.Dispose();
        Assert.Same(injected, iterator.Current);
        var empty = default(PropertyCollection<int>.Enumerator);
        Inject(ref empty, injected);
        Assert.False(empty.MoveNext());
        empty.Reset(); empty.Dispose();
        Assert.Same(injected, empty.Current);
    }

    [Fact]
    public void IndexedResetRestartsPositionAndKeepsTheCachedPropertyUntilAdvance()
    {
        var list = new List<int> { 7, 11, 13 };
        var iterator = PropertyBag.GetPropertyBag<List<int>>().GetProperties(ref list).GetEnumerator();
        Assert.Null(iterator.Current);
        Assert.True(iterator.MoveNext());
        Assert.Equal("0", iterator.Current.Name);
        Assert.True(iterator.MoveNext());
        Assert.Equal("1", iterator.Current.Name);
        var injected = new Named<List<int>>("injected");
        Inject(ref iterator, injected);
        iterator.Reset();
        Assert.Same(injected, iterator.Current);
        Assert.True(iterator.MoveNext());
        Assert.Equal("0", iterator.Current.Name);
        Assert.NotSame(injected, iterator.Current);
        while (iterator.MoveNext()) { }
        Assert.NotNull(iterator.Current);
        Inject(ref iterator, injected);
        iterator.Dispose();
        Assert.Same(injected, iterator.Current);
    }
}
