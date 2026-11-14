using System;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    public class SceneValueEqualityTests
    {
        static Scene Handle(int value) => new Scene(new SceneHandle(value));
        [Fact]
        public void GenericCollectionsPreserveHandleIdentityWithoutAnExtraInterface()
        {
            Assert.False(typeof(IEquatable<Scene>).IsAssignableFrom(typeof(Scene)));
            var map = new Dictionary<Scene, int>(); var set = new HashSet<Scene>();
            for (int i = 0; i < 257; i++)
            {
                int handle = i * 7919 - 1024;
                map.Add(Handle(handle), i); Assert.True(set.Add(Handle(handle)));
                Assert.Equal(i, map[Handle(handle)]); Assert.False(set.Add(Handle(handle)));
                Assert.True(EqualityComparer<Scene>.Default.Equals(Handle(handle), Handle(handle)));
                Assert.Equal(handle, Handle(handle).GetHashCode());
            }
            Assert.Equal(257, map.Count); Assert.Equal(257, set.Count);
            var first = Handle(-1024); var different = Handle(7919 - 1024);
            Assert.False(first.Equals((object)different)); Assert.True(first != different);
            Assert.True(map.Remove(first)); Assert.False(map.ContainsKey(first));
            Assert.True(map.ContainsKey(different)); Assert.False(first.Equals((object)null));
            Assert.False(first.Equals((object)(-1024)));
        }
        [Fact]
        public void BoxedAndNullableSceneCopiesKeepTheSameEqualityRule()
        {
            var first = Handle(int.MinValue); var second = Handle(int.MaxValue);
            Scene? optional = first;
            Assert.True(first.Equals((object)optional)); Assert.True(object.Equals(first, optional));
            Assert.False(first.Equals((object)second)); Assert.True(first == (Scene)optional);
            Assert.Equal(2, new HashSet<Scene> { first, (Scene)optional, second }.Count);
            Assert.Equal(int.MinValue, EqualityComparer<Scene>.Default.GetHashCode(first));
        }
    }
}
