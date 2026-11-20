using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UxmlSerializedDataProbes;

/// <summary>
/// Records Unity 6 UxmlSerializedData / ShouldWriteAttributeValue behavior, source-generated
/// [UxmlElement]/[UxmlAttribute]/[UxmlObject] serialized selection (absent vs specified vs
/// default value), built-in base UxmlSerializedData types, metadata/cache registration,
/// UxmlSerializedDataUtility.CopySerialized, and an ordinary VisualTreeAsset.CloneTree control.
/// </summary>
static class UxmlSerializedDataProbe
{
    static readonly MethodInfo s_DeserializeWithFlags = typeof(UxmlSerializedData).GetMethod(
        "Deserialize",
        BindingFlags.Instance | BindingFlags.NonPublic,
        null,
        new[] { typeof(object), typeof(UxmlSerializedData.UxmlAttributeFlags) },
        null);

    static readonly MethodInfo s_TryGetCachedDescription = typeof(UxmlDescriptionCache).GetMethod(
        "TryGetCachedDescription",
        BindingFlags.Static | BindingFlags.NonPublic);

    static void SetField(object target, string fieldName, object value)
    {
        for (var t = target.GetType(); t != null; t = t.BaseType)
        {
            var f = t.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (f != null)
            {
                f.SetValue(target, value);
                return;
            }
        }
        throw new MissingFieldException(target.GetType().FullName, fieldName);
    }

