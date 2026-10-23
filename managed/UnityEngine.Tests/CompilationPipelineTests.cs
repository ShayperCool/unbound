using UnityEditor.Compilation;
using Xunit;

namespace UnityEngine.Tests
{
    public class CompilationPipelineTests
    {
        [Fact]
        public void DefineConstraintsNeedEveryConstraintAndAnyAlternative()
        {
            var defines = new[] { "UNITY_EDITOR", "UNITY_ANDROID" };
            Assert.True(CompilationPipeline.IsDefineConstraintsCompatible(defines, new string[0]));
            Assert.True(CompilationPipeline.IsDefineConstraintsCompatible(defines, new[] { "UNITY_EDITOR" }));
            Assert.False(CompilationPipeline.IsDefineConstraintsCompatible(defines, new[] { "UNITY_EDITOR", "UNITY_IOS" }));
            Assert.True(CompilationPipeline.IsDefineConstraintsCompatible(defines, new[] { "UNITY_IOS || UNITY_ANDROID" }));
            Assert.True(CompilationPipeline.IsDefineConstraintsCompatible(defines, new[] { "!UNITY_IOS" }));
            Assert.False(CompilationPipeline.IsDefineConstraintsCompatible(defines, new[] { "!UNITY_EDITOR" }));
        }
    }
}
