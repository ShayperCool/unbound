using System.Globalization;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Unbound.ApiTools.Tests;

public class MemberDeclarationCompilationTests
{
    [Fact]
    public void RemovedClothPropertyIsAnError()
    {
        var diagnostic = Assert.Single(TestAssemblies.EngineDiagnostics("public class Consumer { public bool Value(UnityEngine.Cloth cloth) => cloth.selfCollision; }"));
        Assert.Equal("CS0619", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Deprecated.Cloth.selfCollisions is no longer supported since Unity 5.0.", diagnostic.GetMessage(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void StringAddComponentUsesTheReleaseErrorMessage()
    {
        var diagnostic = Assert.Single(TestAssemblies.EngineDiagnostics("public class Consumer { public UnityEngine.Component Add(UnityEngine.GameObject owner) => owner.AddComponent(\"BoxCollider\"); }"));
        Assert.Equal("CS0619", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("GameObject.AddComponent with string argument has been deprecated. Use GameObject.AddComponent<T>() instead. (UnityUpgradable).", diagnostic.GetMessage(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void TypeAddComponentStillCompiles()
    {
        Assert.Empty(TestAssemblies.EngineDiagnostics("public class Consumer { public UnityEngine.Component Add(UnityEngine.GameObject owner) => owner.AddComponent(typeof(UnityEngine.BoxCollider)); }"));
    }

    [Fact]
    public void XrSetterProducesTheReleaseWarning()
    {
        var diagnostic = Assert.Single(TestAssemblies.EngineDiagnostics("public class Consumer { public void Set() { UnityEngine.XR.XRSettings.enabled = true; } }"));
        Assert.Equal("CS0618", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("XRSettings.enabled{set;} is deprecated and should no longer be used. Instead, call Start() and Stop() on an XRDisplaySubystem instance.", diagnostic.GetMessage(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void XrGetterStillCompiles()
    {
        Assert.Empty(TestAssemblies.EngineDiagnostics("public class Consumer { public bool Value => UnityEngine.XR.XRSettings.enabled; }"));
    }
}
