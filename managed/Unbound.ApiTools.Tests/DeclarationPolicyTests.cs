using System;
using System.IO;
using Xunit;

namespace Unbound.ApiTools.Tests;

public class DeclarationPolicyTests
{
    [Theory]
    [InlineData("UnityEngine.NativeClassAttribute", "[UnityEngine.NativeClass(\"Thing\")]")]
    [InlineData("System.Security.SecuritySafeCriticalAttribute", "[System.Security.SecuritySafeCritical]")]
    [InlineData("System.Security.SecurityCriticalAttribute", "[System.Security.SecurityCritical]")]
    [InlineData("UnityEngineInternal.TypeInferenceRuleAttribute", "[UnityEngineInternal.TypeInferenceRule(1)]")]
    [InlineData("System.Diagnostics.DebuggerDisplayAttribute", "[System.Diagnostics.DebuggerDisplay(\"Value\")]")]
    [InlineData("System.Diagnostics.DebuggerTypeProxyAttribute", "[System.Diagnostics.DebuggerTypeProxy(typeof(object))]")]
    [InlineData("Unity.IL2CPP.CompilerServices.Il2CppEagerStaticClassConstructionAttribute", "[Unity.IL2CPP.CompilerServices.Il2CppEagerStaticClassConstruction]")]
    [InlineData("System.ComponentModel.EditorBrowsableAttribute", "[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]")]
    [InlineData("Unity.Profiling.IgnoredByDeepProfilerAttribute", "[Unity.Profiling.IgnoredByDeepProfiler]")]
    [InlineData("UnityEngine.ExtensionOfNativeClassAttribute", "[UnityEngine.ExtensionOfNativeClass]")]
    [InlineData("Unity.Burst.BurstAuthorizedExternalMethodAttribute", "[Unity.Burst.BurstAuthorizedExternalMethod]")]
    public void ToolingPolicyLeavesUnknownObsoleteAndConditionalDifferencesVisible(string name, string annotation)
    {
        const string declarations = """
            using System;
            namespace UnityEngine {
                public class NativeClassAttribute : Attribute { public NativeClassAttribute(string name) {} }
                public class ExtensionOfNativeClassAttribute : Attribute {}
                public class Gameplay : Attribute { public Gameplay(string value) {} }
                PRESENTATION public class Thing {
                    public void Tooling() {}
                    [Gameplay("expected")] public void Game() {}
                    [Obsolete("expected", true)] public void Removed() {}
                    [System.Diagnostics.Conditional("EXPECTED")] public void Conditional() {}
                }
            }
            namespace UnityEngineInternal { public class TypeInferenceRuleAttribute : Attribute { public TypeInferenceRuleAttribute(int value) {} } }
            namespace Unity.IL2CPP.CompilerServices { public class Il2CppEagerStaticClassConstructionAttribute : Attribute {} }
            namespace Unity.Profiling { public class IgnoredByDeepProfilerAttribute : Attribute {} }
            namespace Unity.Burst { public class BurstAuthorizedExternalMethodAttribute : Attribute {} }
            """;
        var root = TestAssemblies.TempDir();
        try
        {
            var unity = Path.Combine(root, "Managed", "UnityEngine");
            string components = typeof(System.ComponentModel.EditorBrowsableAttribute).Assembly.Location;
            TestAssemblies.Compile(unity, "UnityEngine.CoreModule", declarations.Replace("PRESENTATION", annotation), components);
            var same = TestAssemblies.Compile(Path.Combine(root, "same"), "UnityEngine.CoreModule", declarations.Replace("PRESENTATION", ""), components);
            Assert.Empty(ApiChecker.Check(unity, same).Errors);
            var changed = declarations.Replace("PRESENTATION", "")
                .Replace("[Gameplay(\"expected\")]", "")
                .Replace("[Obsolete(\"expected\", true)]", "[Obsolete(\"wrong\", false)]")
                .Replace("Conditional(\"EXPECTED\")", "Conditional(\"WRONG\")");
            var impl = TestAssemblies.Compile(Path.Combine(root, "changed"), "UnityEngine.CoreModule", changed, components);
            var report = ApiChecker.Check(unity, impl);
            Assert.Equal(3, report.Errors.Count);
            Assert.All(report.Errors, row => Assert.Equal("attributes", row.Category));
            Assert.Contains(report.Errors, row => row.Message.Contains("Gameplay"));
            Assert.Contains(report.Errors, row => row.Message.Contains("ObsoleteAttribute"));
            Assert.Contains(report.Errors, row => row.Message.Contains("ConditionalAttribute"));
            Assert.True(DeclarationMetadata.IgnoreAttribute(name));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("System.Runtime.CompilerServices.TupleElementNamesAttribute")]
    [InlineData("System.Reflection.DefaultMemberAttribute")]
    [InlineData("Unity.Collections.LowLevel.Unsafe.NativeContainerAttribute")]
    [InlineData("Unity.Collections.LowLevel.Unsafe.NativeDisableUnsafePtrRestrictionAttribute")]
    [InlineData("Unity.Jobs.LowLevel.Unsafe.JobProducerTypeAttribute")]
    [InlineData("Unity.Burst.BurstDiscardAttribute")]
    [InlineData("UnityEngine.UIElements.EventInterestAttribute")]
    [InlineData("UnityEngine.SerializeField")]
    public void CompilerJobSerializationAndUiContractsRemainChecked(string name) =>
        Assert.False(DeclarationMetadata.IgnoreAttribute(name));
}
