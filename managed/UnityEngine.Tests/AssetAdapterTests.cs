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
                ProjectInfo.UnityVersion = "6000.0.70f1";
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
