using System;
using System.IO;
using Unbound.Runtime;
using Xunit;

namespace Unbound.Tests
{
    public sealed class StackSourcePathTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ACompilerPathUnderASymlinkedAncestorMapsToTheProject(bool nestedTarget)
        {
            string root = Path.Combine(Path.GetTempPath(), "unbound-stack-" + Guid.NewGuid().ToString("N"));
            string physical = Path.Combine(root, "physical");
            string project = Path.Combine(physical, "project");
            Directory.CreateDirectory(project);
            try
            {
                string link = Path.Combine(root, "logical");
                string target = physical;
                if (nestedTarget)
                {
                    string via = Path.Combine(root, "via");
                    Directory.CreateSymbolicLink(via, root);
                    target = Path.Combine(via, "physical");
                }
                Directory.CreateSymbolicLink(link, target);
                string source = Path.Combine(project, "Assets", "Script.cs");
                Assert.Equal("Assets/Script.cs", UnityStackTrace.SourcePath(source, Path.Combine(link, "project")));
                Assert.Equal("Assets/Script.cs", UnityStackTrace.SourcePath(source, project));
                string neighbor = Path.Combine(physical, "project-other", "Assets", "Script.cs");
                Assert.Equal(neighbor.Replace('\\', '/'), UnityStackTrace.SourcePath(neighbor, project));
                Assert.Equal(source.Replace('\\', '/'), UnityStackTrace.SourcePath(source, ""));
                Assert.Equal(source.Replace('\\', '/'), UnityStackTrace.SourcePath(source, "Assets/"));
            }
            finally { Directory.Delete(root, true); }
        }
        [Theory]
        [InlineData("Assets")]
        [InlineData("Packages")]
        public void AProjectViewMapsItsLinkedSourceRoots(string sourceRoot)
        {
            string root = Path.Combine(Path.GetTempPath(), "unbound-stack-" + Guid.NewGuid().ToString("N"));
            string authored = Path.Combine(root, "authored");
            string view = Path.Combine(root, "view");
            Directory.CreateDirectory(Path.Combine(authored, sourceRoot));
            Directory.CreateDirectory(view);
            try
            {
                Directory.CreateSymbolicLink(Path.Combine(view, sourceRoot), Path.Combine(authored, sourceRoot));
                string source = Path.Combine(authored, sourceRoot, "Script.cs");
                Assert.Equal(sourceRoot + "/Script.cs", UnityStackTrace.SourcePath(source, view));
                Assert.Equal(sourceRoot + "/Script.cs", UnityStackTrace.SourcePath(source, view + Path.DirectorySeparatorChar));
                string other = Path.Combine(authored, sourceRoot + "-other", "Script.cs");
                Assert.Equal(other.Replace('\\', '/'), UnityStackTrace.SourcePath(other, view));
            }
            finally { Directory.Delete(root, true); }
        }
    }
}
