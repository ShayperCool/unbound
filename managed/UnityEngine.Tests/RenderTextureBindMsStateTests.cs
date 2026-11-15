using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.Rendering;
using Xunit;
using Object = UnityEngine.Object;

namespace Unbound.Tests
{
    [Collection("Project release")]
    public class RenderTextureBindMsStateTests
    {
        sealed class Capture : ILogHandler
        {
            internal readonly List<(LogType, string)> Entries = new List<(LogType, string)>();
            public void LogException(Exception exception, Object context) => Entries.Add((LogType.Exception, exception.Message));
            public void LogFormat(LogType type, Object context, string format, params object[] args) => Entries.Add((type, string.Format(format, args)));
        }

        static void Measured(Action<Capture> check)
        {
            string release = ProjectInfo.UnityVersion;
            var previous = Debug.unityLogger.logHandler;
            var state = GfxStream.Current;
            var capture = new Capture();
            try
            {
                ProjectInfo.UnityVersion = "6000.0.71f1";
                Debug.unityLogger.logHandler = capture;
                check(capture);
            }
            finally
            {
                Debug.unityLogger.logHandler = previous;
                ProjectInfo.UnityVersion = release;
                GfxStream.Restore(state);
            }
        }

        [Theory]
        [InlineData(1, false, false)]
        [InlineData(1, false, true)]
        [InlineData(1, true, false)]
        [InlineData(1, true, true)]
        [InlineData(4, false, false)]
        [InlineData(4, false, true)]
        [InlineData(4, true, false)]
        [InlineData(4, true, true)]
        public void BindingChangesRequireReleasedStorageButSameValuesStayQuiet(int samples, bool initial, bool same)
        {
            Measured(capture =>
            {
                var rt = new RenderTexture(5, 2, 0) { antiAliasing = samples, bindTextureMS = initial };
                try
                {
                    bool created = rt.Create();
                    Assert.Equal(samples > 1 || !initial, created);
                    capture.Entries.Clear();
                    bool requested = same ? initial : !initial;
                    rt.bindTextureMS = requested;
                    bool refused = created && requested != initial;
                    Assert.Equal(refused ? initial : requested, rt.bindTextureMS);
                    Assert.Equal(created, rt.IsCreated());
                    if (refused)
                    {
                        Assert.Single(capture.Entries);
                        Assert.Equal((LogType.Error, "Setting bind as Texture2DMS of already created render texture is not supported!"), capture.Entries[0]);
                    }
                    else Assert.Empty(capture.Entries);
                    capture.Entries.Clear();
                    rt.Release();
                    rt.bindTextureMS = !initial;
                    Assert.False(rt.IsCreated());
                    Assert.Equal(!initial, rt.bindTextureMS);
                    Assert.Empty(capture.Entries);
                    Assert.Equal(samples > 1 || initial, rt.Create());
                }
                finally { Object.DestroyImmediate(rt); }
            });
        }

        [Theory]
        [InlineData(TextureDimension.Tex2D, true)]
        [InlineData(TextureDimension.Tex2DArray, true)]
        [InlineData(TextureDimension.Cube, false)]
        [InlineData(TextureDimension.Tex3D, false)]
        public void MultisampleBindingBeforeCreationRequiresATwoDimensionalTexture(TextureDimension dimension, bool allowed)
        {
            Measured(capture =>
            {
                var rt = new RenderTexture(4, 4, 0) { dimension = dimension, volumeDepth = dimension == TextureDimension.Cube ? 1 : 2 };
                try
                {
                    rt.bindTextureMS = true;
                    Assert.Equal(allowed, rt.bindTextureMS);
                    Assert.False(rt.IsCreated());
                    if (allowed) Assert.Empty(capture.Entries);
                    else
                    {
                        Assert.Single(capture.Entries);
                        Assert.Equal((LogType.Error, "Invalid operation for this texture type"), capture.Entries[0]);
                    }
                }
                finally { Object.DestroyImmediate(rt); }
            });
        }
    }
}
