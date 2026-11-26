using System;
using System.Linq;
using System.Text;
using UnityEditor.ShortcutManagement;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The Editor's shortcut value types and attributes as Unity's own C# writes them
    /// (tests/projects/shortcuttypes records the OS-independent values): the platform-styled text
    /// and the action/control equality follow the Editor of the machine's OS.</summary>
    public sealed class ShortcutTypesTests
    {
        static bool Mac => Application.platform == RuntimePlatform.OSXEditor;

        [Fact]
        public void UnsupportedKeysReadAsNoneAndHaveNoMenuString()
        {
            Assert.Equal(KeyCode.None, new KeyCombination(KeyCode.JoystickButton0).keyCode);
            Assert.Equal(KeyCode.None, new KeyCombination((KeyCode)999).keyCode);
            Assert.Equal(KeyCode.F16, new KeyCombination(KeyCode.F16).keyCode);
            Assert.Equal(KeyCode.F24, new KeyCombination(KeyCode.F24).keyCode);
            Assert.Equal("", new KeyCombination(KeyCode.JoystickButton0, ShortcutModifiers.Action).ToMenuShortcutString());
            Assert.Equal("None", new KeyCombination(KeyCode.JoystickButton0).ToString());
        }

        [Fact]
        public void MenuStringsNameTheModifiersThenTheKey()
        {
            Assert.Equal("_a", new KeyCombination(KeyCode.A).ToMenuShortcutString());
            Assert.Equal("&#%^a", new KeyCombination(KeyCode.A, (ShortcutModifiers)15).ToMenuShortcutString());
            Assert.Equal("%LEFT", new KeyCombination(KeyCode.LeftArrow, ShortcutModifiers.Action).ToMenuShortcutString());
            Assert.Equal("_M0", new KeyCombination(KeyCode.Mouse0).ToMenuShortcutString());
            // Keys without a character or menu name keep only the modifiers.
            Assert.Equal("%", new KeyCombination(KeyCode.Keypad0, ShortcutModifiers.Action).ToMenuShortcutString());
            Assert.Equal("_", new KeyCombination(KeyCode.LeftShift).ToMenuShortcutString());
            Assert.Throws<InvalidOperationException>(() => KeyCombination.SequenceToMenuString(new[] { new KeyCombination(KeyCode.A), new KeyCombination(KeyCode.B) }));
            Assert.Equal("", KeyCombination.SequenceToMenuString(new KeyCombination[0]));
        }

        [Theory]
        [InlineData("%a", true, KeyCode.A, ShortcutModifiers.Action)]
        [InlineData("#&k", true, KeyCode.K, ShortcutModifiers.Alt | ShortcutModifiers.Shift)]
        [InlineData("_g", true, KeyCode.G, ShortcutModifiers.None)]
        [InlineData("%left", true, KeyCode.LeftArrow, ShortcutModifiers.Action)]
        [InlineData("^#space", true, KeyCode.Space, ShortcutModifiers.Shift | ShortcutModifiers.Control)]
        [InlineData("%A", true, KeyCode.A, ShortcutModifiers.Action)]
        [InlineData("a", false, KeyCode.None, ShortcutModifiers.None)]
        [InlineData("%ab", false, KeyCode.None, ShortcutModifiers.None)]
        [InlineData("%F25", false, KeyCode.None, ShortcutModifiers.None)]
        [InlineData("_", false, KeyCode.None, ShortcutModifiers.None)]
        [InlineData("", false, KeyCode.None, ShortcutModifiers.None)]
        public void MenuStringsParseBack(string text, bool parsed, KeyCode key, ShortcutModifiers modifiers)
        {
            Assert.Equal(parsed, KeyCombination.TryParseMenuItemBindingString(text, out var combination));
            Assert.Equal(key, combination.keyCode);
            Assert.Equal(modifiers, combination.modifiers);
        }

        [Fact]
        public void TextFollowsTheEditorsPlatform()
        {
            var all = new KeyCombination(KeyCode.Return, ShortcutModifiers.Alt | ShortcutModifiers.Shift | ShortcutModifiers.Action | ShortcutModifiers.Control);
            Assert.Equal(Mac ? "⌥⇧⌘^↩" : "Ctrl+Alt+Shift+Enter", all.ToString());
            Assert.Equal(Mac ? "⌘←" : "Ctrl+Left Arrow", new KeyCombination(KeyCode.LeftArrow, ShortcutModifiers.Action).ToString());
            Assert.Equal("Num 0", new KeyCombination(KeyCode.Keypad0).ToString());
            Assert.Equal("Space", new KeyCombination(KeyCode.Space).ToString());
            var builder = new StringBuilder();
            KeyCombination.VisualizeModifiers(ShortcutModifiers.Control, builder);
            Assert.Equal(Mac ? "^" : "Ctrl+", builder.ToString());
        }

        [Fact]
        public void ActionAndControlCompareAsOneOutsideMacOS()
        {
            var action = new KeyCombination(KeyCode.S, ShortcutModifiers.Action);
            var control = new KeyCombination(KeyCode.S, ShortcutModifiers.Control);
            var both = new KeyCombination(KeyCode.S, ShortcutModifiers.Action | ShortcutModifiers.Control);
            Assert.Equal(!Mac, action.Equals(control));
            Assert.Equal(!Mac, action.Equals(both));
            Assert.False(action.Equals(new KeyCombination(KeyCode.S)));
            Assert.False(action.Equals(null));
            Assert.Equal((int)KeyCode.S * 397 ^ 2, action.GetHashCode());
        }

        [Fact]
        public void KeyboardInputMapsCommandAndControlByPlatform()
        {
            var command = KeyCombination.FromKeyboardInput(KeyCode.J, EventModifiers.Command);
            var control = KeyCombination.FromKeyboardInput(KeyCode.J, EventModifiers.Control);
            Assert.Equal(Mac ? ShortcutModifiers.Action : ShortcutModifiers.None, command.modifiers);
            Assert.Equal(Mac ? ShortcutModifiers.Control : ShortcutModifiers.Action, control.modifiers);
            var pref = KeyCombination.FromPrefKeyKeyboardEvent(new Event { keyCode = KeyCode.J, modifiers = EventModifiers.Command | EventModifiers.Shift });
            Assert.Equal(ShortcutModifiers.Action | ShortcutModifiers.Shift, pref.modifiers);
            var evt = new KeyCombination(KeyCode.J, ShortcutModifiers.Action).ToKeyboardEvent();
            Assert.Equal(EventType.KeyDown, evt.type);
            Assert.Equal(KeyCode.J, evt.keyCode);
            Assert.Equal(Mac, evt.command);
            Assert.Equal(!Mac, evt.control);
            var removed = KeyCombination.FromKeyboardInputAndRemoveEventModifiers(new KeyCombination(KeyCode.J, ShortcutModifiers.Alt | ShortcutModifiers.Shift), EventModifiers.Shift);
            Assert.Equal(ShortcutModifiers.Alt, removed.modifiers);
        }

        [Fact]
        public void BindingsHashAsTheEditorsTuplesFold()
        {
            Assert.Equal(0, ShortcutBinding.empty.GetHashCode());
            Assert.Empty(ShortcutBinding.empty.keyCombinationSequence);
            Assert.Equal("", ShortcutBinding.empty.ToString());
            var one = new ShortcutBinding(new KeyCombination(KeyCode.F, ShortcutModifiers.Alt));
            Assert.Equal(40495, one.GetHashCode());
            var three = new ShortcutBinding(new[] { new KeyCombination(KeyCode.A, ShortcutModifiers.Shift), new KeyCombination(KeyCode.B), new KeyCombination(KeyCode.Alpha3, ShortcutModifiers.Alt) });
            Assert.Equal(43175365, three.GetHashCode());
            Assert.Equal("B, 3", new ShortcutBinding(new[] { new KeyCombination(KeyCode.B), new KeyCombination(KeyCode.Alpha3) }).ToString());
            Assert.True(new ShortcutBinding(new KeyCombination[0]).Equals(ShortcutBinding.empty));
            var error = Assert.Throws<ArgumentNullException>(() => new ShortcutBinding(null));
            Assert.Equal("keyCombinationSequence", error.ParamName);
        }

        [Fact]
        public void AttributesKeepTheirIdsBindingsAndDefaultPriorities()
        {
            var plain = new ShortcutAttribute("Probe/Plain");
            Assert.Equal("Probe/Plain", plain.identifier);
            Assert.Equal("Probe/Plain", plain.displayName);
            Assert.Equal(1000000, plain.priority);
            Assert.Empty(plain.defaultBinding.keyCombinationSequence);
            var keyed = new ShortcutAttribute("Probe/Keyed", typeof(UnityEditor.SceneView), "tag", KeyCode.K, ShortcutModifiers.Alt);
            Assert.Equal(1000, keyed.priority);
            Assert.Equal("tag", keyed.tag);
            Assert.Equal(new KeyCombination(KeyCode.K, ShortcutModifiers.Alt), keyed.defaultBinding.keyCombinationSequence.Single());
            Assert.Equal(7, new ShortcutAttribute("Probe/Seven", null, null, KeyCode.A, ShortcutModifiers.None, 7).priority);
            var global = typeof(ShortcutAttribute).Assembly.GetType("UnityEditor.ShortcutManagement.ContextManager+GlobalContext");
            Assert.NotNull(global);
            Assert.Equal(1000000, new ShortcutAttribute("Probe/Global", global).priority);
            var clutch = new ClutchShortcutAttribute("Probe/Clutch", typeof(UnityEditor.SceneView), typeof(UnityEditor.SceneView), KeyCode.C);
            Assert.Equal(typeof(UnityEditor.SceneView), clutch.clutchActivatedContext);
            Assert.Equal(ShortcutModifiers.Shift, new ReserveModifiersAttribute(ShortcutModifiers.Shift).Modifiers);
            Assert.Equal(typeof(ContextManager.GlobalContext), ContextManager.globalContextType);
        }

        sealed class Tool : IShortcutContext { public bool active { get; set; } }
        sealed class OtherTool : IShortcutContext { public bool active { get; set; } }

        [ReserveModifiers(ShortcutModifiers.Shift)]
        sealed class ReservingTool : IShortcutContext { public bool active => true; }

        static int s_Runs;
        static void CountRun() => s_Runs++;

        static ShortcutEntry Entry(string id, Type context, string tag, params KeyCombination[] keys) =>
            new ShortcutEntry(new Identifier(id), keys, null, context, tag, ShortcutType.Action);

        [Fact]
        public void EntriesDefaultToTheGlobalContextAndDropReservedModifiers()
        {
            var entry = Entry("Probe/Global", null, null, new KeyCombination(KeyCode.G));
            Assert.Equal(ContextManager.globalContextType, entry.context);
            Assert.Equal("Probe/Global", entry.displayName);
            Assert.Equal(1000000, entry.priority);
            var reserving = Entry("Probe/Reserving", typeof(ReservingTool), null, new KeyCombination(KeyCode.R, ShortcutModifiers.Shift));
            Assert.Equal(ShortcutModifiers.Shift, reserving.m_ReservedModifier);
            Assert.True(reserving.StartsWith(new[] { new KeyCombination(KeyCode.R) }));
            Assert.False(reserving.StartsWith(new[] { new KeyCombination(KeyCode.R, ShortcutModifiers.Alt) }));
            Assert.True(entry.FullyMatches(new System.Collections.Generic.List<KeyCombination> { new KeyCombination(KeyCode.G) }));
        }

        [Fact]
        public void AMethodWithoutParametersRunsThroughTheProxy()
        {
            var method = typeof(ShortcutTypesTests).GetMethod(nameof(CountRun), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            var entry = new ShortcutAttribute("Probe/Run", KeyCode.F2).CreateShortcutEntry(method);
            s_Runs = 0;
            entry.action(new ShortcutArguments { stage = ShortcutStage.End });
            Assert.Equal(1, s_Runs);
            Assert.Equal(ShortcutType.Action, entry.type);
            Assert.Equal(ShortcutType.Clutch, new ClutchShortcutAttribute("Probe/Clutch").CreateShortcutEntry(method).type);
        }

        [Fact]
        public void WithoutTheActiveFilterTheFirstToolContextAnswersAnyType()
        {
            var manager = new ContextManager();
            var tool = new Tool { active = false };
            var other = new OtherTool { active = true };
            manager.RegisterToolContext(tool);
            manager.RegisterToolContext(tool);
            manager.RegisterToolContext(other);
            manager.RegisterToolContext(null);
            Assert.Equal(2, manager.activeContextCount);
            Assert.Same(tool, manager.GetContextInstanceOfType(typeof(OtherTool), filterActive: false));
            Assert.Same(other, manager.GetContextInstanceOfType(typeof(OtherTool), filterActive: true));
            Assert.Null(manager.GetContextInstanceOfType(typeof(Tool), filterActive: true));
            Assert.Same(ContextManager.globalContext, manager.GetContextInstanceOfType(ContextManager.globalContextType, true));
            manager.DeregisterToolContext(other);
            Assert.Equal(1, manager.activeContextCount);
            Assert.Equal("ShortcutStage.End", ContextManager.EnumTagFormat(ShortcutStage.End));
        }

        [Fact]
        public void LookupsPreferAContextsEntriesAndActiveTags()
        {
            var t = new KeyCombination(KeyCode.T);
            var tool = Entry("Probe/Tool", typeof(Tool), null, t);
            var global = Entry("Probe/GlobalT", null, null, t);
            var tagged = Entry("Probe/TaggedT", null, "probe-unit-tag", t);
            var directory = new Directory(new[] { tool, global, tagged });
            var found = new System.Collections.Generic.List<ShortcutEntry>();
            directory.FindShortcutEntries(new System.Collections.Generic.List<KeyCombination> { t }, found);
            Assert.Equal(new[] { tool }, found);
            directory.FindShortcutEntries(new System.Collections.Generic.List<KeyCombination> { t }, new[] { ContextManager.globalContextType }, new string[0], found);
            Assert.Equal(new[] { global }, found);
            directory.FindShortcutEntries(new System.Collections.Generic.List<KeyCombination> { t }, new[] { ContextManager.globalContextType }, new[] { "probe-unit-tag" }, found);
            Assert.Equal(new[] { tagged }, found);
            Assert.Same(global, directory.FindShortcutEntry("Probe/GlobalT"));
            Assert.Null(directory.FindShortcutEntry("probe/globalt"));
            var conflicts = new System.Collections.Generic.List<ShortcutEntry>();
            directory.FindPotentialConflicts(ContextManager.globalContextType, null, new[] { t }, conflicts, new ContextManager());
            Assert.Equal(new[] { global }, conflicts);
        }

        [Fact]
        public void AnActionAndAClutchOnOneMouseButtonDoNotConflict()
        {
            var mouse = new KeyCombination(KeyCode.Mouse1);
            var action = Entry("Probe/Action", typeof(Tool), null, mouse);
            var clutch = new ShortcutEntry(new Identifier("Probe/Clutch"), new[] { mouse }, null, typeof(Tool), ShortcutType.Clutch);
            var key = Entry("Probe/Key", typeof(Tool), null, new KeyCombination(KeyCode.K));
            var sameKey = Entry("Probe/SameKey", typeof(Tool), null, new KeyCombination(KeyCode.K));
            var conflicts = new System.Collections.Generic.List<ShortcutEntry>();
            new Directory(new[] { action, clutch, key, sameKey }).FindShortcutsWithConflicts(conflicts, new ContextManager());
            Assert.Equal(new[] { key, sameKey }, conflicts);
        }
    }
}