    static string FormatCounter(CustomCounter c) =>
        $"name={c.name};tooltip={c.tooltip};enabled={c.enabledSelf};count={c.count};title={c.title ?? "<null>"};active={c.active};numbers={(c.numbers == null ? "<null>" : string.Join(",", c.numbers))};payload={(c.payload == null ? "<null>" : c.payload.ToString())}";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Record()
    {
        var directory = Environment.GetEnvironmentVariable("UXML_SERIALIZED_DATA_OUT");
        if (string.IsNullOrEmpty(directory)) return;
        var output = new StringBuilder();
        void Put(string key, string value) => output.Append("{\"k\":").Append(Quote(key)).Append(",\"v\":").Append(Quote(value)).Append("}\n");
        void Check(string key, Func<string> read)
        {
            try { Put(key, read()); }
            catch (Exception e)
            {
                var ex = e is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : e;
                Put(key, "throws " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        // 1. UxmlAttributeFlags numeric values and ShouldWriteAttributeValue defaults.
        Put("flags.ignore", ((byte)UxmlSerializedData.UxmlAttributeFlags.Ignore).ToString());
        Put("flags.overridden", ((byte)UxmlSerializedData.UxmlAttributeFlags.OverriddenInUxml).ToString());
        Put("flags.defaultValue", ((byte)UxmlSerializedData.UxmlAttributeFlags.DefaultValue).ToString());
        Put("shouldWrite.default.ignore", UxmlSerializedData.ShouldWriteAttributeValue(UxmlSerializedData.UxmlAttributeFlags.Ignore).ToString());
        Put("shouldWrite.default.overridden", UxmlSerializedData.ShouldWriteAttributeValue(UxmlSerializedData.UxmlAttributeFlags.OverriddenInUxml).ToString());
        Put("shouldWrite.default.defaultValue", UxmlSerializedData.ShouldWriteAttributeValue(UxmlSerializedData.UxmlAttributeFlags.DefaultValue).ToString());
        Put("shouldWrite.default.both", UxmlSerializedData.ShouldWriteAttributeValue(
            UxmlSerializedData.UxmlAttributeFlags.OverriddenInUxml | UxmlSerializedData.UxmlAttributeFlags.DefaultValue).ToString());

        // 2. UxmlSerializedData.Register() no-op and UxmlAttributeNames / UxmlIgnore / RegisterUxmlCache / UxmlObjectReferenceAttribute.
        Check("register.base", () => { UxmlSerializedData.Register(); return "ok"; });
        Check("attrNames.default", () =>
        {
            var n = default(UxmlAttributeNames);
            return $"{n.fieldName ?? "<null>"}|{n.uxmlName ?? "<null>"}|{(n.typeReference == null ? "<null>" : n.typeReference.Name)}|{(n.obsoleteNames == null ? "<null>" : n.obsoleteNames.Length.ToString())}";
        });
        Check("attrNames.nullObsolete", () =>
        {
            var n = new UxmlAttributeNames("count", "count", null, null);
            return $"{n.fieldName}|{n.uxmlName}|{(n.typeReference == null ? "<null>" : n.typeReference.Name)}|{(n.obsoleteNames == null ? "<null>" : n.obsoleteNames.Length.ToString())}";
        });
        Check("attrNames.full", () =>
        {
            var n = new UxmlAttributeNames("title", "display-title", typeof(string), "old-title", "legacy-title");
            return $"{n.fieldName}|{n.uxmlName}|{n.typeReference.Name}|{string.Join(",", n.obsoleteNames)}";
        });
        Check("attr.uxmlIgnore.usage", () =>
        {
            var u = (AttributeUsageAttribute)Attribute.GetCustomAttribute(typeof(UxmlIgnoreAttribute), typeof(AttributeUsageAttribute));
            var f = typeof(CustomCounter.UxmlSerializedData).GetField("count_UxmlAttributeFlags", BindingFlags.Instance | BindingFlags.NonPublic);
            return $"{u.ValidOn}|{u.Inherited}|hasOnFlags={f?.IsDefined(typeof(UxmlIgnoreAttribute), false)}";
        });
        Check("attr.registerCache.usage", () =>
        {
            var u = (AttributeUsageAttribute)Attribute.GetCustomAttribute(typeof(RegisterUxmlCacheAttribute), typeof(AttributeUsageAttribute));
            var m = typeof(CustomCounter.UxmlSerializedData).GetMethod("Register", BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly);
            return $"{u.ValidOn}|{u.Inherited}|hasOnRegister={m?.IsDefined(typeof(RegisterUxmlCacheAttribute), false)}";
        });
        Check("attr.objectRef.default", () =>
        {
            var a = new UxmlObjectReferenceAttribute();
            return $"{a.name ?? "<null>"}|{(a.types == null ? "<null>" : a.types.Length.ToString())}";
        });
        Check("attr.objectRef.named", () =>
        {
            var a = new UxmlObjectReferenceAttribute("items");
            return $"{a.name}|{(a.types == null ? "<null>" : a.types.Length.ToString())}";
        });
        Check("attr.objectRef.namedAndTypes", () =>
        {
            var a = new UxmlObjectReferenceAttribute("items", typeof(CustomBinding));
            return $"{a.name}|{a.types.Length}:{a.types[0].Name}";
        });
        Check("attr.objectRef.nullLiteral1", () =>
        {
            var a = new UxmlObjectReferenceAttribute("null");
            return a.name;
        });
        Check("attr.objectRef.nullLiteral2", () =>
        {
            var a = new UxmlObjectReferenceAttribute("null", typeof(CustomBinding));
            return $"{a.name}|{a.types.Length}";
        });

        // 3. UxmlDescriptionCache registration via source-generated Register().
        Check("cache.customCounter", () =>
        {
            CustomCounter.UxmlSerializedData.Register();
            var args = new object[] { typeof(CustomCounter.UxmlSerializedData), null };
            bool found = (bool)s_TryGetCachedDescription.Invoke(null, args);
            var attrs = (UxmlAttributeNames[])args[1];
            var parts = new List<string>();
            if (attrs != null)
            {
                foreach (var a in attrs)
                    parts.Add($"{a.fieldName}:{a.uxmlName}:{string.Join("+", a.obsoleteNames ?? Array.Empty<string>())}");
            }
            return $"found={found};count={attrs?.Length ?? -1};{string.Join("|", parts)}";
        });

        // 4. Custom [UxmlElement] source-generated UxmlSerializedData: absent vs specified vs default value.
        Check("custom.createInstance", () =>
        {
            var data = new CustomCounter.UxmlSerializedData();
            var inst = (CustomCounter)data.CreateInstance();
            return FormatCounter(inst);
        });
        Check("custom.absentPreservesDefaults", () =>
        {
            var data = new CustomCounter.UxmlSerializedData();
            SetField(data, "count", 0);
            SetField(data, "title", "");
            SetField(data, "active", false);
            SetField(data, "numbers", new List<int> { 1 });
            SetField(data, "payload", new CustomPayload { label = "ignored", value = 0 });
            SetField(data, "name", "ignored-name");
            // All *_UxmlAttributeFlags remain Ignore (0).
            var target = (CustomCounter)data.CreateInstance();
            target.name = "preset";
            target.tooltip = "preset-tip";
            data.Deserialize(target);
            return FormatCounter(target);
        });
        Check("custom.specifiedZeroAndEmptyOverwrite", () =>
        {
            var data = new CustomCounter.UxmlSerializedData();
            SetField(data, "count", 0);
            SetField(data, "count_UxmlAttributeFlags", UxmlSerializedData.UxmlAttributeFlags.OverriddenInUxml);
            SetField(data, "title", "");
            SetField(data, "title_UxmlAttributeFlags", UxmlSerializedData.UxmlAttributeFlags.OverriddenInUxml);
            SetField(data, "active", false);
            SetField(data, "active_UxmlAttributeFlags", UxmlSerializedData.UxmlAttributeFlags.OverriddenInUxml);
            SetField(data, "name", "overridden-name");
            SetField(data, "name_UxmlAttributeFlags", UxmlSerializedData.UxmlAttributeFlags.OverriddenInUxml);
            // numbers and payload stay Ignore -> keep constructor defaults.
            var target = (CustomCounter)data.CreateInstance();
            data.Deserialize(target);
            return FormatCounter(target);
        });
        Check("custom.defaultFlagSelection", () =>
        {
            var data = new CustomCounter.UxmlSerializedData();
            SetField(data, "count", 100);
            SetField(data, "count_UxmlAttributeFlags", UxmlSerializedData.UxmlAttributeFlags.OverriddenInUxml);
            SetField(data, "title", "from-default-flag");
            SetField(data, "title_UxmlAttributeFlags", UxmlSerializedData.UxmlAttributeFlags.DefaultValue);
            SetField(data, "active", false);
            SetField(data, "active_UxmlAttributeFlags", UxmlSerializedData.UxmlAttributeFlags.Ignore);

            var normal = (CustomCounter)data.CreateInstance();
            data.Deserialize(normal);

            var onlyDefault = (CustomCounter)data.CreateInstance();
            s_DeserializeWithFlags.Invoke(data, new object[] { onlyDefault, UxmlSerializedData.UxmlAttributeFlags.DefaultValue });

            var both = (CustomCounter)data.CreateInstance();
            s_DeserializeWithFlags.Invoke(data, new object[] { both, UxmlSerializedData.UxmlAttributeFlags.OverriddenInUxml | UxmlSerializedData.UxmlAttributeFlags.DefaultValue });

            return $"normal=({normal.count},{normal.title},{normal.active})|defaultOnly=({onlyDefault.count},{onlyDefault.title},{onlyDefault.active})|both=({both.count},{both.title},{both.active})|afterRestore={UxmlSerializedData.ShouldWriteAttributeValue(UxmlSerializedData.UxmlAttributeFlags.OverriddenInUxml)},{UxmlSerializedData.ShouldWriteAttributeValue(UxmlSerializedData.UxmlAttributeFlags.DefaultValue)}";
        });
        Check("custom.exceptionRestoresFlags", () =>
        {
            var throwing = new ThrowingSerializedData();
            string caught = "none";
            try
            {
                s_DeserializeWithFlags.Invoke(throwing, new object[] { new VisualElement(), UxmlSerializedData.UxmlAttributeFlags.DefaultValue });
            }
            catch (TargetInvocationException tie)
            {
                caught = tie.InnerException.GetType().Name + ":" + tie.InnerException.Message;
            }
            return $"caught={caught}|during=({throwing.sawDefaultDuringDeserialize},{throwing.sawOverrideDuringDeserialize})|after=({UxmlSerializedData.ShouldWriteAttributeValue(UxmlSerializedData.UxmlAttributeFlags.DefaultValue)},{UxmlSerializedData.ShouldWriteAttributeValue(UxmlSerializedData.UxmlAttributeFlags.OverriddenInUxml)})";
        });

        // 5. Reference isolation via UxmlSerializedDataUtility.CopySerialized.
        Check("copySerialized.null", () => (UxmlSerializedDataUtility.CopySerialized(null) == null).ToString());
        Check("copySerialized.direct", () =>
        {
            var src = new CustomPayload { label = "orig", value = 12 };
            var copy = UxmlSerializedDataUtility.CopySerialized<CustomPayload>(src);
            src.label = "changed";
            return $"distinct={!ReferenceEquals(src, copy)}|copy={copy}";
        });
        Check("copySerialized.throughDeserialize", () =>
        {
            var data = new CustomCounter.UxmlSerializedData();
            SetField(data, "payload", new CustomPayload { label = "shared-source", value = 55 });
            SetField(data, "payload_UxmlAttributeFlags", UxmlSerializedData.UxmlAttributeFlags.OverriddenInUxml);
            SetField(data, "numbers", new List<int> { 1, 2, 3 });
            SetField(data, "numbers_UxmlAttributeFlags", UxmlSerializedData.UxmlAttributeFlags.OverriddenInUxml);

            var a = (CustomCounter)data.CreateInstance();
            var b = (CustomCounter)data.CreateInstance();
            data.Deserialize(a);
            data.Deserialize(b);
            a.payload.label = "mutated-a";
            a.numbers.Add(99);
            return $"distinctPayload={!ReferenceEquals(a.payload, b.payload)}|distinctList={!ReferenceEquals(a.numbers, b.numbers)}|a={a.payload}/{string.Join(",", a.numbers)}|b={b.payload}/{string.Join(",", b.numbers)}";
        });

        // 6. Built-in base UxmlSerializedData types: VisualElement, BindableElement, TextElement, Image, Binding, CustomBinding, DataBinding.
        Check("builtin.visualElement", () =>
        {
            var data = new VisualElement.UxmlSerializedData();
            SetField(data, "name", "ve-name");
            SetField(data, "name_UxmlAttributeFlags", UxmlSerializedData.UxmlAttributeFlags.OverriddenInUxml);
            SetField(data, "enabledSelf", false);
            SetField(data, "enabledSelf_UxmlAttributeFlags", UxmlSerializedData.UxmlAttributeFlags.OverriddenInUxml);
            SetField(data, "viewDataKey", "vdk-1");
            SetField(data, "viewDataKey_UxmlAttributeFlags", UxmlSerializedData.UxmlAttributeFlags.OverriddenInUxml);
            SetField(data, "pickingMode", PickingMode.Ignore);
            SetField(data, "pickingMode_UxmlAttributeFlags", UxmlSerializedData.UxmlAttributeFlags.OverriddenInUxml);
            SetField(data, "usageHints", UsageHints.DynamicTransform);
            SetField(data, "usageHints_UxmlAttributeFlags", UxmlSerializedData.UxmlAttributeFlags.OverriddenInUxml);
            SetField(data, "tabIndex", 7);
            SetField(data, "tabIndex_UxmlAttributeFlags", UxmlSerializedData.UxmlAttributeFlags.OverriddenInUxml);
            SetField(data, "focusable", true);
            SetField(data, "focusable_UxmlAttributeFlags", UxmlSerializedData.UxmlAttributeFlags.OverriddenInUxml);
            SetField(data, "languageDirection", LanguageDirection.RTL);
            SetField(data, "languageDirection_UxmlAttributeFlags", UxmlSerializedData.UxmlAttributeFlags.OverriddenInUxml);
            SetField(data, "dataSourcePathString", "stats.hp");
            SetField(data, "dataSourcePathString_UxmlAttributeFlags", UxmlSerializedData.UxmlAttributeFlags.OverriddenInUxml);
            SetField(data, "dataSourceTypeString", "UnityEngine.UIElements.Button, UnityEngine.UIElementsModule");
            SetField(data, "dataSourceTypeString_UxmlAttributeFlags", UxmlSerializedData.UxmlAttributeFlags.OverriddenInUxml);
            // Leave tooltip Ignore on an element with a preset tooltip!
            var ve = (VisualElement)data.CreateInstance();
            ve.tooltip = "keep-me";
            data.Deserialize(ve);
            return $"{ve.name}|{ve.enabledSelf}|{ve.viewDataKey}|{ve.pickingMode}|{ve.usageHints}|{ve.tabIndex}|{ve.focusable}|{ve.languageDirection}|{ve.dataSourcePath}|{ve.dataSourceType?.Name ?? "<null>"}|{ve.tooltip}";
        });
        Check("builtin.bindableElement", () =>
        {
            var data = new DerivedBindable.UxmlSerializedData();
            SetField(data, "tag", "updated-tag");
            SetField(data, "tag_UxmlAttributeFlags", UxmlSerializedData.UxmlAttributeFlags.OverriddenInUxml);
            var absentPath = (DerivedBindable)data.CreateInstance();
            data.Deserialize(absentPath);

            SetField(data, "bindingPath", "override.path");
            SetField(data, "bindingPath_UxmlAttributeFlags", UxmlSerializedData.UxmlAttributeFlags.OverriddenInUxml);
            var withPath = (DerivedBindable)data.CreateInstance();
            data.Deserialize(withPath);
            return $"absent=({absentPath.bindingPath},{absentPath.tag})|override=({withPath.bindingPath},{withPath.tag})";
        });
        Check("builtin.textElement", () =>
        {
            var data = new CustomLabel.UxmlSerializedData();
            SetField(data, "level", 11);
            SetField(data, "level_UxmlAttributeFlags", UxmlSerializedData.UxmlAttributeFlags.OverriddenInUxml);
            var absentText = (CustomLabel)data.CreateInstance();
            data.Deserialize(absentText);

            SetField(data, "text", "serialized-text");
            SetField(data, "text_UxmlAttributeFlags", UxmlSerializedData.UxmlAttributeFlags.OverriddenInUxml);
            SetField(data, "enableRichText", false);
            SetField(data, "enableRichText_UxmlAttributeFlags", UxmlSerializedData.UxmlAttributeFlags.OverriddenInUxml);
            SetField(data, "displayTooltipWhenElided", false);
            SetField(data, "displayTooltipWhenElided_UxmlAttributeFlags", UxmlSerializedData.UxmlAttributeFlags.OverriddenInUxml);
            var withText = (CustomLabel)data.CreateInstance();
            data.Deserialize(withText);
            return $"absent=({absentText.text},{absentText.enableRichText},{absentText.level})|override=({withText.text},{withText.enableRichText},{withText.displayTooltipWhenElided},{withText.level})";
        });
        Check("builtin.image", () => new Image.UxmlSerializedData().CreateInstance().GetType().Name);
        Check("builtin.customBinding", () =>
        {
            var data = new ProbeCustomBinding.UxmlSerializedData();
            SetField(data, "property", "text");
            SetField(data, "property_UxmlAttributeFlags", UxmlSerializedData.UxmlAttributeFlags.OverriddenInUxml);
            SetField(data, "updateTrigger", BindingUpdateTrigger.WhenDirty);
            SetField(data, "updateTrigger_UxmlAttributeFlags", UxmlSerializedData.UxmlAttributeFlags.OverriddenInUxml);
            // prefix absent -> keeps "pre:"
            var absentPrefix = (ProbeCustomBinding)data.CreateInstance();
            data.Deserialize(absentPrefix);

            SetField(data, "prefix", "custom:");
            SetField(data, "prefix_UxmlAttributeFlags", UxmlSerializedData.UxmlAttributeFlags.OverriddenInUxml);
            var withPrefix = (ProbeCustomBinding)data.CreateInstance();
            data.Deserialize(withPrefix);
            return $"absent=({absentPrefix.updateTrigger},{absentPrefix.prefix})|override=({withPrefix.updateTrigger},{withPrefix.prefix})";
        });
        Check("builtin.dataBinding", () =>
        {
            var data = new DataBinding.UxmlSerializedData();
            SetField(data, "property", "text");
            SetField(data, "property_UxmlAttributeFlags", UxmlSerializedData.UxmlAttributeFlags.OverriddenInUxml);
            SetField(data, "dataSourcePathString", "user.name");
            SetField(data, "dataSourcePathString_UxmlAttributeFlags", UxmlSerializedData.UxmlAttributeFlags.OverriddenInUxml);
            SetField(data, "bindingMode", BindingMode.ToTarget);
            SetField(data, "bindingMode_UxmlAttributeFlags", UxmlSerializedData.UxmlAttributeFlags.OverriddenInUxml);
            var db = (DataBinding)data.CreateInstance();
            data.Deserialize(db);
            return $"{db.dataSourcePath}|{db.bindingMode}|{db.updateTrigger}";
        });

        // 7. Control of ordinary existing UXML (VisualTreeAsset.CloneTree).
        Check("uxml.control", () =>
        {
            var vta = Resources.Load<VisualTreeAsset>("UxmlSerializedDataProbe");
            if (vta == null) return "missing-vta";
            var root = vta.CloneTree();
            var label = root.Q<Label>("plain-label");
            var box = root.Q<VisualElement>("plain-box");
            var absent = root.Q<CustomCounter>("uxml-absent");
            var specified = root.Q<CustomCounter>("uxml-specified");
            var obsolete = root.Q<CustomCounter>("uxml-obsolete");
            var bindable = root.Q<DerivedBindable>("uxml-bindable");
            var bindableDef = root.Q<DerivedBindable>("uxml-bindable-default");
            var textEl = root.Q<CustomLabel>("uxml-text");
            Put("uxml.plainLabel", $"{label.text}|{label.tooltip}");
            Put("uxml.plainBox", $"{box.enabledSelf}");
            Put("uxml.customAbsent", FormatCounter(absent));
            Put("uxml.customSpecified", FormatCounter(specified));
            Put("uxml.customObsolete", FormatCounter(obsolete));
            Put("uxml.derivedBindable", $"{bindable.bindingPath}|{bindable.tag}");
            Put("uxml.derivedBindableDefault", $"{bindableDef.bindingPath}|{bindableDef.tag}");
            Put("uxml.customLabel", $"{textEl.text}|{textEl.level}");
            return "ok";
        });

        output.Append("{\"k\":\"complete\",\"v\":true}\n");
        File.WriteAllText(Path.Combine(directory, "values.jsonl"), output.ToString());
        EditorApplication.Exit(0);
    }

    static string Quote(string s)
    {
        var b = new StringBuilder("\"");
        foreach (char c in s ?? "<null>")
        {
            if (c == '"' || c == '\\') b.Append('\\').Append(c);
            else if (c < ' ') b.Append("\\u").Append(((int)c).ToString("x4"));
            else b.Append(c);
        }
        return b.Append('"').ToString();
    }
}
