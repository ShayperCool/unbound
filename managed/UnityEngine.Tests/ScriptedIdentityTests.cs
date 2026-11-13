using System;
using Unbound.Runtime;
using Xunit;
using Object = UnityEngine.Object;

namespace UnityEngine.Tests
{
    [Collection("Frames")]
    public sealed class ScriptedIdentityTests : IDisposable
    {
        readonly string m_Release = ProjectInfo.UnityVersion;
        public ScriptedIdentityTests() => ProjectInfo.UnityVersion = "6000.0.71f1";
        public void Dispose() => ProjectInfo.UnityVersion = m_Release;
        sealed class LightmapParameters : Object { }

        static System.Collections.Generic.List<(int ClassId,long FileId,string Name)> Table(string fields) =>
            ScriptedImports.NameTable(TestYaml.DocumentBody("ScriptedImporter:\n"+fields));
        static string Legacy(string name="BlankTexture") => "  fileIDToRecycleName:\n    2800002: "+name+"\n    2800000: "+name+"\n";
        static string Modern(int classId,long id,string name="BlankTexture") =>
            "  internalIDToNameTable:\n  - first:\n      "+classId+": "+id+"\n    second: "+name+"\n";

        [Theory]
        [InlineData("",2800000L)]
        [InlineData("  serializedVersion: 1\n",2800000L)]
        [InlineData("  serializedVersion: 2\n",0L)]
        public void LegacySchemaAndNumericOrderChooseIdentity(string version,long expected)
        {
            var texture=new Texture2D(1,1);
            try
            {
                var result=ScriptedImports.FileId("BlankTexture",texture,Table(version+Legacy()));
                Assert.Equal(expected==0?ScriptedImports.FileId("BlankTexture",texture):expected,result);
            }
            finally { Object.DestroyImmediate(texture); }
        }

        [Theory]
        [InlineData(1,28,7654321L)]
        [InlineData(2,28,7654321L)]
        [InlineData(1,1113,2800000L)]
        public void ModernMatchingPairsWinAndWrongClassesLeaveLegacyUsable(int version,int classId,long expected)
        {
            var texture=new Texture2D(1,1);
            try { Assert.Equal(expected,ScriptedImports.FileId("BlankTexture",texture,Table("  serializedVersion: "+version+"\n"+Legacy()+Modern(classId,7654321)))); }
            finally { Object.DestroyImmediate(texture); }
        }

        [Fact]
        public void ALeafClassCatalogEntryDoesNotRequireASerializedFieldLoader()
        {
            Assert.Null(EngineTypes.ForClassId(1113,"",warn:false));
            var lighting=new UnityEditor.LightmapParameters();
            var texture=new Texture2D(1,1);
            try
            {
                var table=Table(Modern(1113,1234567,"Lighting")+Legacy());
                Assert.Equal(1234567,ScriptedImports.FileId("Lighting",lighting,table));
                Assert.Equal(2800000,ScriptedImports.FileId("BlankTexture",texture,table));
                Assert.Equal(ScriptedImports.FileId("Other",lighting),ScriptedImports.FileId("Other",lighting,table));
            }
            finally { Object.DestroyImmediate(lighting);Object.DestroyImmediate(texture); }
        }

        [Fact]
        public void MatchingClassNamesFromGameAssembliesAreNotBuiltinClasses()
        {
            var lighting=new LightmapParameters();
            try { Assert.Equal(ScriptedImports.FileId("Lighting",lighting),ScriptedImports.FileId("Lighting",lighting,Table(Modern(1113,1234567,"Lighting")))); }
            finally { Object.DestroyImmediate(lighting); }
        }

        [Fact]
        public void InvalidClassIdsAndWrongIdentifiersKeepDerivedIds()
        {
            var texture=new Texture2D(1,1);
            try
            {
                var expected=ScriptedImports.FileId("BlankTexture",texture);
                Assert.Equal(expected,ScriptedImports.FileId("BlankTexture",texture,Table(Modern(int.MaxValue,1234))));
                Assert.Equal(expected,ScriptedImports.FileId("BlankTexture",texture,Table(Modern(28,1234,"Other"))));
            }
            finally { Object.DestroyImmediate(texture); }
        }
    }
}
