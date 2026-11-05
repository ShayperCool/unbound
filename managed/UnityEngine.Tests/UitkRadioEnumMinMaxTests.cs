using System;
using System.Collections.Generic;
using System.Linq;
using Unbound.UIElements;
using UnityEngine.UIElements;
using Xunit;

// RadioButton, RadioButtonGroup, EnumField and MinMaxSlider. Each test names its source:
//   [doc]    a statement or example of the Unity 6000.0 Manual pages "RadioButton",
//            "RadioButtonGroup", "EnumField", "MinMaxSlider" (UI Toolkit control reference, with
//            their USS class tables) or the Scripting API pages of these types;
//   [choice] Unbound's choice where those pages say nothing (event order, clamping details,
//            display names, defaults). These check Unbound's implementation, not Unity behaviour.
// No reference capture covers these controls; nothing here is a measured Unity value.
// Red base: 9dbe3085, where the four types are generated stubs (their members do nothing and
// answer defaults). The file uses only members that exist there, so it compiles on that base.
// The UXML EnumField attribute cases name their own red base, 01e797d8.

namespace UnityEngine.Tests
{
    [Collection("UXML custom controls")]
    public class UitkRadioEnumMinMaxTests
    {
        enum ProbeEnum
        {
            Alpha,
            Beta,
            [InspectorName("Gamma Ray")] Gamma,
            [Obsolete] Old,
        }

        enum OtherEnum
        {
            X,
        }

        enum WideEnum : ulong
        {
            None = 0,
            All = ulong.MaxValue,
        }

        sealed class ClickableRadio : RadioButton
        {
            public void Click() => ToggleValue();
        }

        sealed class RecordingMenu : IGenericMenu
        {
            public readonly List<(string Name, bool Checked, Action Action)> Items = new List<(string, bool, Action)>();
            public bool DroppedDown;
            public void AddItem(string itemName, bool isChecked, Action action) => Items.Add((itemName, isChecked, action));
            public void AddItem(string itemName, bool isChecked, Action<object> action, object data) => Items.Add((itemName, isChecked, () => action(data)));
            public void AddDisabledItem(string itemName, bool isChecked) => Items.Add((itemName, isChecked, null));
            public void AddSeparator(string path) { }
            public void DropDown(Rect position, VisualElement targetElement = null, bool anchored = false) => DroppedDown = true;
        }

        static Panel NewPanel() => new Panel(null, ContextType.Player, EventDispatcher.CreateDefault(), null);

