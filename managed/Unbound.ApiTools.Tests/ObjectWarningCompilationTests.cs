using System.Globalization;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Unbound.ApiTools.Tests;

public class ObjectWarningCompilationTests
{
    [Fact]
    public void LegacyTypeLookupsWarnWhileTheModernLookupCompiles()
    {
        var diagnostics = TestAssemblies.EngineDiagnostics("""
            public class Consumer
            {
                public void Find()
                {
                    UnityEngine.Object.FindObjectsOfType(typeof(UnityEngine.GameObject), true);
                    UnityEngine.Object.FindObjectOfType(typeof(UnityEngine.GameObject));
                    UnityEngine.Object.FindObjectOfType(typeof(UnityEngine.GameObject), false);
                    UnityEngine.Object.FindFirstObjectByType(typeof(UnityEngine.GameObject));
                }
            }
            """);
        Assert.Equal(3, diagnostics.Length);
        Assert.All(diagnostics, d =>
        {
            Assert.Equal("CS0618", d.Id);
            Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
            Assert.Contains("has been deprecated", d.GetMessage(CultureInfo.InvariantCulture));
        });
    }
}
