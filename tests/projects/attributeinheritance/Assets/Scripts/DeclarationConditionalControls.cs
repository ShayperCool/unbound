#undef ENABLE_PROFILER
#undef UNITY_EDITOR
using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using Unity.Profiling;
using Unity.Collections;

public static class DeclarationConditionalControls
{
    static int effects;
    static T Argument<T>(T value) { effects++; return value; }
    public static void Record(Action<string, object> row)
    {
        effects = 0;
        Profiler.SetAreaEnabled(Argument(ProfilerArea.CPU), true);
        Profiler.SetCategoryEnabled(Argument(ProfilerCategory.Scripts), true);
        Profiler.GetAllCategories(Argument(new ProfilerCategory[0]));
        Profiler.GetAllCategories(Argument(default(NativeArray<ProfilerCategory>)));
        Profiler.EmitFrameMetaData(Guid.Empty, 1, Argument((Array)new int[0]));
        Profiler.EmitFrameMetaData(Guid.Empty, 1, Argument(new System.Collections.Generic.List<int>()));
        Profiler.EmitFrameMetaData(Guid.Empty, 1, Argument(default(NativeArray<int>)));
        Profiler.AddFramesFromFile(Argument("no-file-needed"));
        ((CommandBuffer)null).BeginSample(Argument(default(ProfilerMarker)));
        ((CommandBuffer)null).EndSample(Argument(default(ProfilerMarker)));
        row("conditional.undefined.argument-effects", effects);
        using (var buffer = new CommandBuffer())
        {
            buffer.BeginSample(Argument("unconditional-string-overload"));
            buffer.EndSample(Argument("unconditional-string-overload"));
        }
        row("conditional.undefined.string-overload-effects", effects);
        Inspect(row, typeof(Profiler), "SetAreaEnabled", "SetCategoryEnabled", "GetAllCategories", "EmitFrameMetaData", "AddFramesFromFile");
        Inspect(row, typeof(CommandBuffer), "BeginSample", "EndSample");
        Inspect(row, typeof(CullingResults), "Validate");
        Inspect(row, typeof(UnityEngine.Jobs.TransformAccess), "CheckHierarchyValid");
        Inspect(row, typeof(UnityEngine.Object).Assembly.GetType("UnityEngine.DrivenPropertyManager"),
            "RegisterProperty", "TryRegisterProperty", "UnregisterProperty", "UnregisterProperties");
    }
    static void Inspect(Action<string, object> row, Type type, params string[] names)
    {
        foreach (string name in names)
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.Name == name).OrderBy(m => m.ToString(), StringComparer.Ordinal))
            {
                string key = type.FullName + "." + method.Name + "(" + string.Join(",", method.GetParameters().Select(p => p.ParameterType.ToString()).ToArray()) + ")";
                string value = string.Join(",", method.CustomAttributes.Where(a => a.AttributeType == typeof(ConditionalAttribute))
                    .Select(a => (string)a.ConstructorArguments[0].Value).OrderBy(x => x, StringComparer.Ordinal).ToArray());
                row("conditional.metadata." + key, value);
            }
    }
}
