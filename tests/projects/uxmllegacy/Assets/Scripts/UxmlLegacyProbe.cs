#pragma warning disable CS0618 // the deprecated UxmlFactory/UxmlTraits path is what is measured
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

// UXML read through the deprecated UxmlFactory/UxmlTraits path that older custom controls use:
// game elements whose factories use the traits of VisualElement, BindableElement and built-in
// controls, each giving the element state of its own in its constructor, created from UXML without
// attributes and with them; a game traits class over a built-in one; and the attribute
// descriptions every built-in traits class declares (names, types, defaults, obsolete names). Also
// the attributes of built-in elements whose property is internal or named differently
// (placeholder-text, hide-placeholder-on-focus, selectable, ...).
namespace LegacyProbe
{
    public enum Fruit { Apple, Pear }

    public class OldEnum : EnumField
    {
        public new class UxmlFactory : UxmlFactory<OldEnum, UxmlTraits> { }
        public OldEnum() : base(KeyCode.Space) { label = "ctorLabel"; }
    }

    public class OldPlain : VisualElement
    {
        public new class UxmlFactory : UxmlFactory<OldPlain, UxmlTraits> { }
        public OldPlain()
        {
            name = "ctorName";
            viewDataKey = "ctorKey";
            tooltip = "ctorTip";
            pickingMode = PickingMode.Ignore;
            usageHints = UsageHints.DynamicColor;
            focusable = true;
            tabIndex = 3;
            SetEnabled(false);
        }
    }

    public class OldBindable : BindableElement
    {
        public new class UxmlFactory : UxmlFactory<OldBindable, UxmlTraits> { }
        public OldBindable() { bindingPath = "ctorPath"; focusable = true; }
    }

    public class OldText : TextField
    {
        public new class UxmlFactory : UxmlFactory<OldText, UxmlTraits> { }
        public OldText()
        {
            label = "ctorLabel";
            value = "ctorValue";
            isReadOnly = true;
            maxLength = 5;
            isPasswordField = true;
            maskChar = '#';
            isDelayed = true;
            multiline = true;
            textEdition.placeholder = "ctorPlaceholder";
            textEdition.hidePlaceholderOnFocus = true;
            textSelection.selectAllOnFocus = false;
            textSelection.selectAllOnMouseUp = false;
            doubleClickSelectsWord = false;
            tripleClickSelectsLine = false;
            verticalScrollerVisibility = ScrollerVisibility.AlwaysVisible;
        }
    }

    public class OldButton : Button
    {
        public new class UxmlFactory : UxmlFactory<OldButton, UxmlTraits> { }
        public OldButton() { text = "ctorText"; focusable = false; tabIndex = 4; }
    }

    public class OldToggle : Toggle
    {
        public new class UxmlFactory : UxmlFactory<OldToggle, UxmlTraits> { }
        public OldToggle() { label = "ctorLabel"; text = "ctorText"; value = true; }
    }

    public class OldVector3 : Vector3Field
    {
        public new class UxmlFactory : UxmlFactory<OldVector3, UxmlTraits> { }
        public OldVector3() { label = "ctorLabel"; value = new Vector3(1, 2, 3); }
    }

    public class OldSlider : Slider
    {
        public new class UxmlFactory : UxmlFactory<OldSlider, UxmlTraits> { }
        public OldSlider() { lowValue = 2; highValue = 8; value = 5; pageSize = 1; showInputField = true; inverted = true; direction = SliderDirection.Vertical; }
    }

    public class OldIntegerField : IntegerField
    {
        public new class UxmlFactory : UxmlFactory<OldIntegerField, UxmlTraits> { }
        public OldIntegerField() { value = 42; label = "ctorLabel"; isDelayed = true; }
    }

    public class OldScrollView : ScrollView
    {
        public new class UxmlFactory : UxmlFactory<OldScrollView, UxmlTraits> { }
        public OldScrollView()
        {
            mode = ScrollViewMode.Horizontal;
            horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            verticalScrollerVisibility = ScrollerVisibility.AlwaysVisible;
            elasticity = 0.5f;
            scrollDecelerationRate = 0.3f;
            touchScrollBehavior = ScrollView.TouchScrollBehavior.Clamped;
            horizontalPageSize = 7;
            verticalPageSize = 9;
            mouseWheelScrollSize = 11;
        }
    }

    public class OldListView : ListView
    {
        public new class UxmlFactory : UxmlFactory<OldListView, UxmlTraits> { }
        public OldListView()
        {
            fixedItemHeight = 40;
            reorderable = true;
            showBorder = true;
            selectionType = SelectionType.Multiple;
            showAlternatingRowBackgrounds = AlternatingRowBackground.All;
            showFoldoutHeader = true;
            headerTitle = "ctorTitle";
            showAddRemoveFooter = true;
            horizontalScrollingEnabled = true;
            virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight;
        }
    }

