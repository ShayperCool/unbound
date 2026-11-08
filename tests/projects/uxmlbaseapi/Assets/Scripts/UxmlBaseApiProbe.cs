#pragma warning disable CS0618
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace UxmlBaseApiControl
{
    public sealed class EmptyTraits : UxmlTraits { }
    public sealed class BaseTraits : BaseUxmlTraits { }
    public sealed class ProbeElement : VisualElement { }
    public sealed class ProbeTraits : UxmlTraits
    {
        readonly UxmlStringAttributeDescription value = new UxmlStringAttributeDescription { name = "probe-value" };
        public override IEnumerable<UxmlChildElementDescription> uxmlChildElementsDescription
        {
            get { yield return new UxmlChildElementDescription(typeof(Label)); }
        }
        public override void Init(VisualElement element, IUxmlAttributes bag, CreationContext context)
        {
            base.Init(element, bag, context);
            element.name = value.GetValueFromBag(bag, context);
        }
    }
    public sealed class SelectingFactory : UxmlFactory<ProbeElement, ProbeTraits>
    {
        public override bool AcceptsAttributeBag(IUxmlAttributes bag, CreationContext context) =>
            bag != null && bag.TryGetAttributeValue("probe-value", out var text) && text == "accepted";
    }
    public sealed class ProbeBag : IUxmlAttributes
    {
        readonly string value;
        public ProbeBag(string text) { value = text; }
        public bool TryGetAttributeValue(string name, out string result)
        {
            result = name == "probe-value" ? value : null;
            return result != null;
        }
    }

    public static class UxmlBaseApiProbe
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Run()
        {
            var output = Environment.GetEnvironmentVariable("UXML_BASE_API_OUT");
            if (string.IsNullOrEmpty(output)) return;
            Record(output);
            Application.Quit();
        }
        static string Names(IEnumerable<UxmlAttributeDescription> values) =>
            string.Join(",", values.Select(value => value.name).OrderBy(name => name, StringComparer.Ordinal));
        static string Children(IEnumerable<UxmlChildElementDescription> values) =>
            string.Join(",", values.Select(value => value.elementNamespace + "." + value.elementName).OrderBy(name => name, StringComparer.Ordinal));
        static string Bool(bool value) => value ? "true" : "false";
        public static void Record(string output)
        {
            var plain = new UxmlFactory<VisualElement, EmptyTraits>();
            var custom = new UxmlFactory<ProbeElement, ProbeTraits>();
            var selecting = new SelectingFactory();
            var bag = new ProbeBag("accepted");
            var element = new VisualElement { name = "keep", tooltip = "keep-tooltip" };
            new EmptyTraits().Init(element, bag, default);
            string init = element.name + "," + element.tooltip + "," + element.childCount;
            new EmptyTraits().Init(null, null, default);
            Directory.CreateDirectory(output);
            var values = new Dictionary<string,string> {
                ["factory.accepts.empty"] = Bool(plain.AcceptsAttributeBag(new ProbeBag(null), default)),
                ["factory.accepts.null"] = Bool(plain.AcceptsAttributeBag(null, default)),
                ["factory.attributes.empty"] = Names(plain.uxmlAttributesDescription),
                ["factory.attributes.custom"] = Names(custom.uxmlAttributesDescription),
                ["factory.children.empty"] = Children(plain.uxmlChildElementsDescription),
                ["factory.children.custom"] = Children(custom.uxmlChildElementsDescription),
                ["traits.children.base"] = Children(new BaseTraits().uxmlChildElementsDescription),
                ["traits.init.base"] = init,
                ["override.accepts.true"] = Bool(selecting.AcceptsAttributeBag(bag, default)),
                ["override.accepts.false"] = Bool(selecting.AcceptsAttributeBag(new ProbeBag("rejected"), default)),
                ["override.create"] = custom.Create(bag, default).name,
                ["control.substitute.exact"] = plain.substituteForTypeQualifiedName,
                ["control.substitute.derived"] = custom.substituteForTypeQualifiedName,
                ["control.attributes.builtin"] = Bool(new VisualElement.UxmlFactory().uxmlAttributesDescription.Any()),
                ["control.children.builtin"] = Children(new VisualElement.UxmlFactory().uxmlChildElementsDescription),
            };
            File.WriteAllLines(Path.Combine(output,"values.tsv"),values.Select(pair => pair.Key + "\t" + pair.Value));
        }
    }
}
