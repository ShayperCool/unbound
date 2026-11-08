using System;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Unbound.Runtime;
using Xunit;

namespace Unbound.Tests
{
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

        static Assembly Load(byte[] image)
        {
            var context = new AssemblyLoadContext("PeImageTests", isCollectible: true);
            try
            {
                return context.LoadFromStream(new MemoryStream(image));
            }
            finally
            {
                context.Unload();
            }
        }

        [Fact]
        public void AssembliesBuiltForAnother64BitCpuLoad()
        {
            var other = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? Machine.Amd64 : Machine.Arm64;
            var image = IlOnlyImage(other);
            Assert.Throws<FileLoadException>(() => Load(image));
            Assert.NotNull(Load(PeImage.MakePortable(image)));
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
