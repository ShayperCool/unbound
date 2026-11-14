using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Xunit;

namespace UnityEngine.Tests
{
    public class ValuePayloadTests
    {
        [Fact]
        public void LocationSamplesKeepIndependentScalarBitsAndUnmanagedCopies()
        {
            var value = new LocationInfo {
                m_Timestamp = BitConverter.Int64BitsToDouble(unchecked((long)0x8000000000000000UL)),
                m_Latitude = -123.25f, m_Longitude = 987.5f, m_Altitude = float.NaN,
                m_HorizontalAccuracy = float.PositiveInfinity, m_VerticalAccuracy = -.125f,
            };
            Assert.Equal(long.MinValue, BitConverter.DoubleToInt64Bits(value.timestamp));
            Assert.Equal(-123.25f, value.latitude); Assert.Equal(987.5f, value.longitude);
            Assert.True(float.IsNaN(value.altitude)); Assert.Equal(float.PositiveInfinity, value.horizontalAccuracy);
            Assert.Equal(-.125f, value.verticalAccuracy); Assert.Equal(32, Marshal.SizeOf<LocationInfo>());
            IntPtr buffer = Marshal.AllocHGlobal(Marshal.SizeOf<LocationInfo>());
            try
            {
                Marshal.StructureToPtr(value, buffer, false);
                var copy = Marshal.PtrToStructure<LocationInfo>(buffer);
                Assert.Equal(long.MinValue, BitConverter.DoubleToInt64Bits(copy.timestamp));
                Assert.Equal(value.latitude, copy.latitude); Assert.True(float.IsNaN(copy.altitude));
                copy.m_Latitude = 17; Assert.Equal(-123.25f, value.latitude); Assert.Equal(17, copy.latitude);
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        [Fact]
        public void RetiredAnimationValueHasNoPayloadOrSampleState()
        {
            var type = typeof(Animator).Assembly.GetType("UnityEngine.AnimationInfo", true);
            Assert.True(type.IsValueType);
            Assert.Empty(type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
            Assert.Equal(1, Marshal.SizeOf(type));
            var value = Activator.CreateInstance(type);
            Assert.Null(type.GetProperty("clip").GetValue(value));
            Assert.Equal(0f, (float)type.GetProperty("weight").GetValue(value));
        }
    }
}
