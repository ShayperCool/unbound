using System;
using System.Collections.Generic;
using System.Reflection;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.Rendering;
using Xunit;
using Object = UnityEngine.Object;

namespace Unbound.Tests
{
    [Collection("Frames")]
    public sealed class CustomRenderTextureFrameTests
    {
        const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
        static readonly Type Manager = typeof(CustomRenderTextureManager);
        static readonly Type Loop = typeof(PlayerLoopDriver);
        static void Step(int frame)
        {
            Time.s_FrameCount = frame;
            OnDemandRendering.LatchFrameInterval();
            Manager.GetMethod("UpdateAll", Static).Invoke(null, null);
        }

        sealed class Scope : IDisposable
        {
            readonly string release = ProjectInfo.UnityVersion;
            readonly int frame = Time.s_FrameCount, interval = OnDemandRendering.renderFrameInterval, latched = OnDemandRendering.s_FrameInterval;
            readonly bool booted = (bool)Loop.GetField("s_Booted", Static).GetValue(null);
            readonly bool entered = (bool)Loop.GetField("s_EnteredPlayMode", Static).GetValue(null);
            readonly bool playing = EditorSession.IsPlaying;
            readonly List<CustomRenderTexture> textures = (List<CustomRenderTexture>)Manager.GetField("s_Textures", Static).GetValue(null);
            readonly CustomRenderTexture[] saved;
            readonly FieldInfo last = Manager.GetField("s_LastUpdateFrame", Static);
            readonly object previousLast;
            internal Scope()
            {
                saved = textures.ToArray();
                textures.Clear();
                previousLast = last?.GetValue(null);
                last?.SetValue(null, int.MinValue);
                Loop.GetField("s_Booted", Static).SetValue(null, false);
                ProjectInfo.UnityVersion = "6000.0.71f1";
                OnDemandRendering.renderFrameInterval = 1;
                GfxStream.Flush();
            }
            public void Dispose()
            {
                foreach (var texture in textures.ToArray()) Object.DestroyImmediate(texture);
                textures.Clear();
                textures.AddRange(saved);
                ProjectInfo.UnityVersion = release;
                Time.s_FrameCount = frame;
                OnDemandRendering.renderFrameInterval = interval;
                OnDemandRendering.s_FrameInterval = latched;
                Loop.GetField("s_Booted", Static).SetValue(null, booted);
                Loop.GetField("s_EnteredPlayMode", Static).SetValue(null, entered);
                EditorSession.IsPlaying = playing;
                if (last != null) last.SetValue(null, previousLast);
                GfxStream.Flush();
            }
        }

        static CustomRenderTexture Make() => new CustomRenderTexture(2, 2)
        {
            initializationMode = CustomRenderTextureUpdateMode.OnDemand,
            updateMode = CustomRenderTextureUpdateMode.OnDemand
        };

        [Fact]
        public void RepeatedVisitsDeferTexturesAddedAfterTheFirstVisit()
        {
            using var scope = new Scope();
            var first = Make();
            CustomRenderTexture second = null;
            void Loaded(CustomRenderTexture texture) { if (ReferenceEquals(texture, first)) second = Make(); }
            CustomRenderTextureManager.textureLoaded += Loaded;
            try
            {
                Step(20);
                Assert.True(first.m_Loaded);
                Assert.NotNull(second);
                Assert.False(second.m_Loaded);
                Step(20);
                Assert.False(second.m_Loaded);
                Step(21);
                Assert.True(second.m_Loaded);
            }
            finally { CustomRenderTextureManager.textureLoaded -= Loaded; }
        }

        [Fact]
        public void AnEmptyVisitStillOwnsThatFrame()
        {
            using var scope = new Scope();
            Step(20);
            var texture = Make();
            Step(20);
            Assert.False(texture.m_Loaded);
            Step(21);
            Assert.True(texture.m_Loaded);
        }

        [Fact]
        public void ScreenSkippedFramesRetainPendingLoads()
        {
            using var scope = new Scope();
            OnDemandRendering.renderFrameInterval = 3;
            var texture = Make();
            Step(4);
            Assert.False(texture.m_Loaded);
            Assert.False(texture.IsCreated());
            Step(6);
            Assert.True(texture.m_Loaded);
            Assert.True(texture.IsCreated());
        }

        [Fact]
        public void PlayEntryWaitsUntilTheRegularRenderingCycle()
        {
            using var scope = new Scope();
            Loop.GetField("s_Booted", Static).SetValue(null, true);
            Loop.GetField("s_EnteredPlayMode", Static).SetValue(null, false);
            EditorSession.IsPlaying = true;
            var texture = Make();
            Step(30);
            Assert.False(texture.m_Loaded);
            Loop.GetField("s_EnteredPlayMode", Static).SetValue(null, true);
            Step(31);
            Assert.True(texture.m_Loaded);
        }

        sealed class Capture : ILogHandler
        {
            internal readonly List<Exception> Exceptions = new List<Exception>();
            public void LogException(Exception exception, Object context) => Exceptions.Add(exception);
            public void LogFormat(LogType type, Object context, string format, params object[] args) { }
        }

        [Fact]
        public void AThrowingLoadedSubscriberDoesNotLosePendingLoadWork()
        {
            using var scope = new Scope();
            var first = Make();
            var second = Make();
            var previous = Debug.unityLogger.logHandler;
            var capture = new Capture();
            Debug.unityLogger.logHandler = capture;
            void Loaded(CustomRenderTexture texture)
            {
                if (ReferenceEquals(texture, first)) throw new InvalidOperationException("CRT load callback");
            }
            CustomRenderTextureManager.textureLoaded += Loaded;
            try
            {
                Step(20);
                Assert.True(first.IsCreated());
                Assert.True(second.IsCreated());
                Assert.True(second.m_Loaded);
                Assert.Equal("CRT load callback", Assert.Single(capture.Exceptions).Message);
            }
            finally
            {
                CustomRenderTextureManager.textureLoaded -= Loaded;
                Debug.unityLogger.logHandler = previous;
            }
        }

        [Fact]
        public void OtherReleasesRetainThePreviousUnmeasuredScheduling()
        {
            using var scope = new Scope();
            ProjectInfo.UnityVersion = "6000.0.70f1";
            OnDemandRendering.renderFrameInterval = 3;
            var texture = Make();
            Step(4);
            Assert.True(texture.m_Loaded);
        }
    }
}
