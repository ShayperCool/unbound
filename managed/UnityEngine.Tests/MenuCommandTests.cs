using System.Collections.Generic;
using System.Reflection;
using Unbound.Runtime;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// MenuCommand and menu methods that take one, as the Unity 6000.0.71f1 Editor runs them by path
    /// (tests/projects/menucommand): the command and its validator each get a new MenuCommand with
    /// no context and user data 0, a validator answering false keeps the command from running, and
    /// a method with another parameter is not run (a control outside the measured signatures).
    /// </summary>
    public sealed class MenuCommandTests
    {
        const string CommandPath = "Tools/Menu command/With command";
        const string ValidatedPath = "Tools/Menu command/Validated";
        const string OtherPath = "Tools/Menu command/Other parameter";
        static readonly List<string> s_Calls = new List<string>();
        static bool s_Allow;

        static string Describe(MenuCommand c) => c == null ? "<null>" : $"{(c.context == null ? "<null>" : c.context.name)}/{c.userData}";

        [MenuItem(CommandPath)] static void WithCommand(MenuCommand command) => s_Calls.Add("run " + Describe(command));
        [MenuItem(ValidatedPath)] static void Validated(MenuCommand command) => s_Calls.Add("run " + Describe(command));
        [MenuItem(ValidatedPath, true)] static bool ValidateValidated(MenuCommand command) { s_Calls.Add("validate " + Describe(command)); return s_Allow; }
        [MenuItem(OtherPath)] static void OtherParameter(int value) => s_Calls.Add("other " + value);

        [Fact]
        public void TheConstructorsKeepTheContextAndUserData()
        {
            var target = new GameObject("Target");
            try
            {
                Assert.Equal("Target/7", Describe(new MenuCommand(target, 7)));
                Assert.Equal("Target/0", Describe(new MenuCommand(target)));
                Assert.Equal("<null>/0", Describe(new MenuCommand(null)));
                Assert.Equal("Target/2", Describe(new MenuCommand(null, 1) { context = target, userData = 2 }));
            }
            finally { Object.DestroyImmediate(target); }
        }

        [Fact]
        public void CommandsRunByPathGetAnEmptyCommand()
        {
            using var engine = MainThreadCheck.Engine();
            var assemblies = (List<Assembly>)typeof(GameDomain).GetField("s_Assemblies", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            var generation = typeof(GameDomain).GetField("s_Generation", BindingFlags.Static | BindingFlags.NonPublic);
            var original = assemblies.ToArray(); int oldGeneration = (int)generation.GetValue(null);
            try
            {
                assemblies.Clear(); assemblies.Add(typeof(MenuCommandTests).Assembly);
                generation.SetValue(null, oldGeneration + 1);
                s_Calls.Clear();
                Assert.True(EditorApplication.ExecuteMenuItem(CommandPath));
                Assert.True(Menu.GetEnabled(CommandPath));
                Assert.Equal(new[] { "run <null>/0" }, s_Calls);

                s_Calls.Clear();
                s_Allow = true;
                Assert.True(EditorApplication.ExecuteMenuItem(ValidatedPath));
                Assert.Equal(new[] { "validate <null>/0", "run <null>/0" }, s_Calls);
                s_Calls.Clear();
                s_Allow = false;
                Assert.False(Menu.GetEnabled(ValidatedPath));
                Assert.False(EditorApplication.ExecuteMenuItem(ValidatedPath));
                Assert.Equal(new[] { "validate <null>/0", "validate <null>/0" }, s_Calls);

                // Control: another parameter type is not a signature the menu runs.
                s_Calls.Clear();
                Assert.False(Menu.GetEnabled(OtherPath));
                Assert.False(EditorApplication.ExecuteMenuItem(OtherPath));
                Assert.Empty(s_Calls);
            }
            finally
            {
                assemblies.Clear(); assemblies.AddRange(original); generation.SetValue(null, oldGeneration);
            }
        }
    }
}
