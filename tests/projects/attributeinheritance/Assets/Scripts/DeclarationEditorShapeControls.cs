using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Compilation;
using Assembly = UnityEditor.Compilation.Assembly;

public static class DeclarationEditorShapeControls
{
    static readonly string[][] Declarations = {
        new string[] { "UnityEditor.Compilation.Assembly", "UnityEditor.CoreModule", "P:compiledAssemblyReferences():System.String[]", "accessors" },
        new string[] { "UnityEditor.Compilation.Assembly", "UnityEditor.CoreModule", "P:defines():System.String[]", "accessors" },
        new string[] { "UnityEditor.Compilation.Assembly", "UnityEditor.CoreModule", "P:flags():UnityEditor.Compilation.AssemblyFlags", "accessors" },
        new string[] { "UnityEditor.Compilation.Assembly", "UnityEditor.CoreModule", "P:name():System.String", "accessors" },
        new string[] { "UnityEditor.Compilation.Assembly", "UnityEditor.CoreModule", "P:outputPath():System.String", "accessors" },
        new string[] { "UnityEditor.Compilation.Assembly", "UnityEditor.CoreModule", "P:rootNamespace():System.String", "accessors" },
        new string[] { "UnityEditor.Compilation.Assembly", "UnityEditor.CoreModule", "P:sourceFiles():System.String[]", "accessors" },
        new string[] { "UnityEditor.IMGUI.Controls.MultiColumnHeader", "UnityEditor.CoreModule", "P:currentColumnIndex():System.Int32", "accessors" },
        new string[] { "UnityEditor.PackageManager.PackageInfo", "UnityEditor.CoreModule", "P:category():System.String", "accessors" },
        new string[] { "UnityEditor.PackageManager.PackageInfo", "UnityEditor.CoreModule", "P:description():System.String", "accessors" },
        new string[] { "UnityEditor.PackageManager.PackageInfo", "UnityEditor.CoreModule", "P:displayName():System.String", "accessors" },
        new string[] { "UnityEditor.PackageManager.PackageInfo", "UnityEditor.CoreModule", "P:isDirectDependency():System.Boolean", "accessors" },
        new string[] { "UnityEditor.PackageManager.PackageInfo", "UnityEditor.CoreModule", "P:keywords():System.String[]", "accessors" },
        new string[] { "UnityEditor.PackageManager.PackageInfo", "UnityEditor.CoreModule", "P:name():System.String", "accessors" },
        new string[] { "UnityEditor.PackageManager.PackageInfo", "UnityEditor.CoreModule", "P:resolvedPath():System.String", "accessors" },
        new string[] { "UnityEditor.PackageManager.PackageInfo", "UnityEditor.CoreModule", "P:source():UnityEditor.PackageManager.PackageSource", "accessors" },
        new string[] { "UnityEditor.PackageManager.PackageInfo", "UnityEditor.CoreModule", "P:type():System.String", "accessors" },
        new string[] { "UnityEditor.PackageManager.PackageInfo", "UnityEditor.CoreModule", "P:version():System.String", "accessors" },
        new string[] { "UnityEditor.SceneView", "UnityEditor.CoreModule", "P:static lastActiveSceneView():UnityEditor.SceneView", "accessors" },
        new string[] { "UnityEditor.ActiveEditorTracker", "UnityEditor.CoreModule", "type", "modifiers" },
        new string[] { "UnityEditor.AssetDatabase", "UnityEditor.CoreModule", "type", "modifiers" },
        new string[] { "UnityEditor.GUID", "UnityEditor.CoreModule", "type", "modifiers" },
        new string[] { "UnityEditor.GlobalObjectId", "UnityEditor.CoreModule", "type", "modifiers" },
        new string[] { "UnityEditor.IMGUI.Controls.MultiColumnHeaderState", "UnityEditor.CoreModule", "type", "modifiers" },
        new string[] { "UnityEditor.Networking.PlayerConnection.EditorConnection", "UnityEditor.CoreModule", "type", "modifiers" },
        new string[] { "UnityEditor.PackageManager.DependencyInfo", "UnityEditor.CoreModule", "M:get_name`0():System.String", "modifiers" },
        new string[] { "UnityEditor.PackageManager.DependencyInfo", "UnityEditor.CoreModule", "M:get_version`0():System.String", "modifiers" },
        new string[] { "UnityEditor.PackageManager.DependencyInfo", "UnityEditor.CoreModule", "type", "modifiers" },
        new string[] { "UnityEditor.PlayerSettings+Android", "UnityEditor.CoreModule", "type", "modifiers" },
        new string[] { "UnityEditor.PlayerSettings+iOS", "UnityEditor.CoreModule", "type", "modifiers" },
        new string[] { "UnityEditor.SceneManagement.PrefabStage", "UnityEditor.CoreModule", "type", "modifiers" },
    };
    public static void Record(Action<string, object> row)
    {
        foreach (var d in Declarations)
        {
            var member = DeclarationSignatures.Find(d[0], d[1], d[2]);
            string value;
            if (d[3] == "accessors")
            {
                var set = ((PropertyInfo)member).GetSetMethod(true);
                value = set == null ? "absent" : set.IsPrivate ? "private" : "other";
            }
            else if (member is Type)
            {
                var type = (Type)member;
                value = type.IsSerializable + ":" + type.IsSealed + ":" + type.IsAbstract + ":" + DeclarationSignatures.IsReadOnly(type);
            }
            else value = DeclarationSignatures.IsReadOnly(member).ToString();
            row("editor.shape." + d[0] + "." + d[2] + "." + d[3], value);
        }
        var dependency = UnityEngine.JsonUtility.FromJson<UnityEditor.PackageManager.DependencyInfo>("{\"m_Name\":\"before\",\"m_Version\":\"1\"}");
        row("editor.serialize.dependency.before-name", dependency.name);
        row("editor.serialize.dependency.before-version", dependency.version);
        object boxed = dependency;
        UnityEngine.JsonUtility.FromJsonOverwrite("{\"m_Name\":\"after\"}", boxed);
        var changed = (UnityEditor.PackageManager.DependencyInfo)boxed;
        row("editor.serialize.dependency.after-name", changed.name);
        row("editor.serialize.dependency.omitted-version", changed.version);
        row("editor.serialize.dependency.json", UnityEngine.JsonUtility.ToJson(changed));
        row("editor.serialize.dependency.copy-name", dependency.name);
        row("editor.serialize.guid.json", UnityEngine.JsonUtility.ToJson(new GUID("0123456789abcdef0123456789abcdef")));
        GlobalObjectId globalId;
        GlobalObjectId.TryParse("GlobalObjectId_V1-1-0123456789abcdef0123456789abcdef-17-19", out globalId);
        row("editor.serialize.global-id.json", UnityEngine.JsonUtility.ToJson(globalId));
        var header = new UnityEditor.IMGUI.Controls.MultiColumnHeaderState(new[] { new UnityEditor.IMGUI.Controls.MultiColumnHeaderState.Column(), new UnityEditor.IMGUI.Controls.MultiColumnHeaderState.Column() });
        var maximum = typeof(UnityEditor.IMGUI.Controls.MultiColumnHeaderState).GetField("m_MaxNumberOfSortedColumns", BindingFlags.Instance | BindingFlags.NonPublic);
        row("editor.serialize.header.nonserialized", maximum.IsNotSerialized);
        row("editor.serialize.header.marked", maximum.IsDefined(typeof(UnityEngine.SerializeField), false));
        maximum.SetValue(header, 9);
        row("editor.serialize.header.before-maximum", maximum.GetValue(header));
        row("editor.serialize.header.before-visible", string.Join(",", header.visibleColumns));
        UnityEngine.JsonUtility.FromJsonOverwrite("{\"m_MaxNumberOfSortedColumns\":17,\"m_VisibleColumns\":[1]}", header);
        row("editor.serialize.header.after-maximum", maximum.GetValue(header));
        row("editor.serialize.header.after-visible", string.Join(",", header.visibleColumns));
        var assembly = new Assembly("before", "before.dll", new[] { "before.cs" }, new[] { "BEFORE" }, new Assembly[0], new[] { "reference.dll" }, AssemblyFlags.None);
        var name = typeof(Assembly).GetProperty("name");
        row("editor.setter.assembly.before", assembly.name);
        name.GetSetMethod(true).Invoke(assembly, new object[] { "after" });
        row("editor.setter.assembly.after", assembly.name);
        row("editor.setter.assembly.control-path", assembly.outputPath);
    }
}