        static VisualElement Clone(string body)
        {
            var asset = ScriptableObject.CreateInstance<VisualTreeAsset>();
            asset.name = "UitkControlsProbe";
            UxmlImporter.Import("Assets/UI/UitkControlsProbe.uxml", "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">" + body + "</ui:UXML>", asset);
            var root = new VisualElement();
            asset.CloneTree(root);
            return root;
        }

        /// <summary>The elements of type <typeparamref name="T"/> below <paramref name="root"/>, in
        /// hierarchy order.</summary>
        static List<T> All<T>(VisualElement root) where T : VisualElement
        {
            var found = new List<T>();
            void Walk(VisualElement e)
            {
                foreach (var child in e.hierarchy.Children())
                {
                    if (child is T match) found.Add(match);
                    Walk(child);
                }
            }
            Walk(root);
            return found;
        }

        static void Send(VisualElement target, EventBase evt)
        {
            evt.target = target;
            target.SendEvent(evt);
        }

        // ------------------------------------------------------------------ RadioButton

        // [doc] RadioButton USS class table (unity-radio-button, __label, __input,
        // __checkmark-background, __checkmark, __text; plus unity-base-field) and the
        // "Text"/"Label" attributes of BaseBoolField.
        // Red on 9dbe3085: the stub's ussClassName is null (first Assert.Equal fails).
        [Fact]
        public void RadioButtonHasTheDocumentedUssClasses()
        {
            Assert.Equal("unity-radio-button", RadioButton.ussClassName);
            Assert.Equal("unity-radio-button__label", RadioButton.labelUssClassName);
            Assert.Equal("unity-radio-button__input", RadioButton.inputUssClassName);
            Assert.Equal("unity-radio-button__checkmark-background", RadioButton.checkmarkBackgroundUssClassName);
            Assert.Equal("unity-radio-button__checkmark", RadioButton.checkmarkUssClassName);
            Assert.Equal("unity-radio-button__text", RadioButton.textUssClassName);
            var radio = new RadioButton("Label") { text = "Text" };
            Assert.True(radio.ClassListContains("unity-radio-button"));
            Assert.True(radio.ClassListContains("unity-base-field"));
            Assert.Equal("Label", radio.label);
            Assert.True(radio.labelElement.ClassListContains("unity-radio-button__label"));
            Assert.NotNull(radio.Q(null, "unity-radio-button__input"));
            var background = radio.Q(null, "unity-radio-button__checkmark-background");
            Assert.NotNull(background);
            Assert.NotNull(background.Q(null, "unity-radio-button__checkmark"));
            Assert.Equal("Text", Assert.IsType<Label>(radio.Q(null, "unity-radio-button__text")).text);
        }

        // [choice] A click checks an unchecked radio button; clicking a checked one keeps it checked.
        // Red on 9dbe3085: the stub ToggleValue does nothing and value answers false.
        [Fact]
        public void ClickChecksAndKeepsChecked()
        {
            var radio = new ClickableRadio();
            radio.Click();
            Assert.True(radio.value);
            radio.Click();
            Assert.True(radio.value);
        }

        // [doc] "Otherwise, the panel acts as the default group": checking one radio button of a
        // panel unchecks the others.
        // Red on 9dbe3085: b.value answers false.
        [Fact]
        public void CheckingOneUnchecksTheOthersOfThePanel()
        {
            using var panel = NewPanel();
            var a = new RadioButton("a");
            var b = new RadioButton("b");
            var c = new RadioButton("c");
            panel.visualTree.Add(a);
            panel.visualTree.Add(b);
            panel.visualTree.Add(c);
            a.value = true;
            b.value = true;
            Assert.False(a.value);
            Assert.True(b.value);
            Assert.False(c.value);
        }

        // [doc] "You can use a GroupBox to group RadioButtons": each GroupBox is its own group.
        // Red on 9dbe3085: a2.value answers false.
        [Fact]
        public void GroupBoxesFormSeparateGroups()
        {
            var root = new VisualElement();
            var first = new GroupBox();
            var second = new GroupBox();
            root.Add(first);
            root.Add(second);
            var a1 = new RadioButton("a1");
            var a2 = new RadioButton("a2");
            var b1 = new RadioButton("b1");
            first.Add(a1);
            first.Add(a2);
            second.Add(b1);
            a1.value = true;
            b1.value = true;
            a2.value = true;
            Assert.False(a1.value);
            Assert.True(a2.value);
            Assert.True(b1.value);
        }

        // ------------------------------------------------------------------ RadioButtonGroup

        // [doc] RadioButtonGroup USS class table (unity-radio-button-group, __container) and its
        // constructor example creating the choices.
        // [choice] The created buttons show the choices as their text; a new group's value is -1.
        // Red on 9dbe3085: the stub's ussClassName is null (and the stub constructor creates no
        // radio button).
        [Fact]
        public void ChoicesCreateRadioButtons()
        {
            Assert.Equal("unity-radio-button-group", RadioButtonGroup.ussClassName);
            Assert.Equal("unity-radio-button-group__container", RadioButtonGroup.containerUssClassName);
            var group = new RadioButtonGroup("Options", new List<string> { "A", "B", "C" });
            var radios = All<RadioButton>(group);
            Assert.Equal(new[] { "A", "B", "C" }, radios.Select(r => r.text));
            Assert.Equal(new[] { "A", "B", "C" }, group.choices);
            Assert.True(group.ClassListContains("unity-radio-button-group"));
            Assert.NotNull(group.Q(null, "unity-radio-button-group__container"));
            Assert.All(radios, r => Assert.False(r.value));
            Assert.Equal(-1, group.value);
        }

        // [doc] The value is "the numbering sequence of the option ... starting at 0"; "Selecting one
        // will deselect the others".
        // Red on 9dbe3085: no radio button exists (the checked-state list is empty).
        [Fact]
        public void ValueIsTheIndexOfTheCheckedOption()
        {
            var group = new RadioButtonGroup("G", new List<string> { "A", "B", "C" });
            group.value = 1;
            var radios = All<RadioButton>(group);
            Assert.Equal(new[] { false, true, false }, radios.Select(r => r.value));
            radios[2].value = true;
            Assert.Equal(2, group.value);
            Assert.Equal(new[] { false, false, true }, radios.Select(r => r.value));
        }

        // [doc] The group sends ChangeEvent<int> (the Manual's example registers it).
        // [choice] Checking an option through value unchecks the previous one through value (its
        // ChangeEvent<bool> first), then the option's own ChangeEvent<bool>; the group's value and
        // SetValueWithoutNotify check and uncheck the options silently.
        // Red on 9dbe3085: no radio button exists (Assert.Equal(2, radios.Count) fails).
        [Fact]
        public void ChangeEventsOfGroupAndOptions()
        {
            using var panel = NewPanel();
            var group = new RadioButtonGroup("G", new List<string> { "A", "B" });
            panel.visualTree.Add(group);
            var radios = All<RadioButton>(group);
            Assert.Equal(2, radios.Count);
            var groupEvents = new List<(int, int)>();
            group.RegisterValueChangedCallback(e => groupEvents.Add((e.previousValue, e.newValue)));
            var radioEvents = new List<string>();
            for (int i = 0; i < radios.Count; i++)
            {
                var radio = radios[i];
                var index = i;
                radio.RegisterValueChangedCallback(e =>
                {
                    if (e.target == radio) radioEvents.Add($"{index}:{e.newValue}");
                });
            }

            radios[0].value = true;
            Assert.Equal(new[] { (-1, 0) }, groupEvents);
            Assert.Equal(new[] { "0:True" }, radioEvents);

            radios[1].value = true;
            Assert.Equal(new[] { (-1, 0), (0, 1) }, groupEvents);
            Assert.Equal(new[] { "0:True", "0:False", "1:True" }, radioEvents);

            group.value = 0;
            Assert.Equal(new[] { (-1, 0), (0, 1), (1, 0) }, groupEvents);
            Assert.Equal(new[] { "0:True", "0:False", "1:True" }, radioEvents);
            Assert.True(radios[0].value);
            Assert.False(radios[1].value);

            group.SetValueWithoutNotify(1);
            Assert.Equal(3, groupEvents.Count);
            Assert.Equal(3, radioEvents.Count);
            Assert.Equal(new[] { false, true }, radios.Select(r => r.value));
        }

        // [doc] Writing choices "removes existing RadioButton elements, except those added explicitly
        // to hierarchy, and re-creates them to display the new list".
        // [choice] The created buttons come before the explicit ones.
        // Red on 9dbe3085: Add throws InvalidOperationException (the stub contentContainer is null).
        [Fact]
        public void RewritingChoicesKeepsExplicitRadioButtons()
        {
            var group = new RadioButtonGroup("G", new List<string> { "A", "B" });
            group.Add(new RadioButton { text = "X" });
            group.choices = new List<string> { "C" };
            var texts = All<RadioButton>(group).Select(r => r.text).ToList();
            Assert.Equal(2, texts.Count);
            Assert.Contains("C", texts);
            Assert.Contains("X", texts);
            Assert.Equal(new[] { "C", "X" }, texts);
        }

        // [choice] A checked radio button added to a group becomes the group's checked option.
        // Red on 9dbe3085: Add throws InvalidOperationException (the stub contentContainer is null).
        [Fact]
        public void AddingACheckedRadioButtonSetsTheGroupValue()
        {
            var group = new RadioButtonGroup("G", new List<string> { "A", "B" });
            group.value = 0;
            group.Add(new RadioButton { text = "X", value = true });
            Assert.Equal(2, group.value);
            Assert.Equal(new[] { false, false, true }, All<RadioButton>(group).Select(r => r.value));
        }

        // [doc] UXML tags RadioButtonGroup (label, choices as comma separated values, value) and
        // RadioButton (label, text, value), read by the existing UXML path.
        // Red on 9dbe3085: the stub SetValueWithoutNotify ignores value (group.value answers 0), and
        // the stub ignores choices.
        [Fact]
        public void UxmlCreatesRadioButtonGroupAndRadioButton()
        {
            var root = Clone("<ui:RadioButtonGroup label=\"G\" choices=\"A,B,C\" value=\"1\" /><ui:RadioButton label=\"L\" text=\"T\" value=\"true\" />");
            var group = Assert.IsType<RadioButtonGroup>(root[0]);
            Assert.Equal("G", group.label);
            Assert.Equal(1, group.value);
            Assert.Equal(new[] { false, true, false }, All<RadioButton>(group).Select(r => r.value));
            var radio = Assert.IsType<RadioButton>(root[1]);
            Assert.Equal("L", radio.label);
            Assert.Equal("T", radio.text);
            Assert.True(radio.value);
        }

        // [doc] UXML RadioButton children of a RadioButtonGroup are its options.
        // [choice] The group's value checks the option added later at that index.
        // Red on 9dbe3085: adding the children to the stub (null contentContainer) throws
        // InvalidOperationException, or leaves the group without options if the builder catches it.
        [Fact]
        public void UxmlRadioButtonChildrenJoinTheGroup()
        {
            var root = Clone("<ui:RadioButtonGroup value=\"1\"><ui:RadioButton text=\"A\" /><ui:RadioButton text=\"B\" /></ui:RadioButtonGroup>");
            var group = Assert.IsType<RadioButtonGroup>(root[0]);
            var radios = All<RadioButton>(group);
            Assert.Equal(new[] { "A", "B" }, radios.Select(r => r.text));
            Assert.Equal(new[] { false, true }, radios.Select(r => r.value));
            radios[0].value = true;
            Assert.Equal(0, group.value);
            Assert.False(radios[1].value);
        }

        // ------------------------------------------------------------------ EnumField

        // [doc] EnumField USS class table (unity-enum-field, __text, __arrow, __label, __input).
        // Red on 9dbe3085: the stub's ussClassName is null.
        [Fact]
        public void EnumFieldHasTheDocumentedUssClasses()
        {
            Assert.Equal("unity-enum-field", EnumField.ussClassName);
            Assert.Equal("unity-enum-field__text", EnumField.textUssClassName);
            Assert.Equal("unity-enum-field__arrow", EnumField.arrowUssClassName);
            Assert.Equal("unity-enum-field__label", EnumField.labelUssClassName);
            Assert.Equal("unity-enum-field__input", EnumField.inputUssClassName);
            var field = new EnumField("L", ProbeEnum.Beta);
            Assert.True(field.ClassListContains("unity-enum-field"));
            Assert.True(field.ClassListContains("unity-base-field"));
            Assert.True(field.labelElement.ClassListContains("unity-enum-field__label"));
            var input = field.Q(null, "unity-enum-field__input");
            Assert.NotNull(input);
            Assert.NotNull(input.Q(null, "unity-enum-field__text"));
            Assert.NotNull(input.Q(null, "unity-enum-field__arrow"));
        }

        // [doc] The constructor value is "Also used to detect Enum type"; Init "Initializes the
        // EnumField with a default value, and initializes its underlying type"; text "Return the text
        // value of the currently selected enum".
        // [choice] The text is the InspectorName, else the declared name; an empty field shows "".
        // Red on 9dbe3085: the stub constructor leaves value null.
        [Fact]
        public void ConstructorInitValueAndText()
        {
            var field = new EnumField("L", ProbeEnum.Beta);
            Assert.Equal((Enum)ProbeEnum.Beta, field.value);
            Assert.Equal("Beta", field.text);
            field.value = ProbeEnum.Alpha;
            Assert.Equal("Alpha", field.text);
            field.Init(ProbeEnum.Gamma);
            Assert.Equal((Enum)ProbeEnum.Gamma, field.value);
            Assert.Equal("Gamma Ray", field.text);

            var empty = new EnumField();
            Assert.Null(empty.value);
            Assert.Equal(string.Empty, empty.text);
            empty.Init(ProbeEnum.Beta);
            Assert.Equal((Enum)ProbeEnum.Beta, empty.value);
            Assert.Equal("Beta", empty.text);
        }

        // [doc] "Makes a dropdown for switching between enum values"; Init's includeObsoleteValues:
        // "Set to true to display obsolete values as choices".
        // [choice] Item texts as above, in declaration order; the current value is checked.
        // Red on 9dbe3085: the stub ShowMenu adds no item.
        [Fact]
        public void MenuListsTheEnumValues()
        {
            var menu = new RecordingMenu();
            var field = new EnumField("L", ProbeEnum.Beta) { createMenuCallback = () => menu };
            field.ShowMenu();
            Assert.Equal(new[] { "Alpha", "Beta", "Gamma Ray" }, menu.Items.Select(i => i.Name));
            Assert.Equal(new[] { false, true, false }, menu.Items.Select(i => i.Checked));
            Assert.True(menu.DroppedDown);

            var withObsolete = new RecordingMenu();
            var obsoleteField = new EnumField { createMenuCallback = () => withObsolete };
            obsoleteField.Init(ProbeEnum.Alpha, true);
            obsoleteField.ShowMenu();
            Assert.Equal(new[] { "Alpha", "Beta", "Gamma Ray", "Old" }, withObsolete.Items.Select(i => i.Name));
        }

        // [doc] Choosing in the dropdown sets the value; the field sends ChangeEvent<Enum> (the
        // Manual's example registers it).
        // Red on 9dbe3085: the menu has no item (Assert.Equal(3, menu.Items.Count) fails).
        [Fact]
        public void ChoosingAMenuItemSetsTheValueWithAChangeEvent()
        {
            using var panel = NewPanel();
            var menu = new RecordingMenu();
            var field = new EnumField("L", ProbeEnum.Beta) { createMenuCallback = () => menu };
            panel.visualTree.Add(field);
            var events = new List<(Enum, Enum)>();
            field.RegisterValueChangedCallback(e => events.Add((e.previousValue, e.newValue)));
            field.ShowMenu();
            Assert.Equal(3, menu.Items.Count);
            menu.Items[2].Action();
            Assert.Equal((Enum)ProbeEnum.Gamma, field.value);
            Assert.Equal("Gamma Ray", field.text);
            Assert.Equal(new[] { ((Enum)ProbeEnum.Beta, (Enum)ProbeEnum.Gamma) }, events);
        }

        // [choice] A value of another enum type fails loudly.
        // Red on 9dbe3085: the stub SetValueWithoutNotify accepts it silently.
        [Fact]
        public void ValueOfAnotherEnumTypeThrows()
        {
            var field = new EnumField("L", ProbeEnum.Beta);
            Assert.Throws<ArgumentException>(() => field.value = OtherEnum.X);
        }

        // [doc] The Manual's UXML example (<EnumField label=... name=... include-obsolete-values=...>)
        // then Init from C#.
        // Red on 9dbe3085: the stub Init does nothing, so value stays null (the first failing
        // assertion is the one on value).
        [Fact]
        public void UxmlCreatesEnumField()
        {
            var root = Clone("<ui:EnumField label=\"UXML Field\" name=\"the-uxml-field\" include-obsolete-values=\"false\" />");
            var field = Assert.IsType<EnumField>(root[0]);
            Assert.Equal("UXML Field", field.label);
            Assert.Equal("the-uxml-field", field.name);
            field.Init(ProbeEnum.Gamma);
            Assert.Equal((Enum)ProbeEnum.Gamma, field.value);
            Assert.Equal("Gamma Ray", field.text);
        }

        // UXML EnumField attributes `type`, `value` and `include-obsolete-values` (the EnumField
        // attribute descriptions of the 6000.0 API metadata; `value` is the documented inherited
        // BaseField attribute). Red base for these cases: 01e797d8, where the builder ignored all
        // three silently (the field stayed untyped and nothing was logged). They use only members
        // that exist there, so the file still compiles on it.

        /// <summary>The ProbeEnum type as a UXML `type` attribute: "Namespace.Type, Assembly".</summary>
        static readonly string ProbeEnumType = typeof(ProbeEnum).FullName + ", " + typeof(ProbeEnum).Assembly.GetName().Name;
        static readonly string WideEnumType = typeof(WideEnum).FullName + ", " + typeof(WideEnum).Assembly.GetName().Name;

        /// <summary>The errors and exceptions logged while <paramref name="action"/> runs that mention
        /// this file's UXML document (other tests may log at the same time).</summary>
        static List<string> Errors(Action action)
        {
            var logs = new List<string>();
            Application.LogCallback capture = (message, stack, type) =>
            {
                if ((type == LogType.Error || type == LogType.Exception) && message != null && message.Contains("UitkControlsProbe"))
                    lock (logs) logs.Add(message);
            };
            Application.logMessageReceived += capture;
            try { action(); }
            finally { Application.logMessageReceived -= capture; }
            return logs;
        }

        // [doc] Init "Initializes the EnumField with a default value, and initializes its underlying
        // type"; the attributes take effect whatever their order in the file.
        // [choice] `type` as an assembly-qualified name; `value` as a declared name.
        // Red on 01e797d8: a.value is null (the first Assert.Equal fails).
        [Fact]
        public void UxmlTypeAndValueInitializeEnumFieldInEitherOrder()
        {
            VisualElement root = null;
            var errors = Errors(() => root = Clone(
                $"<ui:EnumField name=\"a\" type=\"{ProbeEnumType}\" value=\"Gamma\" />"
                + $"<ui:EnumField name=\"b\" value=\"Beta\" type=\"{ProbeEnumType}\" />"));
            var a = Assert.IsType<EnumField>(root[0]);
            var b = Assert.IsType<EnumField>(root[1]);
            Assert.Equal((Enum)ProbeEnum.Gamma, a.value);
            Assert.Equal("Gamma Ray", a.text);
            Assert.Equal((Enum)ProbeEnum.Beta, b.value);
            Assert.Equal("Beta", b.text);
            var menu = new RecordingMenu();
            a.createMenuCallback = () => menu;
            a.ShowMenu();
            Assert.Equal(new[] { "Alpha", "Beta", "Gamma Ray" }, menu.Items.Select(i => i.Name));
            Assert.Equal(new[] { false, false, true }, menu.Items.Select(i => i.Checked));
            Assert.Empty(errors);
        }

        // [doc] Init's includeObsoleteValues: "Set to true to display obsolete values as choices".
        // [choice] The attribute is read as the builder reads other booleans; absent means false.
        // Red on 01e797d8: obsolete.text is "" (the field is untyped; the first Assert.Equal fails).
        [Fact]
        public void UxmlIncludeObsoleteValuesShowsObsoleteChoices()
        {
            VisualElement root = null;
            var errors = Errors(() => root = Clone(
                $"<ui:EnumField name=\"obsolete\" include-obsolete-values=\"true\" value=\"Old\" type=\"{ProbeEnumType}\" />"
                + $"<ui:EnumField name=\"plain\" type=\"{ProbeEnumType}\" value=\"Alpha\" />"));
            var obsolete = Assert.IsType<EnumField>(root[0]);
            var plain = Assert.IsType<EnumField>(root[1]);
            Assert.Equal("Old", obsolete.text);
            var obsoleteMenu = new RecordingMenu();
            obsolete.createMenuCallback = () => obsoleteMenu;
            obsolete.ShowMenu();
            Assert.Equal(new[] { "Alpha", "Beta", "Gamma Ray", "Old" }, obsoleteMenu.Items.Select(i => i.Name));
            Assert.Equal(new[] { false, false, false, true }, obsoleteMenu.Items.Select(i => i.Checked));
            var plainMenu = new RecordingMenu();
            plain.createMenuCallback = () => plainMenu;
            plain.ShowMenu();
            Assert.Equal(new[] { "Alpha", "Beta", "Gamma Ray" }, plainMenu.Items.Select(i => i.Name));
            Assert.Empty(errors);
        }

        // [choice] Unsupported content fails loudly: a `type` that is not loaded, or is not an enum,
        // logs one error naming the attribute and the element, and the field stays untyped.
        // Red on 01e797d8: nothing is logged (the first Assert.Single fails).
        [Fact]
        public void UxmlUnresolvableEnumTypeLogsOneError()
        {
            VisualElement missingRoot = null;
            var missingErrors = Errors(() => missingRoot = Clone("<ui:EnumField type=\"No.Such.ProbeEnum, NoSuchAssembly\" value=\"Beta\" />"));
            var missing = Assert.Single(missingErrors);
            Assert.Contains("EnumField", missing);
            Assert.Contains("'type'", missing);
            Assert.Contains("No.Such.ProbeEnum", missing);
            Assert.Null(Assert.IsType<EnumField>(missingRoot[0]).value);

            VisualElement notEnumRoot = null;
            var notEnumErrors = Errors(() => notEnumRoot = Clone("<ui:EnumField type=\"System.String\" />"));
            var notEnum = Assert.Single(notEnumErrors);
            Assert.Contains("EnumField", notEnum);
            Assert.Contains("'type'", notEnum);
            Assert.Null(Assert.IsType<EnumField>(notEnumRoot[0]).value);
        }

        // [choice] A `value` naming no declared value of the type logs one error naming the
        // attribute and the element; the field still takes the type, with its zero value.
        // Red on 01e797d8: nothing is logged (Assert.Single fails).
        [Fact]
        public void UxmlUnknownEnumValueLogsOneError()
        {
            VisualElement root = null;
            var errors = Errors(() => root = Clone($"<ui:EnumField type=\"{ProbeEnumType}\" value=\"Delta\" />"));
            var error = Assert.Single(errors);
            Assert.Contains("EnumField", error);
            Assert.Contains("'value'", error);
            Assert.Contains("Delta", error);
            var field = Assert.IsType<EnumField>(root[0]);
            Assert.Equal((Enum)ProbeEnum.Alpha, field.value);
        }

        // [choice] A `value` without a `type` cannot be read; it logs one error naming the attribute
        // and the element, and the field stays untyped.
        // Red on 01e797d8: nothing is logged (Assert.Single fails).
        [Fact]
        public void UxmlEnumValueWithoutTypeLogsOneError()
        {
            VisualElement root = null;
            var errors = Errors(() => root = Clone("<ui:EnumField value=\"Beta\" />"));
            var error = Assert.Single(errors);
            Assert.Contains("EnumField", error);
            Assert.Contains("'value'", error);
            Assert.Contains("'type'", error);
            Assert.Null(Assert.IsType<EnumField>(root[0]).value);
        }

        // [doc] The Manual's pattern: UXML without a type (include-obsolete-values set), then Init
        // from C#. It must log no error.
        // Regression guard, green on 01e797d8 (nothing was logged there). Its red is a mutation:
        // the case fails if the builder reports a missing `type` when no `value` is given, or
        // reports `include-obsolete-values` without a `type`.
        [Fact]
        public void UxmlEnumFieldWithoutTypeThenInitLogsNoError()
        {
            EnumField field = null;
            var errors = Errors(() =>
            {
                var root = Clone("<ui:EnumField label=\"UXML Field\" name=\"the-uxml-field\" include-obsolete-values=\"false\" />");
                field = Assert.IsType<EnumField>(root[0]);
                field.Init(ProbeEnum.Beta);
            });
            Assert.Empty(errors);
            Assert.Equal((Enum)ProbeEnum.Beta, field.value);
            Assert.Equal("Beta", field.text);
        }

        // [choice] `value` is read as the builder reads its other enum attributes: a declared name in
        // any case, or an integer.
        // Red on 85fcc53e (own red base): only the exact declared name was accepted, so "gamma"
        // logged an error and a.value is Alpha (the first Assert.Equal fails).
        [Fact]
        public void UxmlEnumValueIgnoresCaseAndAcceptsNumbers()
        {
            VisualElement root = null;
            var errors = Errors(() => root = Clone(
                $"<ui:EnumField name=\"a\" type=\"{ProbeEnumType}\" value=\"gamma\" />"
                + $"<ui:EnumField name=\"b\" type=\"{ProbeEnumType}\" value=\"BETA\" />"
                + $"<ui:EnumField name=\"c\" type=\"{ProbeEnumType}\" value=\"1\" />"));
            var a = Assert.IsType<EnumField>(root[0]);
            Assert.Equal((Enum)ProbeEnum.Gamma, a.value);
            Assert.Equal("Gamma Ray", a.text);
            Assert.Equal((Enum)ProbeEnum.Beta, Assert.IsType<EnumField>(root[1]).value);
            Assert.Equal((Enum)ProbeEnum.Beta, Assert.IsType<EnumField>(root[2]).value);
            Assert.Empty(errors);
        }

        // [choice] A member whose value lies outside Int32 (here a ulong) is still applied by name,
        // with no error and no exception out of CloneTree. Red at b1124616: the builder's Int32
        // conversion throws OverflowException out of CloneTree (root stays null).
        [Fact]
        public void UxmlEnumValueOutsideInt32IsApplied()
        {
            VisualElement root = null;
            var errors = Errors(() => root = Clone($"<ui:EnumField name=\"w\" type=\"{WideEnumType}\" value=\"All\" />"));
            Assert.NotNull(root);
            Assert.Equal((Enum)WideEnum.All, Assert.IsType<EnumField>(root[0]).value);
            Assert.Empty(errors);
        }

        // [choice] A `value` without a `type` on a field its constructor already typed (an
        // [UxmlElement] subclass) is read against that type; a value the type cannot read logs one
        // error naming the attribute and the element, and the field keeps its value. (A field with
        // no type still logs the missing `type`: UxmlEnumValueWithoutTypeLogsOneError.)
        // Red on 85fcc53e (own red base): the builder reported the missing `type` and left the
        // constructor's value, so typed.value is Second (the first Assert.Equal fails).
        [Fact]
        public void UxmlEnumValueWithoutTypeUsesTheFieldsExistingType()
        {
            VisualElement root = null;
            var errors = Errors(() => root = Clone(
                "<UitkEnumFieldProbes.TypedProbeEnumField name=\"typed\" value=\"Third\" />"
                + "<UitkEnumFieldProbes.TypedProbeEnumField name=\"bad\" value=\"Nope\" />"));
            var typed = Assert.IsType<global::UitkEnumFieldProbes.TypedProbeEnumField>(root[0]);
            Assert.Equal((Enum)global::UitkEnumFieldProbes.TypedProbeChoice.Third, typed.value);
            Assert.Equal("Third", typed.text);
            var bad = Assert.IsType<global::UitkEnumFieldProbes.TypedProbeEnumField>(root[1]);
            Assert.Equal((Enum)global::UitkEnumFieldProbes.TypedProbeChoice.Second, bad.value);
            var error = Assert.Single(errors);
            Assert.Contains("TypedProbeEnumField", error);
            Assert.Contains("'value'", error);
            Assert.Contains("Nope", error);
            Assert.Contains(typeof(global::UitkEnumFieldProbes.TypedProbeChoice).FullName, error);
        }

        // ------------------------------------------------------------------ MinMaxSlider

        // [doc] MinMaxSlider USS class table (unity-min-max-slider, __label, __input, __tracker,
        // __dragger, __min-thumb, __max-thumb, --movable).
        // Red on 9dbe3085: the stub's ussClassName is null.
        [Fact]
        public void MinMaxSliderHasTheDocumentedUssClasses()
        {
            Assert.Equal("unity-min-max-slider", MinMaxSlider.ussClassName);
            Assert.Equal("unity-min-max-slider__label", MinMaxSlider.labelUssClassName);
            Assert.Equal("unity-min-max-slider__input", MinMaxSlider.inputUssClassName);
            Assert.Equal("unity-min-max-slider__tracker", MinMaxSlider.trackerUssClassName);
            Assert.Equal("unity-min-max-slider__dragger", MinMaxSlider.draggerUssClassName);
            Assert.Equal("unity-min-max-slider__min-thumb", MinMaxSlider.minThumbUssClassName);
            Assert.Equal("unity-min-max-slider__max-thumb", MinMaxSlider.maxThumbUssClassName);
            Assert.Equal("unity-min-max-slider--movable", MinMaxSlider.movableUssClassName);
            var slider = new MinMaxSlider("L", 0, 20, -10, 40);
            Assert.True(slider.ClassListContains("unity-min-max-slider"));
            Assert.True(slider.ClassListContains("unity-base-field"));
            Assert.True(slider.labelElement.ClassListContains("unity-min-max-slider__label"));
            Assert.NotNull(slider.Q(null, "unity-min-max-slider__input"));
            Assert.NotNull(slider.Q(null, "unity-min-max-slider__tracker"));
            Assert.Same(slider.dragElement, slider.Q(null, "unity-min-max-slider__dragger"));
            Assert.Same(slider.dragMinThumb, slider.Q(null, "unity-min-max-slider__min-thumb"));
            Assert.Same(slider.dragMaxThumb, slider.Q(null, "unity-min-max-slider__max-thumb"));
        }

        // [doc] Constructor parameters: "The minimum value in the range to be represented. The maximum
        // value in the range to be represented. The minimum value of the slider limit. The maximum
        // value of the slider limit."; range "Returns the range of the low/high limits".
        // [choice] MinMaxSlider() uses the label constructor's parameter defaults.
        // Red on 9dbe3085: the stub value answers (0, 0).
        [Fact]
        public void ConstructorsSetValueAndLimits()
        {
            var slider = new MinMaxSlider("L", 0, 20, -10, 40);
            Assert.Equal(new Vector2(0, 20), slider.value);
            Assert.Equal(0f, slider.minValue);
            Assert.Equal(20f, slider.maxValue);
            Assert.Equal(-10f, slider.lowLimit);
            Assert.Equal(40f, slider.highLimit);
            Assert.Equal(50f, slider.range);

            var unlabelled = new MinMaxSlider(2, 3, 0, 5);
            Assert.Equal(new Vector2(2, 3), unlabelled.value);
            Assert.Equal(0f, unlabelled.lowLimit);
            Assert.Equal(5f, unlabelled.highLimit);

            var plain = new MinMaxSlider();
            Assert.Equal(new Vector2(0, 10), plain.value);
            Assert.Equal(float.MinValue, plain.lowLimit);
            Assert.Equal(float.MaxValue, plain.highLimit);
        }

        // [doc] The range is selected "within a specified minimum and maximum range": both bounds stay
        // within the limits, also when a limit changes.
        // [choice] The higher bound never goes below the lower one; moving one bound past the other
        // moves the other along.
        // Red on 9dbe3085: the stub value answers (0, 0).
        [Fact]
        public void ValueIsClampedToTheLimits()
        {
            var slider = new MinMaxSlider("L", 0, 20, -10, 40);
            slider.value = new Vector2(-100, 100);
            Assert.Equal(new Vector2(-10, 40), slider.value);
            slider.value = new Vector2(0, 20);
            slider.lowLimit = 5;
            Assert.Equal(new Vector2(5, 20), slider.value);
            slider.highLimit = 10;
            Assert.Equal(new Vector2(5, 10), slider.value);

            slider.minValue = 50;
            Assert.Equal(new Vector2(10, 10), slider.value);
            slider.value = new Vector2(6, 8);
            slider.maxValue = -5;
            Assert.Equal(new Vector2(5, 5), slider.value);
            slider.value = new Vector2(9, 7);
            Assert.Equal(new Vector2(9, 9), slider.value);
        }

        // [doc] The slider sends ChangeEvent<Vector2> (the Manual's example registers it).
        // [choice] A limit change that clamps the value sends one; SetValueWithoutNotify sends none.
        // Red on 9dbe3085: the stub value setter sends no event.
        [Fact]
        public void ChangeEventsCarryTheClampedValue()
        {
            using var panel = NewPanel();
            var slider = new MinMaxSlider("L", 0, 20, -10, 40);
            panel.visualTree.Add(slider);
            var events = new List<(Vector2, Vector2)>();
            slider.RegisterValueChangedCallback(e => events.Add((e.previousValue, e.newValue)));
            slider.value = new Vector2(5, 300);
            Assert.Equal(new[] { (new Vector2(0, 20), new Vector2(5, 40)) }, events);
            slider.lowLimit = 10;
            Assert.Equal((new Vector2(5, 40), new Vector2(10, 40)), events.Last());
            slider.SetValueWithoutNotify(new Vector2(12, 14));
            slider.value = new Vector2(12, 14);
            Assert.Equal(2, events.Count);
        }

        // [doc] Scripting API remarks: the slider "cycles through the navigation states when a
        // NavigationSubmitEvent is received": minimum thumb, maximum thumb, middle bar, then default
        // navigation; the controlled element has the movable class; a move "aligns with the
        // slider's direction" adjusts the minimum, the maximum, or both.
        // [choice] The step is the power of ten nearest 1% of the limits' range (here 1).
        // Red on 9dbe3085: the stub dragMinThumb is null (NullReferenceException).
        [Fact]
        public void NavigationCyclesThroughThumbsAndMovesThem()
        {
            using var panel = NewPanel();
            var slider = new MinMaxSlider("L", 0, 20, -10, 40);
            panel.visualTree.Add(slider);
            string movable = "unity-min-max-slider--movable";
            void Submit()
            {
                using var e = NavigationSubmitEvent.GetPooled();
                Send(slider, e);
            }
            void Move(NavigationMoveEvent.Direction direction)
            {
                using var e = NavigationMoveEvent.GetPooled(direction);
                Send(slider, e);
            }
            bool[] State() => new[]
            {
                slider.dragMinThumb.ClassListContains(movable),
                slider.dragMaxThumb.ClassListContains(movable),
                slider.dragElement.ClassListContains(movable),
            };

            Submit();
            Assert.Equal(new[] { true, false, false }, State());
            Move(NavigationMoveEvent.Direction.Right);
            Assert.Equal(new Vector2(1, 20), slider.value);
            Submit();
            Assert.Equal(new[] { false, true, false }, State());
            Move(NavigationMoveEvent.Direction.Left);
            Assert.Equal(new Vector2(1, 19), slider.value);
            Submit();
            Assert.Equal(new[] { false, false, true }, State());
            Move(NavigationMoveEvent.Direction.Left);
            Assert.Equal(new Vector2(0, 18), slider.value);
            Submit();
            Assert.Equal(new[] { false, false, false }, State());
            Move(NavigationMoveEvent.Direction.Right);
            Assert.Equal(new Vector2(0, 18), slider.value);
        }

        // [doc] The Manual's UXML example: <MinMaxSlider label=... min-value="0" max-value="20"
        // low-limit="-10" high-limit="40" />, read by the existing UXML path.
        // Red on 9dbe3085: the stub properties ignore the attributes (lowLimit answers 0).
        [Fact]
        public void UxmlCreatesMinMaxSlider()
        {
            var root = Clone("<ui:MinMaxSlider label=\"UXML Field\" name=\"the-uxml-field\" min-value=\"0\" max-value=\"20\" low-limit=\"-10\" high-limit=\"40\" />");
            var slider = Assert.IsType<MinMaxSlider>(root[0]);
            Assert.Equal("UXML Field", slider.label);
            Assert.Equal(-10f, slider.lowLimit);
            Assert.Equal(40f, slider.highLimit);
            Assert.Equal(new Vector2(0, 20), slider.value);
        }
    }
}

namespace UitkEnumFieldProbes
{
    public enum TypedProbeChoice
    {
        First,
        Second,
        Third,
    }

    /// <summary>An EnumField subclass whose constructor gives the field its type and value.</summary>
    [UxmlElement]
    public partial class TypedProbeEnumField : EnumField
    {
        public TypedProbeEnumField() : base("Typed", TypedProbeChoice.Second) { }
    }
}
