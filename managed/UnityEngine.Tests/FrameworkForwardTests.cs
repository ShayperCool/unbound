using System;
using System.IO;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// .NET Framework types game and Editor code reach through .NET's facades, which forward them
    /// to packages the shared framework lacks (Unbound.Host ships them).
    /// </summary>
    public class FrameworkForwardTests
    {
        /// <summary>Editor-only code compiled against .NET Framework names CodeDom in System.dll
        /// (a visual scripting package's Editor assembly): it resolves and works, as on Mono.</summary>
        [Fact]
        public void CodeDomTypesOfSystemResolve()
        {
            // The host's dependencies (which these tests inherit) carry the package: the runner
            // would also find a stale copy in its folder, the host only what its dependencies list.
            var dependencies = File.ReadAllText(Path.ChangeExtension(typeof(FrameworkForwardTests).Assembly.Location, ".deps.json"));
            Assert.Contains("\"System.CodeDom/", dependencies);
            var provider = Type.GetType("Microsoft.CSharp.CSharpCodeProvider, System", throwOnError: true)!;
            var instance = Activator.CreateInstance(provider)!;
            Assert.Equal("cs", provider.GetProperty("FileExtension")!.GetValue(instance));
            var reference = Type.GetType("System.CodeDom.CodeTypeReference, System", throwOnError: true)!;
            var typeReference = Activator.CreateInstance(reference, typeof(int))!;
            Assert.Equal("System.Int32", reference.GetProperty("BaseType")!.GetValue(typeReference));
        }
    }
}
