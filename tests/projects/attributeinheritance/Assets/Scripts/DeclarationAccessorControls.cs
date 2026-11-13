using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

public static class DeclarationAccessorControls
{
    static readonly string[][] Declarations = {
        new string[] { "UnityEditor.AssetPostprocessor", "UnityEditor.CoreModule", "P:assetImporter():UnityEditor.AssetImporter" },
        new string[] { "UnityEngine.AI.NavMeshQueryFilter", "UnityEngine.AIModule", "P:costs():System.Single[]" },
        new string[] { "UnityEngine.Android.AndroidConfiguration", "UnityEngine.AndroidJNIModule", "P:colorModeHdr():UnityEngine.Android.AndroidColorModeHdr" },
        new string[] { "UnityEngine.Android.AndroidConfiguration", "UnityEngine.AndroidJNIModule", "P:colorModeWideColorGamut():UnityEngine.Android.AndroidColorModeWideColorGamut" },
        new string[] { "UnityEngine.Android.AndroidConfiguration", "UnityEngine.AndroidJNIModule", "P:screenLayoutDirection():UnityEngine.Android.AndroidScreenLayoutDirection" },
        new string[] { "UnityEngine.Android.AndroidConfiguration", "UnityEngine.AndroidJNIModule", "P:screenLayoutLong():UnityEngine.Android.AndroidScreenLayoutLong" },
        new string[] { "UnityEngine.Android.AndroidConfiguration", "UnityEngine.AndroidJNIModule", "P:screenLayoutRound():UnityEngine.Android.AndroidScreenLayoutRound" },
        new string[] { "UnityEngine.Android.AndroidConfiguration", "UnityEngine.AndroidJNIModule", "P:screenLayoutSize():UnityEngine.Android.AndroidScreenLayoutSize" },
        new string[] { "UnityEngine.Android.AndroidConfiguration", "UnityEngine.AndroidJNIModule", "P:uiModeNight():UnityEngine.Android.AndroidUIModeNight" },
        new string[] { "UnityEngine.Android.AndroidConfiguration", "UnityEngine.AndroidJNIModule", "P:uiModeType():UnityEngine.Android.AndroidUIModeType" },
        new string[] { "UnityEngine.Animations.PropertyStreamHandle", "UnityEngine.AnimationModule", "P:animatorBindingsVersion():System.UInt32" },
        new string[] { "UnityEngine.Animations.TransformStreamHandle", "UnityEngine.AnimationModule", "P:animatorBindingsVersion():System.UInt32" },
        new string[] { "UnityEngine.Application", "UnityEngine.CoreModule", "P:static absoluteURL():System.String" },
        new string[] { "UnityEngine.Application", "UnityEngine.CoreModule", "P:static isFocused():System.Boolean" },
        new string[] { "UnityEngine.ApplicationMemoryUsageChange", "UnityEngine.CoreModule", "P:memoryUsage():UnityEngine.ApplicationMemoryUsage" },
        new string[] { "UnityEngine.AwaitableCompletionSource", "UnityEngine.CoreModule", "P:Awaitable():UnityEngine.Awaitable" },
        new string[] { "UnityEngine.AwaitableCompletionSource`1", "UnityEngine.CoreModule", "P:Awaitable():UnityEngine.Awaitable`1<!0>" },
        new string[] { "UnityEngine.BuildCompression", "UnityEngine.CoreModule", "P:blockSize():System.UInt32" },
        new string[] { "UnityEngine.BuildCompression", "UnityEngine.CoreModule", "P:compression():UnityEngine.CompressionType" },
        new string[] { "UnityEngine.BuildCompression", "UnityEngine.CoreModule", "P:level():UnityEngine.CompressionLevel" },
        new string[] { "UnityEngine.Camera", "UnityEngine.CoreModule", "P:static current():UnityEngine.Camera" },
        new string[] { "UnityEngine.DefaultExecutionOrder", "UnityEngine.CoreModule", "P:order():System.Int32" },
        new string[] { "UnityEngine.QualitySettings", "UnityEngine.CoreModule", "P:static names():System.String[]" },
        new string[] { "UnityEngine.Rendering.ResourcePathsBaseAttribute", "UnityEngine.CoreModule", "P:isField():System.Boolean" },
        new string[] { "UnityEngine.Rendering.ResourcePathsBaseAttribute", "UnityEngine.CoreModule", "P:location():UnityEngine.Rendering.SearchType" },
        new string[] { "UnityEngine.Rendering.ResourcePathsBaseAttribute", "UnityEngine.CoreModule", "P:paths():System.String[]" },
        new string[] { "UnityEngine.Texture", "UnityEngine.CoreModule", "P:updateCount():System.UInt32" },
    };
    public static void Record(Action<string, object> row)
    {
        foreach (var d in Declarations)
        {
            var property = (PropertyInfo)DeclarationSignatures.Find(d[0], d[1], d[2]);
            var setter = property.GetSetMethod(true);
            row("accessor." + d[0] + "." + d[2], setter == null ? "absent" : setter.IsPrivate ? "private" : "other");
        }
        var filter = new NavMeshQueryFilter { agentTypeID = 3, areaMask = 5 };
        row("accessor.costs.before", filter.GetAreaCost(0));
        object boxed = filter;
        var costs = new float[32];
        for (int i = 0; i < costs.Length; ++i) costs[i] = 1;
        costs[0] = 0.25f;
        typeof(NavMeshQueryFilter).GetProperty("costs", BindingFlags.Instance | BindingFlags.NonPublic).GetSetMethod(true).Invoke(boxed, new object[] { costs });
        var changed = (NavMeshQueryFilter)boxed;
        row("accessor.costs.after", changed.GetAreaCost(0));
        row("accessor.costs.copy", filter.GetAreaCost(0));
        row("accessor.costs.control-agent", changed.agentTypeID);
        var resource = new ResourcePathAttribute("before");
        var paths = new[] { "after" };
        typeof(ResourcePathsBaseAttribute).GetProperty("paths").GetSetMethod(true).Invoke(resource, new object[] { paths });
        row("accessor.paths.changed", resource.paths[0]);
        row("accessor.paths.identity", ReferenceEquals(paths, resource.paths));
        row("accessor.paths.control-isfield", resource.isField);
    }
}
