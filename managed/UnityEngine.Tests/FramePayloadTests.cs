using System;
using UnityEngine.Playables;
using Xunit;

namespace UnityEngine.Tests
{
    public class FramePayloadTests
    {
        [Serializable] sealed class Holder<T> { public T value; public int tail = 11; }
        [Fact]
        public void FrameRateJsonRestoresSignedPayloadAndPreservesOmittedFields()
        {
            var original = new FrameRate(59, true);
            Assert.Equal("{\"m_Rate\":-59}", JsonUtility.ToJson(original));
            var restored = JsonUtility.FromJson<FrameRate>(JsonUtility.ToJson(original));
            Assert.Equal(original, restored); Assert.True(restored.dropFrame); Assert.True(restored.IsValid());
            object boxed = original;
            JsonUtility.FromJsonOverwrite("{\"m_Rate\":85}", boxed);
            Assert.Equal(85, FrameRate.FrameRateToInt((FrameRate)boxed)); Assert.False(((FrameRate)boxed).dropFrame);
            JsonUtility.FromJsonOverwrite("{\"other\":7}", boxed);
            Assert.Equal(85, FrameRate.FrameRateToInt((FrameRate)boxed));
            Assert.Equal(-59, FrameRate.FrameRateToInt(original));
        }
        [Fact]
        public void FieldMarkerDoesNotEnableAnUnmarkedNestedType()
        {
            var holder = new Holder<FrameRate> { value = new FrameRate(47, true) };
            Assert.Equal("{\"tail\":11}", JsonUtility.ToJson(holder));
            JsonUtility.FromJsonOverwrite("{\"value\":{\"m_Rate\":31},\"tail\":23}", holder);
            Assert.Equal(-47, FrameRate.FrameRateToInt(holder.value)); Assert.Equal(23, holder.tail);
        }
    }
}
