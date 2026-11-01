using System;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Unbound.Runtime;
using UnityEngine;
using Xunit;
using Object = UnityEngine.Object;

namespace Unbound.Tests
{
    public sealed class ModuleActivationObserver : MonoBehaviour
    {
        public int Awakes;
        public bool SawCollider;
        void Awake() { Awakes++; SawCollider = GetComponent<BoxCollider>() != null; }
    }

    public class DisabledModulesTests : IDisposable
    {
        readonly string m_Module = Path.GetTempFileName();
        readonly bool m_Playing = EditorSession.IsPlaying;

        public DisabledModulesTests()
        {
            var metadata = new MetadataBuilder();
            metadata.AddModule(0, metadata.GetOrAddString("disabled.dll"), metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
            metadata.AddAssembly(metadata.GetOrAddString("DisabledFixture"), new Version(1, 0), default, default, default, default);
            metadata.AddTypeDefinition(TypeAttributes.NotPublic, default, metadata.GetOrAddString("<Module>"), default,
                MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
            metadata.AddTypeDefinition(TypeAttributes.Public, metadata.GetOrAddString("UnityEngine"), metadata.GetOrAddString("BoxCollider"), default,
                MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
            var image = new BlobBuilder();
            new ManagedPEBuilder(new PEHeaderBuilder(imageCharacteristics: Characteristics.Dll),
                new MetadataRootBuilder(metadata), new BlobBuilder()).Serialize(image);
            File.WriteAllBytes(m_Module, image.ToArray());
            DisabledModules.Configure(new[] { m_Module });
        }

        public void Dispose()
        {
            DisabledModules.Configure(Array.Empty<string>());
            EditorSession.IsPlaying = m_Playing;
            File.Delete(m_Module);
        }

        static Type ScriptWithEngineTypeName()
        {
            var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("UserScript" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.Run);
            var type = assembly.DefineDynamicModule("User").DefineType("UnityEngine.BoxCollider", TypeAttributes.Public, typeof(MonoBehaviour));
            type.DefineDefaultConstructor(MethodAttributes.Public);
            return type.CreateType();
        }

        [Fact]
        public void DisabledTypeNamesDoNotMatchUserAssemblyTypes()
        {
            Assert.True(DisabledModules.Contains(typeof(BoxCollider)));
            Assert.False(DisabledModules.Contains(ScriptWithEngineTypeName()));
            Assert.False(DisabledModules.Contains(typeof(Transform)));
        }

        [Fact]
        public void ExistingEditModeComponentsArePrunedBeforeActivation()
        {
            var go = new GameObject("Disabled module activation");
            try
            {
                go.SetActive(false);
                EditorSession.IsPlaying = false;
                var engineCollider = go.AddComponent<BoxCollider>();
                var userType = ScriptWithEngineTypeName();
                var userComponent = go.AddComponent(userType);
                var observer = go.AddComponent<ModuleActivationObserver>();
                Assert.False(DisabledModules.Contains(userType));
                DisabledModules.Prune(go.transform);
                Assert.True(engineCollider == null);
                Assert.Same(userComponent, go.GetComponent(userType));
                Assert.Equal(0, observer.Awakes);
                EditorSession.IsPlaying = true;
                go.SetActive(true);
                Assert.Equal(1, observer.Awakes);
                Assert.False(observer.SawCollider);
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
