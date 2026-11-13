using Microsoft.CodeAnalysis;
using Xunit;

namespace Unbound.ApiTools.Tests;

public class AccessibilityCompilationTests
{
    [Fact]
    public void InternalSystemLimitIsUnavailableToGameCode()
    {
        Assert.Contains(TestAssemblies.EngineDiagnostics("public class Consumer { public int Value => UnityEngine.SystemInfo.maxRenderTextureSize; }"), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void PublicTextureLimitRemainsAvailable()
    {
        Assert.Empty(TestAssemblies.EngineDiagnostics("public class Consumer { public int Value => UnityEngine.SystemInfo.maxTextureSize; }"));
    }

    [Fact]
    public void ProtectedTooltipIsAvailableToDerivedCode()
    {
        Assert.Empty(TestAssemblies.EngineDiagnostics("public class Consumer : UnityEngine.GUI { public string Value => mouseTooltip; }"));
    }
}
