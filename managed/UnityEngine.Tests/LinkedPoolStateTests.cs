using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine.Pool;
using Xunit;

namespace UnityEngine.Tests
{
    public class LinkedPoolStateTests
    {
        sealed class Value
        {
            public override bool Equals(object other) => other is Value;
            public override int GetHashCode() => 1;
        }

        [Fact]
        public void ObjectPoolChecksIdentityAndRetainsItsDistinctNullSlotRules()
        {
            var first = new Value(); var second = new Value();
            var pool = new ObjectPool<Value>(() => new Value());
            pool.Release(null); Assert.Equal(0, pool.CountInactive);
            pool.Release(first); pool.Release(second); pool.Release(null);
            Assert.Same(first, pool.Get());
            Assert.Throws<InvalidOperationException>(() => pool.Release(null));
            Assert.Null(pool.Get()); Assert.Same(second, pool.Get());
            pool.Release(first); Assert.Throws<InvalidOperationException>(() => pool.Release(first));
            pool.Clear();
            pool.m_CollectionCheck = false;
            pool.Release(first); pool.Release(first); pool.m_CollectionCheck = true;
            Assert.Throws<InvalidOperationException>(() => pool.Release(first));
            Assert.Same(first, pool.Get()); Assert.Throws<InvalidOperationException>(() => pool.Release(first));
            Assert.Same(first, pool.Get()); pool.Release(first); Assert.Equal(1, pool.CountInactive);
            var uncheckedPool = new ObjectPool<Value>(() => new Value(), collectionCheck: false);
            uncheckedPool.Release(first); uncheckedPool.Release(first); uncheckedPool.m_CollectionCheck = true;
            Assert.Throws<InvalidOperationException>(() => uncheckedPool.Release(first));
            Assert.Same(first, uncheckedPool.Get()); Assert.Throws<InvalidOperationException>(() => uncheckedPool.Release(first));
            Assert.Same(first, uncheckedPool.Get());
        }

        [Fact]
        public void ConstructorPreservesSelectedArgumentIdentifiers()
        {
            Assert.Equal("createFunc", Assert.Throws<ArgumentNullException>(() => new LinkedPool<object>(null)).ParamName);
            Assert.Equal("Max size must be greater than 0", Assert.Throws<ArgumentException>(() => new LinkedPool<object>(() => new object(), maxSize: 0)).ParamName);
        }

        [Fact]
        public void CachedCountControlsCapacityWhileStorageControlsGets()
        {
            var first = new Value(); var second = new Value(); var rejected = new Value();
            var destroyed = new List<Value>();
            var pool = new LinkedPool<Value>(() => new Value(), actionOnDestroy: destroyed.Add, maxSize: 2);
            pool.Release(first); pool.Release(second);
            typeof(LinkedPool<Value>).GetProperty("CountInactive").SetValue(pool, 5);
            pool.Release(rejected);
            Assert.Same(rejected, Assert.Single(destroyed));
            Assert.Same(second, pool.Get()); Assert.Equal(4, pool.CountInactive);
            Assert.Same(first, pool.Get()); Assert.Equal(3, pool.CountInactive);
            Assert.NotNull(pool.Get()); Assert.Equal(3, pool.CountInactive);
            pool.Clear(); Assert.Equal(0, pool.CountInactive);
            pool.Release(first); Assert.Same(first, pool.Get());
        }

        [Fact]
        public void NullAndEqualValuesUseReferenceIdentityAndReverseReleaseOrder()
        {
            var pool = new LinkedPool<Value>(() => null);
            var first = new Value(); var second = new Value();
            pool.Release(first); pool.Release(second); pool.Release(null);
            Assert.Throws<InvalidOperationException>(() => pool.Release(first));
            Assert.Throws<InvalidOperationException>(() => pool.Release(null));
            Assert.Equal(3, pool.CountInactive);
            Assert.Null(pool.Get()); Assert.Same(second, pool.Get()); Assert.Same(first, pool.Get());
            Assert.Equal(0, pool.CountInactive);
            Assert.Null(pool.Get()); Assert.Equal(0, pool.CountInactive);
        }

        [Fact]
        public void CallbackFailureLeavesOnlyCompletedStorageOperationsApplied()
        {
            bool fail = false;
            var a = new Value(); var b = new Value();
            var observed = new List<int>();
            LinkedPool<Value> pool = null;
            pool = new LinkedPool<Value>(() => b,
                actionOnGet: item => { observed.Add(pool.CountInactive); if (fail) throw new InvalidOperationException(); },
                actionOnDestroy: item => { if (fail) throw new InvalidOperationException(); });
            pool.Release(a); fail = true;
            Assert.Throws<InvalidOperationException>(() => pool.Clear()); Assert.Equal(1, pool.CountInactive);
            Assert.Throws<InvalidOperationException>(() => pool.Get()); Assert.Equal(0, pool.CountInactive);
            Assert.Equal(0, Assert.Single(observed));
            fail = false; Assert.Same(b, pool.Get());
        }

        [Fact]
        public void ReentrantReleaseKeepsDuplicateChecksUntilAllCopiesLeave()
        {
            var value = new Value(); bool recurse = true;
            LinkedPool<Value> pool = null;
            pool = new LinkedPool<Value>(() => new Value(), actionOnRelease: item =>
            {
                if (recurse) { recurse = false; pool.Release(item); }
            });
            pool.Release(value); Assert.Equal(2, pool.CountInactive);
            Assert.Same(value, pool.Get());
            Assert.Throws<InvalidOperationException>(() => pool.Release(value));
            Assert.Same(value, pool.Get());
            pool.Release(value); Assert.Equal(1, pool.CountInactive);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ReusedNodesDoNotRetainValuesAndScopedReturnUsesSamePool(bool check)
        {
            var pool = new LinkedPool<object>(() => new object(), collectionCheck: check);
            var objects = new object[257];
            for (int i = 0; i < objects.Length; ++i) objects[i] = pool.Get();
            for (int round = 0; round < 4; ++round)
            {
                foreach (var item in objects) pool.Release(item);
                for (int i = objects.Length - 1; i >= 0; --i) Assert.Same(objects[i], pool.Get());
                Assert.Equal(0, pool.CountInactive);
            }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; ++i) { pool.Release(objects[0]); pool.Get(); }
            Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
            for (var node = pool.m_NextAvailableListItem; node != null; node = node.poolNext) Assert.Null(node.value);
            using (pool.Get(out var item)) Assert.NotNull(item);
            Assert.Equal(1, pool.CountInactive);
            pool.Dispose(); Assert.Equal(0, pool.CountInactive); Assert.Null(pool.m_NextAvailableListItem);
        }
    }
}
