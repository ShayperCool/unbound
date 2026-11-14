using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine.XR;
using Xunit;

namespace UnityEngine.Tests
{
    public class XRValuePayloadTests
    {
        static T Set<T>(T value, string name, object field) where T : struct
        {
            object boxed = value;
            var info = typeof(T).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            info.SetValue(boxed, info.FieldType.IsEnum ? Enum.ToObject(info.FieldType, field) : field);
            return (T)boxed;
        }
        static void Handles<T>() where T : struct, IEquatable<T>
        {
            var values = new Dictionary<T, int>();
            // Distinct arbitrary identities outside the fixed capture matrix.
            for (int i = 0; i < 200; i++)
            {
                ulong device = (ulong)i * 0x100000001UL;
                uint feature = (uint)(i % 7);
                T value = Set(Set(default(T), "m_DeviceId", device), "m_FeatureIndex", feature);
                values.Add(value, i);
                Assert.Equal(i, values[Set(Set(default(T), "m_DeviceId", device), "m_FeatureIndex", feature)]);
                Assert.Equal(device.GetHashCode() ^ (feature.GetHashCode() << 1), value.GetHashCode());
                Assert.True(value.Equals((object)value)); Assert.False(value.Equals((object)null));
            }
            Assert.Equal(200, values.Count);
        }
        [Fact]
        public void FeatureHandlesKeepBothIdentityComponentsInCollections()
        {
            Handles<Bone>(); Handles<Eyes>(); Handles<Hand>();
        }
        [Fact]
        public void CapabilityBufferSupportIsDistinctEvenWhenItsHashCollides()
        {
            var first = new HapticCapabilities { numChannels = 3, bufferMaxSize = uint.MaxValue };
            var second = first; second.supportsBuffer = true;
            Assert.NotEqual(first, second); Assert.True(first != second);
            Assert.Equal(first.GetHashCode(), second.GetHashCode());
            Assert.Equal(2, new HashSet<HapticCapabilities> { first, second }.Count);
            Assert.False(first.supportsBuffer); Assert.True(second.supportsBuffer);
        }
        [Fact]
        public void NodeAvailabilityOverridesStoredDataAndCopiesKeepTheirOwnMasks()
        {
            var state = new XRNodeState { position = new Vector3(1, 2, 3), rotation = new Quaternion(.2f, .3f, .4f, .5f), tracked = true };
            var unavailable = Set(state, "m_AvailableFields", 0);
            Assert.False(unavailable.TryGetPosition(out var p)); Assert.Equal(Vector3.zero, p);
            Assert.False(unavailable.TryGetRotation(out var r)); Assert.Equal(Quaternion.identity, r);
            unavailable.velocity = new Vector3(4, 5, 6);
            Assert.True(unavailable.TryGetVelocity(out var v)); Assert.Equal(new Vector3(4, 5, 6), v);
            Assert.False(unavailable.TryGetPosition(out _));
            Assert.True(state.TryGetPosition(out p)); Assert.Equal(new Vector3(1, 2, 3), p);
            Assert.False(state.TryGetVelocity(out _));
            state.tracked = false; Assert.True(state.TryGetPosition(out _));
            Assert.False(Set(state, "m_Tracked", -1).tracked); Assert.False(Set(state, "m_Tracked", 2).tracked);
            Assert.True(Set(state, "m_Tracked", 1).tracked);
        }
    }
}
