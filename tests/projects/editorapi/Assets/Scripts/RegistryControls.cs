using System;
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static class RegistryControls
{
    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    public class BaseTarget : ScriptableObject { }
    public class ChildTarget : BaseTarget { }
    public class OtherTarget : ScriptableObject { }
    public class GenericTarget<T> : ScriptableObject { }
    [CustomEditor(typeof(BaseTarget), true), CanEditMultipleObjects]
    public class BaseInspector : Editor { }
    [CustomEditor(typeof(ChildTarget), isFallback = true)]
    public class FallbackInspector : Editor { }
    [CustomEditor(typeof(GenericTarget<>), true)]
    public class GenericInspector : Editor { }
    public class DynamicInspector : Editor { }
    public class FallbackTarget : ScriptableObject { }
    [CustomEditor(typeof(FallbackTarget), isFallback = true)]
    public class OnlyFallback : Editor { }
    public class PipelineTarget : ScriptableObject { }
    public class ProbePipeline : RenderPipelineAsset
    {
        protected override RenderPipeline CreatePipeline() => null;
    }
    [CustomEditor(typeof(PipelineTarget)), SupportedOnRenderPipeline(typeof(ProbePipeline))]
    public class PipelineInspector : Editor { }
    public class InheritedPipelineTarget : ScriptableObject { }
    [CustomEditor(typeof(InheritedPipelineTarget))]
    public class InheritedPipelineInspector : PipelineInspector { }
    public class OrderedTarget : ScriptableObject { }
    [CustomEditor(typeof(OrderedTarget))] public class ZInspector : Editor { }
    [CustomEditor(typeof(OrderedTarget))] public class AInspector : Editor { }

    public static void Run(Action<string, Func<object>> check)
    {
        Type registry = typeof(Editor).Assembly.GetType("UnityEditor.CustomEditorAttributes");
        MethodInfo find = registry.GetMethod("FindCustomEditorTypeByType", All);
        MethodInfo rebuild = registry.GetMethod("Rebuild", All);
        Func<Type, bool, object> lookup = (type, multi) => ((Type)find.Invoke(null, new object[] { type, multi }))?.FullName ?? "<null>";
        check("registry.rebuild", () => { rebuild.Invoke(null, null); return "ok"; });
        check("registry.fallbackOnly", () => lookup(typeof(FallbackTarget), false));
        check("registry.fallbackNotMulti", () => lookup(typeof(FallbackTarget), true));
        check("registry.pipelineUnsupported", () => lookup(typeof(PipelineTarget), false));
        check("registry.pipelineAttributeNotInherited", () => lookup(typeof(InheritedPipelineTarget), false));
        check("registry.sorted", () => lookup(typeof(OrderedTarget), false));
        check("registry.exact", () => lookup(typeof(BaseTarget), false));
        check("registry.inheritedRegularBeforeFallback", () => lookup(typeof(ChildTarget), false));
        check("registry.multiInherited", () => lookup(typeof(ChildTarget), true));
        check("registry.generic", () => lookup(typeof(GenericTarget<int>), false));
        check("registry.genericMulti", () => lookup(typeof(GenericTarget<int>), true));
        check("registry.null", () => lookup(null, false));
        check("registry.unregistered", () => lookup(typeof(OtherTarget), false));
        check("registry.dynamic", () => {
            object instance = registry.GetProperty("instance", All).GetValue(null);
            object cache = registry.GetField("m_Cache", All).GetValue(instance);
            IDictionary dict = (IDictionary)cache.GetType().GetField("m_CustomEditorCache", All).GetValue(cache);
            Type storageType = registry.GetNestedType("MonoEditorTypeStorage", All);
            object storage = Activator.CreateInstance(storageType);
            foreach (string field in new[] { "customEditors", "customEditorsMultiEdition" })
                storageType.GetField(field, All).SetValue(storage, Activator.CreateInstance(storageType.GetField(field, All).FieldType));
            dict.Add(typeof(OtherTarget), storage);
            object entry = Activator.CreateInstance(registry.GetNestedType("MonoEditorType", All));
            foreach (var pair in new[] { new object[] { "inspectorType", typeof(DynamicInspector) }, new object[] { "editorForChildClasses", false }, new object[] { "isFallback", false } })
                entry.GetType().GetField((string)pair[0], All).SetValue(entry, pair[1]);
            ((IList)storageType.GetField("customEditors", All).GetValue(storage)).Insert(0, entry);
            return lookup(typeof(OtherTarget), false) + ":" + lookup(typeof(OtherTarget), true);
        });
        check("registry.dynamicCreate", () => {
            var target = ScriptableObject.CreateInstance<OtherTarget>();
            Editor editor = null;
            try { editor = Editor.CreateEditor(target); return editor?.GetType().FullName; }
            finally { if (editor != null) Object.DestroyImmediate(editor); Object.DestroyImmediate(target); }
        });
        check("registry.rebuildRemovesDynamic", () => { rebuild.Invoke(null, null); return lookup(typeof(OtherTarget), false); });
        check("registry.rebuildRetainsAttribute", () => lookup(typeof(BaseTarget), false));
    }
}
