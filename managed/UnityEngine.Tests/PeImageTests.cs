using System;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using Unbound.Runtime;
using Xunit;

namespace Unbound.Tests
{
    /// <summary>Loads unload their contexts (see <see cref="UnityEngine.Tests.CollectibleContexts"/>).</summary>
    [Collection(UnityEngine.Tests.CollectibleContexts.Name)]
    public class PeImageTests
    {
        /// <summary>An IL-only PE32+ assembly built for one 64-bit CPU (PlatformTarget x64 or ARM64).</summary>
        static byte[] IlOnlyImage(Machine machine)
        {
            var metadata = new MetadataBuilder();
            metadata.AddModule(0, metadata.GetOrAddString("Targeted.dll"), metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
            metadata.AddAssembly(metadata.GetOrAddString("Targeted" + machine), new Version(1, 0), default, default, default, default);
            metadata.AddTypeDefinition(TypeAttributes.NotPublic, default, metadata.GetOrAddString("<Module>"), default,
                MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
            var image = new BlobBuilder();
            new ManagedPEBuilder(new PEHeaderBuilder(machine: machine, imageCharacteristics: Characteristics.Dll),
                new MetadataRootBuilder(metadata), new BlobBuilder()).Serialize(image);
            return image.ToArray();
        }

        /// <summary>Whether the image loads in a collectible context, which is collected again.</summary>
        static bool Loads(byte[] image)
        {
            bool loaded = false;
            UnityEngine.Tests.CollectibleContexts.Use("PeImageTests",
                context => loaded = context.LoadFromStream(new MemoryStream(image)) != null);
            return loaded;
        }

        [Fact]
        public void AssembliesBuiltForAnother64BitCpuLoad()
        {
            var other = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? Machine.Amd64 : Machine.Arm64;
            var image = IlOnlyImage(other);
            Assert.Throws<FileLoadException>(() => Loads(image));
            Assert.True(Loads(PeImage.MakePortable(image)));
        }

        [Fact]
        public void AssembliesBuiltForThisCpuStayAsTheyAre()
        {
            var own = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? Machine.Arm64 : Machine.Amd64;
            var image = IlOnlyImage(own);
            Assert.Same(image, PeImage.MakePortable(image));
        }
    }
}
