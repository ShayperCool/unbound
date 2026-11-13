using Microsoft.CodeAnalysis;
using Xunit;

namespace Unbound.ApiTools.Tests;

public class NullabilityDeclarationCompilationTests
{
    [Fact]
    public void ObjectTruthConversionNarrowsTheLiveReference()
    {
        Assert.Empty(TestAssemblies.EngineDiagnostics("""
            #nullable enable
            public class Consumer
            {
                public string Name(UnityEngine.Object? value)
                {
                    if (value) return value.name;
                    return "";
                }
            }
            """));
    }

    [Fact]
    public void FriendModuleNullArgumentsWarnWhileNonNullArgumentsCompile()
    {
        var diagnostics = TestAssemblies.EngineDiagnostics("""
            #nullable enable
            public class Consumer
            {
                public void Fill()
                {
                    Unity.Collections.CollectionExtensions.Fill<int>(null, 1, 3);
                    Unity.Collections.CollectionExtensions.ContainsByEquals<int>(null, 1);
                    Unity.Collections.CollectionExtensions.ContainsByEquals<int>(new int[] { 1 }, 1);
                }
            }
            """, "UnityEngine.UIElementsModule");
        Assert.Equal(2, diagnostics.Length);
        Assert.All(diagnostics, d =>
        {
            Assert.Equal("CS8625", d.Id);
            Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
        });
    }
}
