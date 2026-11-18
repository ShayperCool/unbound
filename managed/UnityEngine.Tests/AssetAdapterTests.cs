using System;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    public class AssetAdapterTests : IDisposable
    {
        readonly string m_Release = ProjectInfo.UnityVersion;
        public AssetAdapterTests() { ProjectInfo.UnityVersion = "6000.0.71f1"; }
        public void Dispose() { ProjectInfo.UnityVersion = m_Release; }
        sealed class Capture : ILogHandler
        {
            public readonly System.Collections.Generic.List<string> Errors = new System.Collections.Generic.List<string>();
            public void LogException(Exception exception, Object context) { }
            public void LogFormat(LogType type, Object context, string format, params object[] args)
            {
                if (type == LogType.Error) Errors.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args));
            }
        }
        [Fact]
        public void MaterialValidationDistinguishesFiniteClampsAndNonfiniteWrites()
        {
            var previous = Debug.unityLogger.logHandler; var capture = new Capture(); Debug.unityLogger.logHandler = capture;
            var three = new PhysicsMaterial("range3d"); var two = new PhysicsMaterial2D("range2d") { friction = .625f };
            try
            {
                three.staticFriction = -2;
                Assert.Equal(0, three.staticFriction);
                Assert.Contains("Physics material range3d cannot have staticFriction = -2.000000", capture.Errors);
                three.dynamicFriction = float.PositiveInfinity;
                Assert.Equal(0x7f7ffffe, BitConverter.SingleToInt32Bits(three.dynamicFriction));
                three.bounciness = 2; Assert.Equal(1, three.bounciness);
                int count = capture.Errors.Count;
                three.staticFriction = float.NaN; Assert.True(float.IsNaN(three.staticFriction)); Assert.Equal(count, capture.Errors.Count);
                two.friction = 100001; Assert.Equal(100000, two.friction); Assert.Equal(count, capture.Errors.Count);
                two.friction = float.NaN; Assert.Equal(100000, two.friction);
                Assert.Contains("PhysicsMaterial2D.friction assign attempt for 'range2d' is not valid. Input friction is { NaN }.", capture.Errors);
                ProjectInfo.UnityVersion = "6000.1.0f1";
                three.staticFriction = -2; Assert.Equal(-2, three.staticFriction);
                two.friction = float.NaN; Assert.True(float.IsNaN(two.friction));
            }
            finally { Debug.unityLogger.logHandler = previous; Object.DestroyImmediate(three); Object.DestroyImmediate(two); }
        }
        class DerivedMaterial : PhysicsMaterial { }

        [Fact]
        public void ReaderDiscoveryAndDispatchIncludeDerivedAssets()
        {
            var material = new DerivedMaterial();
            Assert.Equal(typeof(PhysicsMaterial), AssetAdapters.Reader(typeof(DerivedMaterial)).DeclaringType);
            Assert.True(AssetAdapters.TryRead(material, TestYaml.DocumentBody(@"
PhysicMaterial:
  m_DynamicFriction: 0.125
  m_StaticFriction: 0.375
  m_Bounciness: 0.625
  m_FrictionCombine: 1
  m_BounceCombine: 3
"), null));
            Assert.Equal(.125f, material.dynamicFriction);
            Assert.Equal(.375f, material.staticFriction);
            Assert.Equal(.625f, material.bounciness);
            Assert.Equal(PhysicsMaterialCombine.Multiply, material.frictionCombine);
            Assert.Equal(PhysicsMaterialCombine.Maximum, material.bounceCombine);
            var report = ClassCoverage.Report("6000.0.71f1", new[] {
                new ClassCoverage.Found { ClassId = 134, TypeName = "PhysicMaterial", Keys = new System.Collections.Generic.List<string> { "m_DynamicFriction" } },
            });
            Assert.Equal("Read", Assert.Single(report.Classes).Reading);
            Assert.False(typeof(ISerializedAsset).IsAssignableFrom(typeof(DerivedMaterial)));
            Object.DestroyImmediate(material);
        }

        [Fact]
        public void TextCloneOwnsItsLoadedSnapshotAndDoesNotReuseItsSourceArray()
        {
            var source = new TextAsset();
            Assert.True(AssetAdapters.TryRead(source, TestYaml.DocumentBody(@"
TextAsset:
  m_Script: {_bytes: ff00417f}
"), null));
            var copy = Object.Instantiate(source);
            Assert.Equal(new byte[] { 255, 0, 65, 127 }, copy.bytes);
            Assert.NotSame(source.m_Bytes, copy.m_Bytes);
            source.m_Bytes[0] = 3;
            Assert.Equal(255, copy.bytes[0]);
            Object.DestroyImmediate(source);
            Assert.Equal(new byte[] { 255, 0, 65, 127 }, copy.bytes);
            Object.DestroyImmediate(copy);
        }

        [Fact]
        public void AudioClipDispatchRetainsBundleMetadataAndEmptyResourceCloneRules()
        {
            var source = (AudioClip)Activator.CreateInstance(typeof(AudioClip), nonPublic: true);
            var previous = Debug.unityLogger.logHandler; var capture = new Capture(); Debug.unityLogger.logHandler = capture;
            try
            {
                Assert.Equal(typeof(AudioClip), AssetAdapters.Reader(typeof(AudioClip)).DeclaringType);
                Assert.True(AssetAdapters.TryRead(source, TestYaml.DocumentBody(@"
AudioClip:
  m_LoadType: 1
  m_Channels: 2
  m_Frequency: 22050
  m_Length: 0.25
  m_PreloadAudioData: 0
  m_LoadInBackground: 1
  m_Ambisonic: 1
  m_CompressionFormat: 0
"), null));
                Assert.Equal(2, source.channels); Assert.Equal(22050, source.frequency); Assert.Equal(5512, source.samples);
                Assert.True(source.ambisonic); Assert.True(source.loadInBackground); Assert.False(source.preloadAudioData);
                Assert.Contains("has no audio data Unbound can read", Assert.Single(capture.Errors));
                Assert.False(typeof(ISerializedAsset).IsAssignableFrom(typeof(AudioClip)));
                Assert.False(typeof(Instantiation.ICloneable).IsAssignableFrom(typeof(AudioClip)));
                Assert.True(AssetAdapters.TryClone(source, out var cloned));
                var copy = Assert.IsType<AudioClip>(cloned);
                Assert.NotSame(source, copy); Assert.Equal(2, copy.channels); Assert.Equal(22050, copy.frequency);
                Assert.Equal(0, copy.samples);
                Object.DestroyImmediate(source); Assert.Equal(22050, copy.frequency); Object.DestroyImmediate(copy);
            }
            finally { Debug.unityLogger.logHandler = previous; if (source) Object.DestroyImmediate(source); }
        }

        [Fact]
        public void FontDispatchCopiesItsLoadedCharacterDataIndependently()
        {
            var source = new Font();
            Assert.Equal(typeof(Font), AssetAdapters.Reader(typeof(Font)).DeclaringType);
            Assert.True(AssetAdapters.TryRead(source, TestYaml.DocumentBody(@"
Font:
  m_FontSize: 27
  m_Ascent: 19
  m_Descent: -4
  m_LineSpacing: 34
  m_FontNames:
  - CaseA
  - CaseB
  m_CharacterRects:
  - index: 65
    advance: 7
    uv: {x: 0.1, y: 0.2, width: 0.3, height: 0.4}
    vert: {x: 1, y: 2, width: 5, height: -8}
"), new LoadedFile()));
            Assert.Equal(27, source.fontSize); Assert.Equal(19, source.ascent); Assert.Equal(34, source.lineHeight);
            Assert.Equal(new[] { "CaseA", "CaseB" }, source.m_FontNames); Assert.Single(source.characterInfo);
            Assert.False(typeof(ISerializedAsset).IsAssignableFrom(typeof(Font)));
            Assert.False(typeof(Instantiation.ICloneable).IsAssignableFrom(typeof(Font)));
            var copy = Object.Instantiate(source);
            Assert.NotSame(source.m_FontNames, copy.m_FontNames);
            Assert.Equal(new[] { "CaseA", "CaseB" }, copy.m_FontNames);
            var characters = copy.characterInfo; characters[0].advance = 33; copy.characterInfo = characters;
            Assert.Equal(7, source.characterInfo[0].advance);
            Assert.Equal(33, copy.characterInfo[0].advance);
            Object.DestroyImmediate(source); Assert.Equal(33, copy.characterInfo[0].advance); Object.DestroyImmediate(copy);
        }

        [Fact]
        public void UnmigratedReadersAndClonersKeepTheirDispatch()
        {
            var source = new Texture2D(1, 1);
            Assert.NotNull(AssetAdapters.Reader(typeof(Texture2D)));
            var copy = Object.Instantiate(source);
            Assert.NotSame(source, copy);
            Assert.Equal(1, copy.width);
            Assert.Null(AssetAdapters.Reader(typeof(GameObject)));
            Object.DestroyImmediate(copy);
            Object.DestroyImmediate(source);
        }
    }
}
