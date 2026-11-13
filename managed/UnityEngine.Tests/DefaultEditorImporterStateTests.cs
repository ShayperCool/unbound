using System;
using Unbound.Runtime;
using UnityEditor;
using UnityEditor.AssetImporters;
using Xunit;

namespace UnityEngine.Tests
{
    public sealed class DefaultEditorImporterStateTests : IDisposable
    {
        readonly string release = ProjectInfo.UnityVersion;
        public DefaultEditorImporterStateTests() { ProjectInfo.UnityVersion = "6000.0.71f1"; }
        public void Dispose() { ProjectInfo.UnityVersion = release; }

        [Theory]
        [InlineData(typeof(AssetImporter))]
        [InlineData(typeof(TextureImporter))]
        [InlineData(typeof(MonoImporter))]
        [InlineData(typeof(PluginImporter))]
        public void ManualWrappersHaveNoEngineIdentityOrReadableProperties(Type type)
        {
            using var engine = MainThreadCheck.Engine();
            var before = ObjectRegistry.All(type).Count;
            var importer = (AssetImporter)Activator.CreateInstance(type);
            Assert.True(importer == null); Assert.False(importer);
            Assert.Equal(0, importer.GetInstanceID());
            Assert.Equal(before, ObjectRegistry.All(type).Count);
            Assert.Throws<NullReferenceException>(() => importer.assetPath);
            Assert.Throws<NullReferenceException>(() => importer.userData);
            Assert.Throws<NullReferenceException>(() => importer.name);
            Assert.Throws<NullReferenceException>(() => importer.hideFlags);
        }

        [Fact]
        public void RemovedTextureSettingsIgnoreWritesWithoutRequiringAnEngineObject()
        {
            using var engine = MainThreadCheck.Engine();
            foreach (var bound in new[] { false, true })
            {
                var importer = bound ? (TextureImporter)ObjectConstruction.CreateWithBacking(typeof(TextureImporter)) : new TextureImporter();
                foreach (var name in new[] { "correctGamma", "generateMipsInLinearSpace", "spritePackingTag" })
                {
                    var property = typeof(TextureImporter).GetProperty(name);
                    foreach (var changed in new[] { false, true })
                    {
                        property.SetValue(importer, property.PropertyType == typeof(bool) ? (object)changed : changed ? null : "changed-tag");
                        Assert.Equal(property.PropertyType == typeof(bool) ? (object)true : "", property.GetValue(importer));
                    }
                }
                if (bound) Object.DestroyImmediate(importer);
            }
        }

        sealed class ConstructorImporter : ScriptedImporter
        {
            static bool makingNested;
            internal readonly string FieldState = NestedInitializer();
            internal string State, Message, NestedState;
            static string NestedInitializer()
            {
                if (makingNested) return "recursion-stop";
                makingNested = true;
                try
                {
                    var nested = new ConstructorImporter();
                    try { return "value:" + nested.userData; }
                    catch (Exception error) { return error.GetType().FullName; }
                }
                finally { makingNested = false; }
            }
            public ConstructorImporter()
            {
                try { State = "value:" + userData; }
                catch (Exception error) { State = error.GetType().FullName; Message = error.Message; }
                var nested = new TextureImporter();
                try { NestedState = "value:" + nested.userData; }
                catch (Exception error) { NestedState = error.GetType().FullName; }
            }
            public override void OnImportAsset(AssetImportContext context) { }
        }

        [Fact]
        public void FactoryBindingBelongsToTheExactInstanceBeforeFieldInitialization()
        {
            using var engine = MainThreadCheck.Engine();
            var manual = new ConstructorImporter();
            Assert.Equal(typeof(NullReferenceException).FullName, manual.State);
            Assert.Equal(typeof(NullReferenceException).FullName, manual.FieldState);
            var bound = (ConstructorImporter)ObjectConstruction.CreateWithBacking(typeof(ConstructorImporter));
            try
            {
                Assert.True(bound); Assert.NotEqual(0, bound.GetInstanceID());
                Assert.Equal(typeof(UnityException).FullName, bound.State);
                Assert.Equal("get_userData is not allowed to be called from a script constructor (or instance field initializer). Called from 'ConstructorImporter'.\nSee \"Script Serialization\" page in the Unity Manual for further details.", bound.Message);
                Assert.Equal(typeof(NullReferenceException).FullName, bound.NestedState);
                Assert.Equal(typeof(NullReferenceException).FullName, bound.FieldState);
                bound.userData = "changed"; Assert.Equal("changed", bound.userData);
                bound.userData = null; bound.assetBundleName = null; bound.assetBundleVariant = null;
                Assert.Equal("", bound.userData); Assert.Equal("", bound.assetBundleName); Assert.Equal("", bound.assetBundleVariant);
                var later = new TextureImporter(); Assert.True(later == null);
            }
            finally { Object.DestroyImmediate(bound); }
        }

        sealed class BrokenImporter : ScriptedImporter
        {
            internal bool Entered;
            public BrokenImporter() { Entered = true; throw new InvalidOperationException("constructor control"); }
            public override void OnImportAsset(AssetImportContext context) { }
        }

        sealed class ExceptionCapture : ILogHandler
        {
            internal int Exceptions;
            public void LogFormat(LogType type, Object context, string format, params object[] args) { }
            public void LogException(Exception error, Object context) { Exceptions++; }
        }

        [Fact]
        public void ConstructorErrorsRetainTheObjectAndRestoreConstructionState()
        {
            using var engine = MainThreadCheck.Engine();
            var previous = Debug.unityLogger.logHandler; var capture = new ExceptionCapture();
            AssetImporter importer = null;
            try
            {
                Debug.unityLogger.logHandler = capture;
                importer = (AssetImporter)ObjectConstruction.CreateWithBacking(typeof(BrokenImporter));
                Assert.True(importer); Assert.True(((BrokenImporter)importer).Entered);
                Assert.NotEqual(0, importer.GetInstanceID()); Assert.Equal(1, capture.Exceptions);
                importer.userData = "after"; Assert.Equal("after", importer.userData);
                Assert.True(new TextureImporter() == null);
            }
            finally { Debug.unityLogger.logHandler = previous; Object.DestroyImmediate(importer); }
        }

        [Fact]
        public void UnmeasuredReleasesRetainTheirExistingManualWrapperBehavior()
        {
            using var engine = MainThreadCheck.Engine();
            ProjectInfo.UnityVersion = "6000.0.70f1";
            var importer = new TextureImporter();
            try
            {
                Assert.True(importer); Assert.NotEqual(0, importer.GetInstanceID());
                Assert.Equal("", importer.assetPath); importer.userData = null; Assert.Null(importer.userData);
            }
            finally { Object.DestroyImmediate(importer); }
        }
    }
}
