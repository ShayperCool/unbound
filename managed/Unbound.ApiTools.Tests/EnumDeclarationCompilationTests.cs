using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Unbound.ApiTools.Tests;

public class EnumDeclarationCompilationTests
{
    [Theory]
    [InlineData("UnityEditor.BuildTarget.MetroPlayer", "CS0619", "Use WSAPlayer instead (UnityUpgradable) -> WSAPlayer", DiagnosticSeverity.Error)]
    [InlineData("UnityEditor.BuildTarget.BB10", "CS0618", "BlackBerry has been removed in 5.4", DiagnosticSeverity.Warning)]
    public void RemovedAndDeprecatedEnumFieldsProduceTheReleaseDiagnostic(string expression, string id,
        string message, DiagnosticSeverity severity)
    {
        var diagnostic = Assert.Single(TestAssemblies.EngineDiagnostics("public class Consumer { public object Value => " + expression + "; }"));
        Assert.Equal(id, diagnostic.Id);
        Assert.Equal(severity, diagnostic.Severity);
        Assert.Contains(message, diagnostic.GetMessage(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void RemovedEnumTypeIsAnError()
    {
        var diagnostic = Assert.Single(TestAssemblies.EngineDiagnostics("public class Consumer { public UnityEngine.BlendWeights Value { get; set; } }"));
        Assert.Equal("CS0619", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("BlendWeights is obsolete. Use SkinWeights instead", diagnostic.GetMessage(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void SupportedEnumFieldStillCompiles()
    {
        Assert.Empty(TestAssemblies.EngineDiagnostics("public class Consumer { public object Value => UnityEngine.RuntimePlatform.WindowsPlayer; }"));
    }
}
