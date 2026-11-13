using Xunit;

namespace Unbound.ApiTools.Tests;

public class CoreModifierCompilationTests
{
    [Fact]
    public void TextureGraphicsFormatCanBeOverridden()
    {
        Assert.Empty(TestAssemblies.EngineDiagnostics("public class Consumer : UnityEngine.Texture { public override UnityEngine.Experimental.Rendering.GraphicsFormat graphicsFormat => UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm; }"));
    }

    [Fact]
    public void HiddenInspectorAnnotationRejectsDerivation()
    {
        Assert.Contains(TestAssemblies.EngineDiagnostics("public class Consumer : UnityEngine.HideInInspector { }"), diagnostic => diagnostic.Id == "CS0509");
    }

    [Fact]
    public void PreferenceErrorsAllowDerivation()
    {
        Assert.Empty(TestAssemblies.EngineDiagnostics("public class Consumer : UnityEngine.PlayerPrefsException { public Consumer() : base(\"data\") { } }"));
    }
}
