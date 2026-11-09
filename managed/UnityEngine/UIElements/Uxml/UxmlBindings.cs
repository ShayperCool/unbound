using System;
using System.Collections.Generic;
using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

// Bindings declared in UXML: an element's <Bindings> child lists binding objects (DataBinding, or a
// game's [UxmlObject] binding type), each set on the element under its `property` attribute.
// Documented (Unity 6000.0 Manual "Define data binding in UXML", "Runtime data binding"; Scripting
// API DataBinding, Binding): attributes `property`, `update-trigger`, and for DataBinding
// `data-source-path`, `data-source` (an asset), `data-source-type`, `binding-mode`,
// `source-to-ui-converters` and `ui-to-source-converters` (comma-separated converter group ids);
// a game binding's [UxmlAttribute] members. Measured (Unity 6000.0.71f1 Editor,
// tests/projects/uitkbindings):
//   - converter group ids are looked up when the document is cloned: a group registered later is
//     not used; an unknown id is skipped silently; groups apply in order, a later group's converter
//     replacing an earlier one's for the same types;
//   - a UXML object that is not a binding (a Column) is dropped silently; a tag naming no UXML
//     object type is reported by the importer (UxmlImporter) and dropped; the other bindings of
//     the element are kept;
//   - a binding without `property` is set under the empty id and reported when the panel first
//     updates it (UaDataBindingSystem);
//   - two bindings of the same property: the last one is kept.
namespace Unbound.UIElements
{
    internal static class UxmlBindings
    {
        /// <summary>Creates the bindings of a &lt;Bindings&gt; node and sets them on the element.</summary>
        internal static void Apply(VisualTreeAsset asset, VisualElement element, UxmlNode bindings)
        {
            foreach (var node in bindings.Children)
            {
                var type = UxmlBuilder.BindingType(node);
                // Abstract types (Binding, CustomBinding) cannot be created; other UXML objects are
                // not bindings.
                if (type == null || type.IsAbstract) continue;
                Binding binding;
                try
                {
                    binding = (Binding)Activator.CreateInstance(type, true);
                }
                catch (Exception e)
                {
                    Debug.LogException(e is System.Reflection.TargetInvocationException { InnerException: { } inner } ? inner : e);
                    continue;
                }
                string property = null;
                var members = UxmlTypeIndex.MembersOf(type);
                foreach (var a in node.Attributes)
                {
                    if (a.Key == "property")
                    {
                        property = a.Value;
                        continue;
                    }
                    if (a.Key == "update-trigger")
                    {
                        binding.updateTrigger = (BindingUpdateTrigger)ReadEnum(asset, a.Value, typeof(BindingUpdateTrigger));
                        continue;
                    }
                    if (binding is DataBinding data && ApplyDataBindingAttribute(asset, node, data, a.Key, a.Value)) continue;
                    if (members.ByName.TryGetValue(a.Key, out var member) && member.Problem == null)
                        UxmlBuilder.ApplyMember(asset, node, binding, member, a.Key, a.Value);
                }
                element.SetBinding(new BindingId(property), binding);
            }
        }

        static object ReadEnum(VisualTreeAsset asset, string text, Type type)
        {
            UxmlAttributeValues.TryReadLenient(text, type, null, asset, out var value, out _);
            return value;
        }

        static bool ApplyDataBindingAttribute(VisualTreeAsset asset, UxmlNode node, DataBinding binding, string name, string text)
        {
            switch (name)
            {
                case "data-source-path":
                    binding.dataSourcePath = new PropertyPath(text);
                    return true;
                case "data-source":
                    if (UxmlBuilder.TryReadObjectReference(null, name, text, typeof(Object), nameof(DataBinding.dataSource), asset, node, out var source))
                        binding.dataSource = source;
                    return true;
                case "data-source-type":
                    binding.dataSourceType = UxmlAttributeValues.ReadTypeReference(node, name, text, typeof(object));
                    return true;
                case "binding-mode":
                    binding.bindingMode = (BindingMode)ReadEnum(asset, text, typeof(BindingMode));
                    return true;
                case "source-to-ui-converters":
                    ApplyConverterGroups(text, binding.ApplyConverterGroupToUI);
                    return true;
                case "ui-to-source-converters":
                    ApplyConverterGroups(text, binding.ApplyConverterGroupToSource);
                    return true;
            }
            return false;
        }

        static void ApplyConverterGroups(string ids, Action<ConverterGroup> apply)
        {
            if (string.IsNullOrWhiteSpace(ids)) return;
            foreach (var id in ids.Split(','))
                if (ConverterGroups.TryGetConverterGroup(id.Trim(), out var group)) apply(group);
        }
    }
}