    public class OldMultiColumn : MultiColumnListView
    {
        public new class UxmlFactory : UxmlFactory<OldMultiColumn, UxmlTraits> { }
        public OldMultiColumn()
        {
            sortingMode = ColumnSortingMode.Custom;
            columns.Add(new Column { name = "c", title = "C" });
            sortColumnDescriptions.Add(new SortColumnDescription("c", SortDirection.Ascending));
        }
    }

    public class OldFoldout : Foldout
    {
        public new class UxmlFactory : UxmlFactory<OldFoldout, UxmlTraits> { }
        public OldFoldout() { text = "ctorText"; value = false; }
    }

    public class OldLabel : Label
    {
        public new class UxmlFactory : UxmlFactory<OldLabel, UxmlTraits> { }
        public OldLabel() { text = "ctorText"; enableRichText = false; displayTooltipWhenElided = false; parseEscapeSequences = true; }
    }

    public class OldDropdown : DropdownField
    {
        public new class UxmlFactory : UxmlFactory<OldDropdown, UxmlTraits> { }
        public OldDropdown() { choices = new List<string> { "a", "b", "c" }; index = 2; label = "ctorLabel"; }
    }

    public class OldProgress : ProgressBar
    {
        public new class UxmlFactory : UxmlFactory<OldProgress, UxmlTraits> { }
        public OldProgress() { lowValue = 10; highValue = 20; value = 15; title = "ctorTitle"; }
    }

    // A game traits class over a built-in one: its own attribute after the base's.
    public class CustomButton : Button
    {
        public int number = -1;
        public new class UxmlFactory : UxmlFactory<CustomButton, UxmlTraits> { }
        public new class UxmlTraits : Button.UxmlTraits
        {
            readonly UxmlIntAttributeDescription m_Number = new UxmlIntAttributeDescription { name = "number", defaultValue = 9 };
            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                ((CustomButton)ve).number = m_Number.GetValueFromBag(bag, cc);
            }
        }
        public CustomButton() { text = "ctorText"; }
    }
}

