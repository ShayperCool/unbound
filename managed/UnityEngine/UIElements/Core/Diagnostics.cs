using System;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unbound.UIElements
{
    /// <summary>Debugging aid: with UNBOUND_UITK_DUMP set to a class or name, each layout pass logs the
    /// computed style and layout node of the matching elements.</summary>
    internal static class UaDiagnostics
    {
        static readonly string s_Filter = Environment.GetEnvironmentVariable("UNBOUND_UITK_DUMP");

        internal static void AfterLayout(VisualElement root)
        {
            if (string.IsNullOrEmpty(s_Filter)) return;
            Visit(root);
        }

        static void Visit(VisualElement e)
        {
            if (e.name == s_Filter || e.ClassListContains(s_Filter)) Dump(e);
            for (int i = 0; i < e.hierarchy.childCount; i++) Visit(e.hierarchy[i]);
        }

        static void Dump(VisualElement e)
        {
            var s = new StringBuilder();
            s.Append("[uitk] ").Append(e.GetType().Name).Append('#').Append(e.name).Append(' ').Append(string.Join(".", e.GetClasses()))
                .Append(" layout ").Append(e.layout).Append('\n');
            foreach (var f in typeof(ComputedValues).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                s.Append("  ").Append(f.Name).Append(" = ").Append(f.GetValue(e.computedValues)).Append('\n');
            var n = e.yogaNode;
            s.Append("  yoga: w=").Append(n.Width).Append(" h=").Append(n.Height).Append(" basis=").Append(n.FlexBasis)
                .Append(" grow=").Append(n.FlexGrow).Append(" shrink=").Append(n.FlexShrink).Append(" minW=").Append(n.MinWidth)
                .Append(" measure=").Append(n.MeasureFunction != null);
            Debug.Log(s.ToString());
        }
    }
}
