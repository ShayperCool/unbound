using System.Linq;
using Xunit;

namespace Unbound.ApiTools.Tests;

public class EditorShapeCompilationTests
{
    [Fact]
    public void AssetDatabaseRejectsDerivation()
    {
        var errors = TestAssemblies.EngineDiagnostics("public class Consumer : UnityEditor.AssetDatabase { }");
        Assert.Contains(errors, diagnostic => diagnostic.Id == "CS0509");
    }

    [Fact]
    public void PlatformSettingsStillAllowDerivation()
    {
        Assert.Empty(TestAssemblies.EngineDiagnostics("public class Consumer : UnityEditor.PlayerSettings.Android { }"));
    }
}
