using System;
using System.Collections.Generic;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    public sealed class DefaultAudioStateTests : IDisposable
    {
        readonly string release = ProjectInfo.UnityVersion;
        public DefaultAudioStateTests() { ProjectInfo.UnityVersion = "6000.0.71f1"; }
        public void Dispose() { ProjectInfo.UnityVersion = release; }

        [RequireComponent(typeof(AudioBehaviour))]
        sealed class RequiresAudio : MonoBehaviour { }

        [Theory]
        [InlineData(typeof(AudioLowPassFilter))]
        [InlineData(typeof(AudioHighPassFilter))]
        [InlineData(typeof(AudioEchoFilter))]
        [InlineData(typeof(AudioDistortionFilter))]
        [InlineData(typeof(AudioChorusFilter))]
        [InlineData(typeof(AudioReverbFilter))]
        public void FiltersNeedAnExistingSourceOrListener(Type filter)
        {
            foreach (var provider in new[] { null, typeof(AudioSource), typeof(AudioListener) })
            {
                var owner = new GameObject("audio requirement");
                try
                {
                    owner.SetActive(false);
                    if (provider != null) owner.AddComponent(provider);
                    var result = owner.AddComponent(filter);
                    Assert.Equal(provider != null, result != null);
                    Assert.Equal(provider == null ? 1 : 3, owner.GetComponents<Component>().Length);
                }
                finally { Object.DestroyImmediate(owner); }
            }
        }

        [Fact]
        public void AudioBaseCannotBeCreatedAndConcreteProvidersSatisfyScriptRequirements()
        {
            var owner = new GameObject("required audio");
            try
            {
                owner.SetActive(false);
                Assert.Null(owner.AddComponent(typeof(AudioBehaviour)));
                Assert.Null(owner.AddComponent<RequiresAudio>());
                owner.AddComponent<AudioListener>();
                Assert.Null(owner.AddComponent(typeof(AudioBehaviour)));
                Assert.NotNull(owner.AddComponent<RequiresAudio>());
            }
            finally { Object.DestroyImmediate(owner); }
        }

        sealed class WarningCapture : ILogHandler
        {
            internal readonly List<string> warnings = new List<string>();
            public void LogFormat(LogType type, Object context, string format, params object[] args)
            { if (type == LogType.Warning) warnings.Add(string.Format(format, args)); }
            public void LogException(Exception exception, Object context) { }
        }

        [Fact]
        public void RemovedReverbSettingIgnoresWritesAndWarnsOnEveryReadAndWrite()
        {
            var owner = new GameObject("removed reverb setting"); var previous = Debug.unityLogger.logHandler;
            var capture = new WarningCapture();
            try
            {
                owner.SetActive(false); var zone = owner.AddComponent<AudioReverbZone>();
                var property = typeof(AudioReverbZone).GetProperty("roomRolloffFactor");
                Debug.unityLogger.logHandler = capture;
                property.SetValue(zone, 2.5f); Assert.Equal(10f, property.GetValue(zone));
                zone.reverbPreset = AudioReverbPreset.Cave;
                property.SetValue(zone, -4f); Assert.Equal(10f, property.GetValue(zone));
                Assert.Equal(4, capture.warnings.Count);
                Assert.All(capture.warnings, warning => Assert.Equal("Warning! roomRolloffFactor is no longer supported.", warning));
            }
            finally { Debug.unityLogger.logHandler = previous; Object.DestroyImmediate(owner); }
        }
    }
}
