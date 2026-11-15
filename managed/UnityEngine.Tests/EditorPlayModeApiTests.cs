using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unbound.Runtime;
using UnityEditor;
using UnityEngine.Events;
using Xunit;

namespace UnityEngine.Tests
{
    public sealed class EditorPlayModeApiTests
    {
        [Fact]
        public void TypeNamesUseTheWholeMeasuredTableAndRejectAnotherRelease()
        {
            string release = ProjectInfo.UnityVersion;
            try
            {
                ProjectInfo.UnityVersion = "6000.0.71f1";
                foreach (var entry in ClassCoverage.UnityTable(ProjectInfo.UnityVersion, exact: true))
                {
                    var type = UnityEditor.UnityType.FindTypeByName(entry.Value.Name);
                    Assert.Equal(entry.Key, type.persistentTypeID);
                    Assert.Equal(entry.Value.Name, type.name);
                    Assert.Same(type, UnityEditor.UnityType.FindTypeByName(entry.Value.Name));
                }
                Assert.Null(UnityEditor.UnityType.FindTypeByName("gameobject"));
                Assert.Equal("key", Assert.Throws<ArgumentNullException>(() => UnityEditor.UnityType.FindTypeByName(null)).ParamName);
                ProjectInfo.UnityVersion = "6000.0.70f1";
                Assert.NotNull(ClassCoverage.UnityTable(ProjectInfo.UnityVersion));
                Assert.Throws<NotSupportedException>(() => UnityEditor.UnityType.FindTypeByName("GameObject"));
            }
            finally { ProjectInfo.UnityVersion = release; }
        }

        static int s_MenuCalls;
        const string MenuPath = "Tools/Unbound managed menu control";
        [MenuItem(MenuPath + " _g")]
        static void MenuControl() { s_MenuCalls++; }

        [Fact]
        public void MenuDiscoveryAndTypeCacheFollowChangesToTheCurrentAssemblyList()
        {
            using var engine = MainThreadCheck.Engine();
            var assemblies = (List<Assembly>)typeof(GameDomain).GetField("s_Assemblies", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            var generation = typeof(GameDomain).GetField("s_Generation", BindingFlags.Static | BindingFlags.NonPublic);
            var original = assemblies.ToArray();
            int oldGeneration = (int)generation.GetValue(null);
            try
            {
                assemblies.Clear();
                generation.SetValue(null, oldGeneration + 1);
                Assert.False(Menu.GetChecked(MenuPath));
                Assert.DoesNotContain(TypeCache.GetMethodsWithAttribute<MenuItem>(), method => method.DeclaringType == typeof(EditorPlayModeApiTests));
                assemblies.Add(typeof(EditorPlayModeApiTests).Assembly);
                Menu.SetChecked(MenuPath, true);
                Assert.True(Menu.GetChecked(MenuPath));
                int before = s_MenuCalls;
                Assert.True(EditorApplication.ExecuteMenuItem(MenuPath));
                Assert.Equal(before + 1, s_MenuCalls);
                // Replace the assembly without changing the count or list identity.
                assemblies[0] = typeof(string).Assembly;
                generation.SetValue(null, oldGeneration + 2);
                Assert.False(Menu.GetChecked(MenuPath));
                Assert.DoesNotContain(TypeCache.GetMethodsWithAttribute<MenuItem>(), method => method.DeclaringType == typeof(EditorPlayModeApiTests));
            }
            finally
            {
                assemblies.Clear(); assemblies.AddRange(original);
                generation.SetValue(null, oldGeneration);
            }
        }

        [Fact]
        public void NullRemovalValidatesBeforeVisitingAnEmptyEventAcrossArities()
        {
            Assert.Throws<NullReferenceException>(() => new UnityEvent().RemoveListener(null));
            Assert.Throws<NullReferenceException>(() => new UnityEvent<string, int>().RemoveListener(null));
        }

        [Fact]
        public void IdleEditorSubscriptionsKeepMultiplicityAndSupportRemoval()
        {
            Action callback = () => { };
            var field = typeof(Lightmapping).GetField("bakeCompleted", BindingFlags.Static | BindingFlags.NonPublic);
            var before = (Delegate)field.GetValue(null);
            int original = before?.GetInvocationList().Length ?? 0;
            Lightmapping.bakeCompleted += callback;
            Lightmapping.bakeCompleted += callback;
            try
            {
                Assert.Equal(original + 2, ((Delegate)field.GetValue(null)).GetInvocationList().Length);
                Lightmapping.bakeCompleted -= callback;
                Assert.Equal(original + 1, ((Delegate)field.GetValue(null)).GetInvocationList().Length);
            }
            finally
            {
                Lightmapping.bakeCompleted -= callback;
                Assert.Equal(original, ((Delegate)field.GetValue(null))?.GetInvocationList().Length ?? 0);
            }
        }
    }
}
