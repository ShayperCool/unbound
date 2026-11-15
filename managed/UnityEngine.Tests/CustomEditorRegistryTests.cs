using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unbound.Runtime;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    public sealed class CustomEditorRegistryTests
    {
        class Target : ScriptableObject { }
        class Child : Target { }
        class GenericTarget<T> : ScriptableObject { }
        [CustomEditor(typeof(Target), true), CanEditMultipleObjects]
        class Inspector : Editor { }
        [CustomEditor(typeof(Child), isFallback = true)]
        class Fallback : Editor { }
        [CustomEditor(typeof(GenericTarget<>))]
        class GenericInspector : Editor { }

        [Fact]
        public void RegistryFollowsAssemblyChangesAndRebuildsAfterDomainReload()
        {
            var assemblies = (List<Assembly>)typeof(GameDomain).GetField("s_Assemblies", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            var generation = typeof(GameDomain).GetField("s_Generation", BindingFlags.Static | BindingFlags.NonPublic);
            var original = assemblies.ToArray();
            int oldGeneration = (int)generation.GetValue(null);
            try
            {
                assemblies.Clear();
                generation.SetValue(null, oldGeneration + 1);
                Assert.Null(CustomEditorAttributes.FindCustomEditorTypeByType(typeof(Target), false));
                assemblies.Add(typeof(CustomEditorRegistryTests).Assembly);
                Assert.Equal(typeof(Inspector), CustomEditorAttributes.FindCustomEditorTypeByType(typeof(Target), false));
                Assert.Equal(typeof(Inspector), CustomEditorAttributes.FindCustomEditorTypeByType(typeof(Child), false));
                Assert.Equal(typeof(Inspector), CustomEditorAttributes.FindCustomEditorTypeByType(typeof(Child), true));
                Assert.Equal(typeof(GenericInspector), CustomEditorAttributes.FindCustomEditorTypeByType(typeof(GenericTarget<int>), false));
                Assert.Null(CustomEditorAttributes.FindCustomEditorTypeByType(typeof(GenericTarget<int>), true));
                assemblies[0] = typeof(string).Assembly;
                generation.SetValue(null, oldGeneration + 2);
                Assert.Null(CustomEditorAttributes.FindCustomEditorTypeByType(typeof(Target), false));
            }
            finally
            {
                assemblies.Clear(); assemblies.AddRange(original);
                generation.SetValue(null, oldGeneration);
                CustomEditorAttributes.Rebuild();
            }
        }
    }
}
