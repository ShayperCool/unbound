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
        }
    }
}
