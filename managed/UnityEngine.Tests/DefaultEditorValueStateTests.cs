using System;
using System.Reflection;
using Unbound.Runtime;
using UnityEditor;
using UnityEditor.Build.Content;
using Xunit;

namespace UnityEngine.Tests
{
    public sealed class DefaultEditorValueStateTests : IDisposable
    {
        readonly string release = ProjectInfo.UnityVersion;
        public DefaultEditorValueStateTests() { ProjectInfo.UnityVersion = "6000.0.71f1"; }
        public void Dispose() { ProjectInfo.UnityVersion = release; }

        [Fact]
        public void MissingCollectionStorageDiffersFromAValidEmptyOrPopulatedResult()
        {
            Assert.Throws<NullReferenceException>(() => default(TypeCache.TypeCollection).Count);
            Assert.Throws<NullReferenceException>(() => default(TypeCache.MethodCollection).Count);
            Assert.Throws<NullReferenceException>(() => default(TypeCache.FieldInfoCollection).Count);
            Assert.Equal(0, new TypeCache.TypeCollection(Array.Empty<Type>()).Count);
            Assert.Equal(0, new TypeCache.MethodCollection(Array.Empty<MethodInfo>()).Count);
            Assert.Equal(0, new TypeCache.FieldInfoCollection(Array.Empty<FieldInfo>()).Count);
            Assert.Equal(1, new TypeCache.TypeCollection(new[] { typeof(string) }).Count);
            Assert.Equal(1, new TypeCache.MethodCollection(new[] { typeof(string).GetMethod("IsNullOrEmpty") }).Count);
            Assert.Equal(1, new TypeCache.FieldInfoCollection(new[] { typeof(string).GetField("Empty") }).Count);
        }

        [Fact]
        public void UnsetPathsRetainNullAndExplicitWritesRetainTheirValues()
        {
            var processor = new AssetPostprocessor();
            Assert.Null(processor.assetPath);
            processor.assetPath = "Assets/explicit.asset"; Assert.Equal("Assets/explicit.asset", processor.assetPath);
            processor.assetPath = ""; Assert.Equal("", processor.assetPath);
            processor.assetPath = null; Assert.Null(processor.assetPath);
            var identifier = default(ObjectIdentifier);
            Assert.Null(identifier.filePath);
            identifier.filePath = "explicit-path"; Assert.Equal("explicit-path", identifier.filePath);
            identifier.filePath = null; Assert.Null(identifier.filePath);
        }

        [Fact]
        public void OtherReleasesRetainEmptyFallbacks()
        {
            ProjectInfo.UnityVersion = "6000.0.70f1";
            Assert.Equal(0, default(TypeCache.TypeCollection).Count);
            Assert.Equal(0, default(TypeCache.MethodCollection).Count);
            Assert.Equal(0, default(TypeCache.FieldInfoCollection).Count);
            var processor = new AssetPostprocessor();
            Assert.Equal("", processor.assetPath);
            processor.assetPath = null; Assert.Equal("", processor.assetPath);
            Assert.Equal("", default(ObjectIdentifier).filePath);
        }
    }
}
