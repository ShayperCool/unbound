using Xunit;

namespace Unbound.ApiTools.Tests;

public class ParameterDeclarationCompilationTests
{
    [Fact]
    public void ArrayClearAcceptsItsNamedRequiredArguments()
    {
        Assert.Empty(TestAssemblies.EngineDiagnostics("public class Consumer { public void Clear(UnityEngine.Rendering.CommandBuffer buffer) { buffer.ClearRenderTarget(clearFlags: UnityEngine.Rendering.RTClearFlags.Color, backgroundColors: new UnityEngine.Color[1]); } }"));
    }

    [Fact]
    public void TargetConstructorAcceptsAnOmittedMipLevelAndDepthSlice()
    {
        Assert.Empty(TestAssemblies.EngineDiagnostics("public class Consumer { public object Target => new UnityEngine.Rendering.RenderTargetIdentifier(UnityEngine.Rendering.BuiltinRenderTextureType.CameraTarget, cubeFace: UnityEngine.CubemapFace.PositiveX); }"));
    }
}
