using System;
using System.IO;
using System.Linq;
using Unbound.Compiler;
using Xunit;

namespace Unbound.ApiTools.Tests;

public class MessageCoverageTests
{
    [Fact]
    public void UsesHostInheritanceDefinesAndCachedCompilations()
    {
        var root = TestAssemblies.TempDir();
        try
        {
            var engine = TestAssemblies.Compile(root, "UnityEngine.CoreModule", """
                namespace UnityEngine {
                    public class MonoBehaviour { }
                    public class StateMachineBehaviour { public virtual void OnStateIK() {} }
                }
                """);
            var source = Path.Combine(root, "Game.cs");
            File.WriteAllText(source, """
                class Ordinary { void OnStateIK() {} }
                class WrongHost : UnityEngine.MonoBehaviour { void OnStateIK() {} }
                partial class Base<T> : UnityEngine.StateMachineBehaviour {
                    public override void OnStateIK() {}
                }
                class Derived : Base<int> { }
                class StaticMethod : UnityEngine.StateMachineBehaviour { static void OnStateIK(int x) {} }
                #if EXCLUDED
                class Excluded : UnityEngine.StateMachineBehaviour { public override void OnStateIK() {} }
                #endif
                """);
            var request = new CompileRequest { CacheDir = Path.Combine(root, "cache"), LanguageVersion = "latest" };
            request.BclReferences.AddRange(((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator));
            request.RuntimeEngineReferences.Add(engine);
            request.Assemblies.Add(new AssemblySpec { Name = "Game", Sources = { source } });
            var rules = new[] { ("UnityEngine.StateMachineBehaviour", "OnStateIK") };
            // New services force both the initial compilation and a cold content-cache hit.
            foreach (var service in new[] { new CompilerService(), new CompilerService() })
            {
                for (int repeat = 0; repeat < 2; repeat++)
                {
                    var report = service.FindUnsupportedMessages(request, rules);
                    Assert.Empty(report.FailedAssemblies);
                    var message = Assert.Single(report.Messages);
                    Assert.Equal(source, message.File);
                    Assert.Equal("Base<T>", message.Type);
                    Assert.Equal("OnStateIK", message.Message);
                    Assert.True(message.Line > 0);
                    Assert.Empty(service.FindUnsupportedMessages(request, Array.Empty<(string, string)>()).Messages);
                }
            }
            File.WriteAllText(source, "this does not compile");
            Assert.Equal(new[] { "Game" }, new CompilerService().FindUnsupportedMessages(request, rules).FailedAssemblies);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
