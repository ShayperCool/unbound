using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Pool;

public static class DeclarationFinalControls
{
    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static readonly string[][] Declarations = new string[][] {
        new string[] { "UnityEngine.Texture2D", "UnityEngine.CoreModule", "type" },
        new string[] { "UnityEngine.SpriteMask", "UnityEngine.SpriteMaskModule", "type" },
        new string[] { "UnityEngine.RuntimeAnimatorController", "UnityEngine.AnimationModule", "type" },
        new string[] { "UnityEngine.Audio.AudioMixer", "UnityEngine.AudioModule", "type" },
        new string[] { "UnityEditor.ConsoleWindow", "UnityEditor.CoreModule", "type" },
        new string[] { "UnityEditor.GameView", "UnityEditor.CoreModule", "type" },
        new string[] { "UnityEditor.SceneView", "UnityEditor.CoreModule", "type" },
        new string[] { "UnityEditor.EditorWindow", "UnityEditor.CoreModule", "type" },
        new string[] { "UnityEditor.Editor", "UnityEditor.CoreModule", "type" },
        new string[] { "UnityEditor.AssetImporter", "UnityEditor.CoreModule", "type" },
        new string[] { "UnityEditor.Build.Profile.BuildProfile", "UnityEditor.CoreModule", "type" },
        new string[] { "UnityEngine.Rendering.RayTracingShader", "UnityEngine.CoreModule", "type" },
        new string[] { "UnityEngine.TextCore.Text.TextAsset", "UnityEngine.TextCoreTextEngineModule", "type" },
        new string[] { "UnityEngine.TextCore.Text.TextSettings", "UnityEngine.TextCoreTextEngineModule", "type" },
        new string[] { "UnityEngine.TextCore.Text.TextStyleSheet", "UnityEngine.TextCoreTextEngineModule", "type" },
        new string[] { "UnityEditor.SerializedObject", "UnityEditor.CoreModule", "M:Dispose`0():System.Void" },
        new string[] { "UnityEditor.SerializedProperty", "UnityEditor.CoreModule", "M:Dispose`0():System.Void" },
        new string[] { "Unity.Collections.NativeArray`1", "UnityEngine.CoreModule", "M:static IsUnmanagedAndThrow`0():System.Void" },
    };
    static readonly HashSet<string> Kinds = new HashSet<string> {
        "UnityEngine.ExcludeFromObjectFactoryAttribute", "UnityEditor.EditorWindowTitleAttribute",
        "UnityEngine.Rendering.RayTracingShaderHelpURLAttribute", "UnityEngine.RejectDragAndDropMaterial",
        "UnityEngine.ExcludeFromPresetAttribute", "UnityEngine.HelpURLAttribute", "Unity.Burst.BurstDiscardAttribute",
        "UnityEngine.ThreadAndSerializationSafeAttribute", "System.Diagnostics.ConditionalAttribute"
    };
    [ExcludeFromObjectFactory] class Excluded { }
    class ExcludedChild : Excluded { }
    class Included { }
    static string ExceptionName(Action action)
    {
        try { action(); return "none"; }
        catch (TargetInvocationException ex) { return ex.InnerException.GetType().FullName; }
        catch (Exception ex) { return ex.GetType().FullName; }
    }
    public static void Record(Action<string, object> row)
    {
        foreach (var d in Declarations)
            row("final.annotation." + d[0] + "." + d[2], DeclarationAnnotationControls.Attributes(DeclarationSignatures.Find(d[0], d[1], d[2]).GetCustomAttributesData(), Kinds));
        foreach (string name in new[] { "UnityEngine.ExcludeFromObjectFactoryAttribute", "UnityEditor.EditorWindowTitleAttribute", "UnityEngine.Rendering.RayTracingShaderHelpURLAttribute", "UnityEngine.WritableAttribute", "UnityEngine.ThreadAndSerializationSafeAttribute", "UnityEngine.RejectDragAndDropMaterial" })
        {
            string module = name.StartsWith("UnityEditor.") ? "UnityEditor.CoreModule" : name.Contains("Writable") || name.Contains("ThreadAnd") || name.Contains("Reject") ? "UnityEngine.SharedInternalsModule" : "UnityEngine.CoreModule";
            Type type = Type.GetType(name + ", " + module, true);
            var usage = (AttributeUsageAttribute)Attribute.GetCustomAttribute(type, typeof(AttributeUsageAttribute), true);
            row("final.attribute." + name, type.IsPublic + "/" + type.IsSealed + "/" + (int)usage.ValidOn + "/" + usage.Inherited + "/" + usage.AllowMultiple);
            row("final.construct." + name, ExceptionName(() => Activator.CreateInstance(type)));
        }
        var writableMethod = (MethodInfo)DeclarationSignatures.Find("UnityEngine.Playables.PlayableOutputHandle", "UnityEngine.CoreModule", "M:SetUserData`0(UnityEngine.Object):System.Void");
        row("final.writable", DeclarationAnnotationControls.Attributes(writableMethod.GetParameters()[0].GetCustomAttributesData(), new HashSet<string> { "UnityEngine.WritableAttribute" }));
        foreach (var d in new[] { new[] { "UnityEngine.UIElements.StyleComplexSelector", "s_PseudoStates" }, new[] { "UnityEngine.UIElements.Internal.MultiColumnCollectionHeader", "m_OldSortedColumnStates" } })
            row("final.tuple." + d[0], DeclarationAnnotationControls.Attributes(Type.GetType(d[0] + ", UnityEngine.UIElementsModule", true).GetField(d[1], All).GetCustomAttributesData(), new HashSet<string> { "System.Runtime.CompilerServices.TupleElementNamesAttribute" }));
        row("final.factory.inherited", Attribute.IsDefined(typeof(ExcludedChild), typeof(ExcludeFromObjectFactoryAttribute), true));
        row("final.factory.direct", Attribute.IsDefined(typeof(ExcludedChild), typeof(ExcludeFromObjectFactoryAttribute), false));
        row("final.factory.control", Attribute.IsDefined(typeof(Included), typeof(ExcludeFromObjectFactoryAttribute), true));
        var title = new UnityEditor.EditorWindowTitleAttribute();
        row("final.title.default", (title.title ?? "<null>") + "/" + (title.icon ?? "<null>") + "/" + title.useTypeNameAsIconName);
        title.title = "Before"; title.icon = "icon-before"; title.useTypeNameAsIconName = true;
        row("final.title.assigned", title.title + "/" + title.icon + "/" + title.useTypeNameAsIconName);
        title.title = "After";
        row("final.title.changed", title.title + "/" + title.icon + "/" + title.useTypeNameAsIconName);
        Type ray = Type.GetType("UnityEngine.Rendering.RayTracingShaderHelpURLAttribute, UnityEngine.CoreModule", true);
        row("final.ray.url", ((HelpURLAttribute)Activator.CreateInstance(ray)).URL);
        var empty = default(NamedBuildTarget);
        row("final.named.readonly", DeclarationSignatures.IsReadOnly(typeof(NamedBuildTarget)));
        foreach (string method in new[] { "get_TargetName", "Equals", "GetHashCode", "ToBuildTargetGroup" })
        {
            var m = typeof(NamedBuildTarget).GetMethods(All).First(x => x.Name == method && (method != "Equals" || x.GetParameters()[0].ParameterType == typeof(NamedBuildTarget)));
            row("final.named.readonly." + method, DeclarationSignatures.IsReadOnly(m));
        }
        row("final.named.interface", typeof(IComparable<NamedBuildTarget>).IsAssignableFrom(typeof(NamedBuildTarget)));
        row("final.named.default-hash", ExceptionName(() => empty.GetHashCode()));
        row("final.named.default-compare", ExceptionName(() => empty.CompareTo(NamedBuildTarget.Server)));
        row("final.named.other-default", Math.Sign(NamedBuildTarget.Server.CompareTo(empty)));
        row("final.named.default-equals", empty.Equals(default(NamedBuildTarget)));
        row("final.named.copy", NamedBuildTarget.Server.Equals((object)NamedBuildTarget.Server));
        row("final.named.object-control", NamedBuildTarget.Server.Equals((object)"Server"));
        foreach (var field in typeof(NamedBuildTarget).GetFields(BindingFlags.Public | BindingFlags.Static).OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            var value = (NamedBuildTarget)field.GetValue(null);
            row("final.named.name." + field.Name, value.TargetName);
            row("final.named.hash." + field.Name, ExceptionName(() => value.GetHashCode()));
        }
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            foreach (string culture in new[] { "", "en-US", "tr-TR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                row("final.named.compare." + culture, Math.Sign(NamedBuildTarget.iOS.CompareTo(NamedBuildTarget.Server)));
                row("final.named.self." + culture, NamedBuildTarget.iOS.CompareTo(NamedBuildTarget.iOS));
            }
        }
        finally { CultureInfo.CurrentCulture = saved; }
        row("final.named.ordinal-control", Math.Sign(string.CompareOrdinal(NamedBuildTarget.iOS.TargetName, NamedBuildTarget.Server.TargetName)));
        Pool(row, false); Pool(row, true);
    }
    static void Pool(Action<string, object> row, bool linked)
    {
        int destroyed = 0;
        IObjectPool<object> pool = linked ? (IObjectPool<object>)new LinkedPool<object>(() => new object(), actionOnDestroy: _ => destroyed++) : new ObjectPool<object>(() => new object(), actionOnDestroy: _ => destroyed++);
        Type contract = Type.GetType("UnityEngine.Pool.IPool, UnityEngine.CoreModule", true);
        string key = "final.pool." + (linked ? "linked." : "object.");
        row(key + "interface", contract.IsInstanceOfType(pool));
        var count = contract.GetProperty("CountInactive");
        row(key + "initial", count.GetValue(pool, null));
        object first = pool.Get(), second = pool.Get();
        row(key + "distinct", !ReferenceEquals(first, second));
        pool.Release(first); pool.Release(second);
        row(key + "released", count.GetValue(pool, null));
        contract.GetMethod("Clear").Invoke(pool, null);
        row(key + "cleared", count.GetValue(pool, null));
        row(key + "destroyed", destroyed);
    }
}
