using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using Xunit;
using Object = UnityEngine.Object;

namespace UnityEngine.Tests
{
    /// <summary>
    /// Texture2D.LoadRawTextureData's three forms as Unity 6000.0's own C# (Texture.cs) has them: the
    /// NativeArray form loads what the array form loads; empty input is logged by the array and
    /// pointer forms and thrown by the NativeArray form.
    /// </summary>
    public class LoadRawTextureDataFormsTests
    {
        sealed class Capture : ILogHandler
        {
            internal readonly List<(LogType Type, string Text, Object Context)> Logs = new List<(LogType, string, Object)>();
            public void LogException(Exception exception, Object context) { }
            public void LogFormat(LogType type, Object context, string format, params object[] args) =>
                Logs.Add((type, string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args), context));
        }

        static readonly byte[] k_Pixels = { 0, 255, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 10, 20, 30, 40 };

        static Texture2D Texture() => new Texture2D(2, 2, TextureFormat.RGBA32, false);

        [Fact]
        public void TheNativeArrayFormLoadsWhatTheArrayFormLoads()
        {
            var expected = Texture();
            var actual = Texture();
            var array = new NativeArray<byte>(k_Pixels, Allocator.Temp);
            try
            {
                expected.LoadRawTextureData(k_Pixels);
                actual.LoadRawTextureData(array);
                Assert.Equal(expected.GetPixels32(), actual.GetPixels32());
                Assert.Equal(new Color32(10, 20, 30, 40), actual.GetPixels32()[3]);
            }
            finally
            {
                array.Dispose();
                Object.DestroyImmediate(expected);
                Object.DestroyImmediate(actual);
            }
        }

        [Fact]
        public void TheNativeArrayFormCountsElementSizes()
        {
            var texture = Texture();
            var words = new NativeArray<uint>(4, Allocator.Temp);
            try
            {
                for (int i = 0; i < 4; i++) words[i] = BitConverter.ToUInt32(k_Pixels, i * 4);
                texture.LoadRawTextureData(words);
                Assert.Equal(new Color32(10, 20, 30, 40), texture.GetPixels32()[3]);
                // Three words are 12 bytes, short of the 16 the level needs.
                var shortWords = new NativeArray<uint>(3, Allocator.Temp);
                try
                {
                    var error = Assert.Throws<UnityException>(() => texture.LoadRawTextureData(shortWords));
                    Assert.Equal("LoadRawTextureData: not enough data provided (will result in overread).", error.Message);
                }
                finally { shortWords.Dispose(); }
            }
            finally
            {
                words.Dispose();
                Object.DestroyImmediate(texture);
            }
        }

        [Fact]
        public void EmptyInputIsLoggedByTheArrayAndPointerFormsAndThrownByTheNativeArrayForm()
        {
            var texture = Texture();
            texture.LoadRawTextureData(k_Pixels);
            var logs = new Capture();
            var handler = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = logs;
            try
            {
                texture.LoadRawTextureData((byte[])null);
                texture.LoadRawTextureData(new byte[0]);
                texture.LoadRawTextureData(IntPtr.Zero, 16);
                Assert.Equal(3, logs.Logs.Count);
                foreach (var log in logs.Logs)
                {
                    Assert.Equal(LogType.Error, log.Type);
                    Assert.Equal("No texture data provided to LoadRawTextureData", log.Text);
                    Assert.Same(texture, log.Context);
                }
                var error = Assert.Throws<UnityException>(() => texture.LoadRawTextureData(default(NativeArray<byte>)));
                Assert.Equal("No texture data provided to LoadRawTextureData", error.Message);
                var empty = new NativeArray<byte>(0, Allocator.Temp);
                try { Assert.Throws<UnityException>(() => texture.LoadRawTextureData(empty)); }
                finally { empty.Dispose(); }
                // Nothing was loaded: the texels stay as they were.
                Assert.Equal(new Color32(10, 20, 30, 40), texture.GetPixels32()[3]);
            }
            finally
            {
                Debug.unityLogger.logHandler = handler;
                Object.DestroyImmediate(texture);
            }
        }
    }
}