public class UxmlLegacyProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod] static void Install() { new GameObject("UxmlLegacyProbe").AddComponent<UxmlLegacyProbe>(); }
    [Serializable] class Row { public string key; public string value; }
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    StreamWriter output;

    void Put(string key, string value) => output.WriteLine(JsonUtility.ToJson(new Row { key = key, value = value }));

    static string S(string s) => s == null ? "<null>" : "\"" + s + "\"";
    static string F(float v) => v.ToString("R", Inv);

    static string Base(VisualElement e) =>
        "name " + S(e.name) + " key " + S(e.viewDataKey) + " tip " + S(e.tooltip) + " picking " + e.pickingMode + " hints " + e.usageHints
        + " focusable " + e.focusable + " tab " + e.tabIndex + " enabled " + e.enabledSelf;

    static string Field<T>(BaseField<T> f) => " label " + S(f.label) + " value " + S(Convert.ToString(f.value, Inv));

    static string Text(TextInputBaseField<string> t) =>
        " readOnly " + t.isReadOnly + " maxLength " + t.maxLength + " password " + t.isPasswordField + " mask " + (int)t.maskChar + " delayed " + t.isDelayed
        + " placeholder " + S(t.textEdition.placeholder) + " hideOnFocus " + t.textEdition.hidePlaceholderOnFocus
        + " allOnFocus " + t.textSelection.selectAllOnFocus + " allOnMouseUp " + t.textSelection.selectAllOnMouseUp
        + " word " + t.doubleClickSelectsWord + " line " + t.tripleClickSelectsLine + " vScroller " + t.verticalScrollerVisibility;

    static string State(VisualElement e)
    {
        var s = Base(e);
        switch (e)
        {
            case LegacyProbe.OldText t: s += Field(t) + Text(t) + " multiline " + t.multiline; break;
            case TextInputBaseField<string> t: s += Field(t) + Text(t); break;
            case EnumField ef: s += " label " + S(ef.label) + " value " + (ef.value == null ? "<null>" : ef.value.GetType().FullName + "." + ef.value); break;
            case LegacyProbe.CustomButton c: s += " text " + S(c.text) + " number " + c.number; break;
            case Button b: s += " text " + S(b.text); break;
            case Toggle t: s += Field(t) + " text " + S(t.text); break;
            case Vector3Field v: s += Field(v); break;
            case Slider sl: s += Field(sl) + " low " + F(sl.lowValue) + " high " + F(sl.highValue) + " page " + F(sl.pageSize) + " input " + sl.showInputField + " inverted " + sl.inverted + " direction " + sl.direction; break;
            case IntegerField i: s += Field(i) + " delayed " + i.isDelayed + " placeholder " + S(i.textEdition.placeholder) + " hideOnFocus " + i.textEdition.hidePlaceholderOnFocus; break;
            case ScrollView v: s += " mode " + v.mode + " h " + v.horizontalScrollerVisibility + " v " + v.verticalScrollerVisibility + " elasticity " + F(v.elasticity)
                + " deceleration " + F(v.scrollDecelerationRate) + " touch " + v.touchScrollBehavior + " hPage " + F(v.horizontalPageSize) + " vPage " + F(v.verticalPageSize)
                + " wheel " + F(v.mouseWheelScrollSize) + " nested " + v.nestedInteractionKind; break;
            case MultiColumnListView m: s += " sorting " + m.sortingMode + " columns " + m.columns.Count + " sorts " + m.sortColumnDescriptions.Count; break;
            case ListView l: s += " height " + F(l.fixedItemHeight) + " reorderable " + l.reorderable + " border " + l.showBorder + " selection " + l.selectionType
                + " alternating " + l.showAlternatingRowBackgrounds + " foldout " + l.showFoldoutHeader + " title " + S(l.headerTitle) + " footer " + l.showAddRemoveFooter
                + " horizontal " + l.horizontalScrollingEnabled + " virtualization " + l.virtualizationMethod + " reorderMode " + l.reorderMode + " binding " + S(l.bindingPath); break;
            case Foldout f: s += " text " + S(f.text) + " value " + f.value; break;
            case DropdownField d: s += Field(d) + " index " + d.index + " choices " + string.Join("|", d.choices ?? new List<string>()); break;
            case ProgressBar p: s += " low " + F(p.lowValue) + " high " + F(p.highValue) + " value " + F(p.value) + " title " + S(p.title); break;
            case TextElement t: s += " text " + S(t.text) + " rich " + t.enableRichText + " elided " + t.displayTooltipWhenElided + " escape " + t.parseEscapeSequences
                + " selectable " + t.selection.isSelectable + " word " + t.selection.doubleClickSelectsWord + " line " + t.selection.tripleClickSelectsLine; break;
            case BindableElement b: s += " binding " + S(b.bindingPath); break;
        }
        return s;
    }

    static string Description(UxmlAttributeDescription d) =>
        d.GetType().Name + " " + S(d.name) + " default " + S(d.defaultValueAsString) + " obsolete " + string.Join("|", d.obsoleteNames ?? Enumerable.Empty<string>())
        + " use " + d.use + " type " + S(d.type);

    void Start()
    {
        string path = Environment.GetEnvironmentVariable("UXML_LEGACY_OUT"); if (string.IsNullOrEmpty(path)) return;
        output = new StreamWriter(path);
        bool failed = false;
        try { Record(); }
        catch (Exception ex) { Debug.LogException(ex); failed = true; }
        if (!failed) Put("complete", "1");
        output.Dispose();
        EditorApplication.Exit(failed ? 1 : 0);
    }

    void Record()
    {
        var root = new VisualElement();
        Resources.Load<VisualTreeAsset>("Legacy").CloneTree(root);
        foreach (var e in root.Query<VisualElement>().ToList())
        {
            if (e == root) continue;
            if (e.ClassListContains("case")) Put("element." + e.GetClasses().First(c => c != "case" && !c.StartsWith("unity-", StringComparison.Ordinal)), State(e));
        }

        // Controls: a text field's vertical scroller visibility through multiline and password changes.
        var multi = new TextField { multiline = true };
        multi.verticalScrollerVisibility = ScrollerVisibility.AlwaysVisible;
        var before = multi.verticalScrollerVisibility;
        multi.multiline = false;
        Put("control.scrollerSingleLine", before + " -> " + multi.verticalScrollerVisibility);
        var password = new TextField { isPasswordField = true };
        password.multiline = true;
        password.verticalScrollerVisibility = ScrollerVisibility.AlwaysVisible;
        Put("control.scrollerPassword", "multiline " + password.multiline + " " + password.verticalScrollerVisibility);
        var unset = new TextField { multiline = true };
        unset.verticalScrollerVisibility = ScrollerVisibility.AlwaysVisible;
        unset.isPasswordField = false;
        unset.isPasswordField = true;
        Put("control.scrollerPasswordLater", "multiline " + unset.multiline + " " + unset.verticalScrollerVisibility);

        // The descriptions of every built-in traits class, in the order they are listed.
        var traitsTypes = typeof(VisualElement).Assembly.GetTypes()
            .Where(t => t.Namespace == "UnityEngine.UIElements" && typeof(UxmlTraits).IsAssignableFrom(t) && !t.IsAbstract && !t.ContainsGenericParameters && t.IsPublic | t.IsNestedPublic && t.GetConstructor(Type.EmptyTypes) != null)
            .OrderBy(t => t.FullName, StringComparer.Ordinal);
        foreach (var type in traitsTypes)
        {
            var traits = (UxmlTraits)Activator.CreateInstance(type);
            int i = 0;
            foreach (var d in traits.uxmlAttributesDescription) Put("traits." + type.FullName + "." + i++, Description(d));
            Put("traits." + type.FullName + ".count", i.ToString(Inv) + " anyAttribute " + traits.canHaveAnyAttribute
                + " children " + string.Join("|", traits.uxmlChildElementsDescription.Select(c => c.elementName)));
        }
    }
}
