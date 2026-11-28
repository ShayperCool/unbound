using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Microsoft.CodeAnalysis;
using Mono.Cecil;
using Unbound.Host;
using Xunit;

namespace UnityEngine.Tests
{
    [Collection(CollectibleContexts.Name)]
    public sealed class FrameworkShimsTests
    {
        static byte[] Compile(string source) =>
            WideFloatsTests.Emit(WideFloatsTests.Compilation(source, OptimizationLevel.Release, "FrameworkShimProbe")).Image;

        [Fact]
        public void GenericPluginMethodsWithShimmedNamesStillLoadAndRun()
        {
            var image = Compile("""
                public sealed class Endpoint<TRequest, TResponse>
                {
                    public string GetName() => "endpoint";
                }

                public static class Probe
                {
                    public static string Run() => new Endpoint<int, string>().GetName();
                }
                """);

            var redirected = FrameworkShims.Redirect(image);
            Assert.Same(image, redirected);
            CollectibleContexts.Use(nameof(GenericPluginMethodsWithShimmedNamesStillLoadAndRun), context =>
            {
                var plugin = context.LoadFromStream(new MemoryStream(redirected));
                Assert.Equal("endpoint", plugin.GetType("Probe").GetMethod("Run").Invoke(null, null));
            });
        }

        [Fact]
        public void FrameworkOnlyInstanceOverloadStillRunsThroughItsShim()
        {
            var image = Compile("""
                using System.Reflection;
                using System.Reflection.Emit;

                public static class Probe
                {
                    public static AssemblyBuilder Run() =>
                        AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("ProbeDynamic"), AssemblyBuilderAccess.Run);
                }
                """);

            using var source = new MemoryStream(image);
            using var module = ModuleDefinition.ReadModule(source);
            var run = module.GetType("Probe").Methods.Single(m => m.Name == "Run");
            var call = run.Body.Instructions.Single(i => i.Operand is MethodReference method &&
                method.Name == nameof(AssemblyBuilder.DefineDynamicAssembly));
            var original = (MethodReference)call.Operand;
            var legacy = new MethodReference(original.Name, original.ReturnType, module.ImportReference(typeof(AppDomain)))
            {
                HasThis = true,
            };
            foreach (var parameter in original.Parameters)
                legacy.Parameters.Add(new ParameterDefinition(parameter.ParameterType));
            call.Operand = legacy;
            var currentDomain = typeof(AppDomain).GetProperty(nameof(AppDomain.CurrentDomain)).GetMethod;
            var il = run.Body.GetILProcessor();
            il.InsertBefore(run.Body.Instructions[0], il.Create(Mono.Cecil.Cil.OpCodes.Call, module.ImportReference(currentDomain)));

            using var emitted = new MemoryStream();
            module.Write(emitted);
            var redirected = FrameworkShims.Redirect(emitted.ToArray());
            CollectibleContexts.Use(nameof(FrameworkOnlyInstanceOverloadStillRunsThroughItsShim), context =>
            {
                var plugin = context.LoadFromStream(new MemoryStream(redirected));
                var result = Assert.IsAssignableFrom<AssemblyBuilder>(plugin.GetType("Probe").GetMethod("Run").Invoke(null, null));
                Assert.Equal("ProbeDynamic", result.GetName().Name);
            });
        }
    }
}
