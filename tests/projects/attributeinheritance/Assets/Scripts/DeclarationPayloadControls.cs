using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;

public static class DeclarationPayloadControls
{
    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    [Serializable] public class PrimitiveHolder { public UnityEngine.Random.State state; public UnityEngine.SceneManagement.Scene scene; public int control; }
    public class CloneHolder : ScriptableObject
    {
        public UnityEngine.Random.State state;
        public UnityEditor.Build.Content.ObjectIdentifier identifier = MakeIdentifier(29, "initializer.asset");
        public int control = 19;
    }
    static UnityEditor.Build.Content.ObjectIdentifier MakeIdentifier(long fileId, string path)
    {
        object boxed = default(UnityEditor.Build.Content.ObjectIdentifier);
        Type type = boxed.GetType();
        type.GetField("m_GUID", All).SetValue(boxed, new UnityEditor.GUID("0123456789abcdef0123456789abcdef"));
        type.GetField("m_LocalIdentifierInFile", All).SetValue(boxed, fileId);
        type.GetField("m_FileType", All).SetValue(boxed, UnityEditor.Build.Content.FileType.SerializedAssetType);
        type.GetField("m_FilePath", All).SetValue(boxed, path);
        return (UnityEditor.Build.Content.ObjectIdentifier)boxed;
    }
    static readonly HashSet<string> Kinds = new HashSet<string> { "UnityEngine.SerializeField", "UnityEngine.HideInInspector", "System.Runtime.CompilerServices.TupleElementNamesAttribute", "System.ComponentModel.DefaultValueAttribute", "System.Diagnostics.CodeAnalysis.DisallowNullAttribute", "System.Diagnostics.CodeAnalysis.NotNullWhenAttribute", "System.Diagnostics.CodeAnalysis.MaybeNullWhenAttribute" };
    static readonly string[][] Fields = {
        new string[] { "UnityEngine.SceneManagement.Scene", "UnityEngine.CoreModule", "m_Handle" },
        new string[] { "UnityEditor.IMGUI.Controls.MultiColumnHeaderState+Column", "UnityEditor.CoreModule", "width" },
        new string[] { "UnityEditor.IMGUI.Controls.MultiColumnHeaderState+Column", "UnityEditor.CoreModule", "sortedAscending" },
        new string[] { "UnityEditor.IMGUI.Controls.MultiColumnHeaderState+Column", "UnityEditor.CoreModule", "headerContent" },
        new string[] { "UnityEditor.IMGUI.Controls.MultiColumnHeaderState+Column", "UnityEditor.CoreModule", "contextMenuText" },
        new string[] { "UnityEditor.IMGUI.Controls.MultiColumnHeaderState+Column", "UnityEditor.CoreModule", "headerTextAlignment" },
        new string[] { "UnityEditor.IMGUI.Controls.MultiColumnHeaderState+Column", "UnityEditor.CoreModule", "sortingArrowAlignment" },
        new string[] { "UnityEditor.IMGUI.Controls.MultiColumnHeaderState+Column", "UnityEditor.CoreModule", "minWidth" },
        new string[] { "UnityEditor.IMGUI.Controls.MultiColumnHeaderState+Column", "UnityEditor.CoreModule", "maxWidth" },
        new string[] { "UnityEditor.IMGUI.Controls.MultiColumnHeaderState+Column", "UnityEditor.CoreModule", "autoResize" },
        new string[] { "UnityEditor.IMGUI.Controls.MultiColumnHeaderState+Column", "UnityEditor.CoreModule", "allowToggleVisibility" },
        new string[] { "UnityEditor.IMGUI.Controls.MultiColumnHeaderState+Column", "UnityEditor.CoreModule", "canSort" },
        new string[] { "UnityEditor.IMGUI.Controls.MultiColumnHeaderState+Column", "UnityEditor.CoreModule", "userData" },
        new string[] { "UnityEngine.Random+State", "UnityEngine.CoreModule", "s0" },
        new string[] { "UnityEngine.Random+State", "UnityEngine.CoreModule", "s1" },
        new string[] { "UnityEngine.Random+State", "UnityEngine.CoreModule", "s2" },
        new string[] { "UnityEngine.Random+State", "UnityEngine.CoreModule", "s3" },
        new string[] { "UnityEngine.TextCore.Text.TextGenerator", "UnityEngine.TextCoreTextEngineModule", "m_VertexBufferAutoSizeReduction" },
        new string[] { "UnityEditor.IMGUI.Controls.MultiColumnHeaderState+Column", "UnityEditor.CoreModule", "userDataObj" },
        new string[] { "UnityEngine.Networking.UnityWebRequest", "UnityEngine.UnityWebRequestModule", "m_CertificateHandler" },
        new string[] { "UnityEngine.Networking.UnityWebRequest", "UnityEngine.UnityWebRequestModule", "m_DownloadHandler" },
        new string[] { "UnityEngine.Networking.UnityWebRequest", "UnityEngine.UnityWebRequestModule", "m_UploadHandler" },
        new string[] { "Unity.Properties.ConversionRegistry", "UnityEngine.PropertiesModule", "m_Converters" },
    };
    static readonly string[][] Parameters = {
        new string[] { "UnityEngine.Audio.AudioClipPlayable", "UnityEngine.AudioModule", "M:Seek`0(System.Double,System.Double,System.Double):System.Void", "2" },
        new string[] { "Unity.Collections.CollectionExtensions", "UnityEngine.CoreModule", "M:static AddSorted`1(System.Collections.Generic.List`1<!!0>,!!0,System.Collections.Generic.IComparer`1<!!0>):System.Void", "0" },
        new string[] { "Unity.Collections.CollectionExtensions", "UnityEngine.CoreModule", "M:static Fill`1(System.Collections.Generic.List`1<!!0>,!!0,System.Int32):System.Void", "0" },
        new string[] { "Unity.Collections.CollectionExtensions", "UnityEngine.CoreModule", "M:static SerializedView`1(System.Collections.Generic.IEnumerable`1<!!0>,System.Func`2<!!0,System.String>):System.String", "0" },
        new string[] { "Unity.Collections.CollectionExtensions", "UnityEngine.CoreModule", "M:static SerializedView`1(System.Collections.Generic.IEnumerable`1<!!0>,System.Func`2<!!0,System.String>):System.String", "1" },
        new string[] { "Unity.Collections.CollectionExtensions", "UnityEngine.CoreModule", "M:static ContainsByEquals`1(System.Collections.Generic.IEnumerable`1<!!0>,!!0):System.Boolean", "0" },
        new string[] { "UnityEngine.Object", "UnityEngine.CoreModule", "M:static op_Implicit`0(UnityEngine.Object):System.Boolean", "0" },
        new string[] { "UnityEngine.Analytics.IAnalytic", "UnityEngine.UnityAnalyticsCommonModule", "M:TryGatherData`0(out UnityEngine.Analytics.IAnalytic+IData,out System.Exception):System.Boolean", "1" },
        new string[] { "UnityEngine.Playables.PlayableGraph", "UnityEngine.CoreModule", "M:Evaluate`0(System.Single):System.Void", "0" },
    };
    static readonly string[][] Returns = {
        new string[] { "Unity.Baselib.LowLevel.Binding", "UnityEngine.CoreModule", "M:static Baselib_NetworkAddress_HostnameLookupCheckStatus`0(Unity.Baselib.LowLevel.Binding+Baselib_NetworkAddress_HostnameLookupHandle*,Unity.Baselib.LowLevel.Binding+Baselib_NetworkAddress*,Unity.Baselib.LowLevel.Binding+Baselib_ErrorState*):System.Boolean" },
        new string[] { "Unity.Baselib.LowLevel.Binding", "UnityEngine.CoreModule", "M:static Baselib_RegisteredNetwork_IsEmulated`0():System.Boolean" },
        new string[] { "Unity.Baselib.LowLevel.Binding", "UnityEngine.CoreModule", "M:static Baselib_SystemFutex_NativeSupport`0():System.Boolean" },
        new string[] { "Unity.Baselib.LowLevel.Binding", "UnityEngine.CoreModule", "M:static Baselib_SystemSemaphore_TryAcquire`0(Unity.Baselib.LowLevel.Binding+Baselib_SystemSemaphore_Handle):System.Boolean" },
        new string[] { "Unity.Baselib.LowLevel.Binding", "UnityEngine.CoreModule", "M:static Baselib_SystemSemaphore_TryTimedAcquire`0(Unity.Baselib.LowLevel.Binding+Baselib_SystemSemaphore_Handle,System.UInt32):System.Boolean" },
    };
    public static void Record(Action<string, object> row)
    {
        foreach (var d in Fields)
        {
            var field = Type.GetType(d[0] + ", " + d[1], true).GetField(d[2], All);
            row("payload.field." + d[0] + "." + d[2], DeclarationAnnotationControls.Attributes(field.GetCustomAttributesData(), Kinds) + ";nonserialized=" + field.IsNotSerialized);
        }
        foreach (var d in Returns)
        {
            var method = (MethodInfo)DeclarationSignatures.Find(d[0], d[1], d[2]);
            var attributes = method.ReturnParameter.GetCustomAttributes(typeof(System.Runtime.InteropServices.MarshalAsAttribute), false);
            row("payload.return." + d[0] + "." + d[2], attributes.Length == 0 ? -1 : (int)((System.Runtime.InteropServices.MarshalAsAttribute)attributes[0]).Value);
        }
        foreach (var d in Parameters)
        {
            var method = (MethodBase)DeclarationSignatures.Find(d[0], d[1], d[2]);
            var parameter = method.GetParameters()[int.Parse(d[3], CultureInfo.InvariantCulture)];
            row("payload.parameter." + d[0] + "." + d[2] + "." + d[3], DeclarationAnnotationControls.Attributes(parameter.GetCustomAttributesData(), Kinds));
        }
        var state = JsonUtility.FromJson<UnityEngine.Random.State>("{\"s0\":1,\"s1\":-2,\"s2\":3,\"s3\":4}");
        row("payload.random.root", JsonUtility.ToJson(state));
        object boxedState = state;
        JsonUtility.FromJsonOverwrite("{\"s1\":7}", boxedState);
        row("payload.random.overwrite", JsonUtility.ToJson(boxedState));
        row("payload.random.copy", JsonUtility.ToJson(state));
        var scene = JsonUtility.FromJson<UnityEngine.SceneManagement.Scene>("{\"m_Handle\":123}");
        row("payload.scene.handle", scene.handle);
        row("payload.scene.root", JsonUtility.ToJson(scene));
        var holder = JsonUtility.FromJson<PrimitiveHolder>("{\"state\":{\"s0\":11,\"s1\":12,\"s2\":13,\"s3\":14},\"scene\":{\"m_Handle\":321},\"control\":19}");
        row("payload.nested", JsonUtility.ToJson(holder));
        JsonUtility.FromJsonOverwrite("{\"control\":23}", holder);
        row("payload.nested.omitted", JsonUtility.ToJson(holder));
        var column = new UnityEditor.IMGUI.Controls.MultiColumnHeaderState.Column { width = 9, minWidth = 23 };
        JsonUtility.FromJsonOverwrite("{\"width\":17}", column);
        row("payload.column.width", column.width);
        row("payload.column.control-min", column.minWidth);
        Type registryType = Type.GetType("Unity.Properties.ConversionRegistry, UnityEngine.PropertiesModule", true);
        object registry = registryType.GetMethod("Create", All).Invoke(null, null);
        var register = registryType.GetMethod("Register", All);
        var get = registryType.GetMethod("GetConverter", All);
        Func<int, string> first = value => (value + 7).ToString(CultureInfo.InvariantCulture);
        register.Invoke(registry, new object[] { typeof(int), typeof(string), first });
        row("payload.converter.first", ((Func<int, string>)get.Invoke(registry, new object[] { typeof(int), typeof(string) }))(1));
        Func<int, string> second = value => (value + 8).ToString(CultureInfo.InvariantCulture);
        register.Invoke(registry, new object[] { typeof(int), typeof(string), second });
        row("payload.converter.replace", ((Func<int, string>)get.Invoke(registry, new object[] { typeof(int), typeof(string) }))(1));
        row("payload.converter.reverse-control", get.Invoke(registry, new object[] { typeof(string), typeof(int) }) == null);
        object identifier = MakeIdentifier(17, "before.asset");
        var original = ScriptableObject.CreateInstance<CloneHolder>();
        CloneHolder cloned = null;
        try
        {
            original.state = state;
            original.identifier = (UnityEditor.Build.Content.ObjectIdentifier)identifier;
            cloned = UnityEngine.Object.Instantiate(original);
            row("payload.clone.identifier.file-id", cloned.identifier.localIdentifierInFile);
            row("payload.clone.identifier.path", cloned.identifier.filePath);
            row("payload.clone.state", JsonUtility.ToJson(cloned.state));
            row("payload.clone.control", cloned.control);
            row("payload.clone.original-file-id", original.identifier.localIdentifierInFile);
            original.identifier = MakeIdentifier(31, "changed.asset");
            row("payload.clone.independent", cloned.identifier.localIdentifierInFile);
        }
        finally
        {
            if (cloned != null) UnityEngine.Object.DestroyImmediate(cloned);
            UnityEngine.Object.DestroyImmediate(original);
        }
    }
}
