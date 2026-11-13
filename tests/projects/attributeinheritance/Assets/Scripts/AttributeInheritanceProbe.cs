using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEditor;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine.UIElements;

namespace AttributeInheritanceControls
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Field, Inherited = true)]
    public class InheritedMarker : Attribute {}
    public sealed class DerivedMarker : InheritedMarker {}
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Field, Inherited = false)]
    public sealed class DirectMarker : Attribute {}
    [InheritedMarker] public class MarkedBase
    {
        [InheritedMarker] public virtual void Method() {}
        [InheritedMarker] public int field;
    }
    public class MarkedChild : MarkedBase { public override void Method() {} public new int field; }
    [DirectMarker] public class DirectBase {}
    public class DirectChild : DirectBase {}
    [DerivedMarker] public class DerivedAttributeBase {}
    public class DerivedAttributeChild : DerivedAttributeBase {}

    [ExecuteAlways] public class AlwaysBase : MonoBehaviour {}
    public class AlwaysChild : AlwaysBase {}
    [ExecuteInEditMode] public class EditBase : MonoBehaviour {}
    public class EditChild : EditBase {}
    [DisallowMultipleComponent] public class UniqueBase : MonoBehaviour {}
    public class UniqueChild : UniqueBase {}
    [RequireComponent(typeof(BoxCollider))] public class RequiredBase : MonoBehaviour {}
    [RequireComponent(typeof(SphereCollider))] public class RequiredChild : RequiredBase {}
    [DefaultExecutionOrder(-37)] public class OrderBase : MonoBehaviour {}
    public class OrderChild : OrderBase {}
    [SharedBetweenAnimators] public class SharedBase : StateMachineBehaviour {}
    public class SharedChild : SharedBase {}
    [CreateAssetMenu] public class MenuBase : ScriptableObject {}
    public class MenuChild : MenuBase {}
    [Icon("Assets/icon.png")] public class IconBase : MonoBehaviour {}
    public class IconChild : IconBase {}
    [ExcludeFromPreset] public class ExcludedBase : MonoBehaviour {}
    public class ExcludedChild : ExcludedBase {}

    public class NoEditorTarget : ScriptableObject {}
    public class NoEditorChild : NoEditorTarget {}
    public class YesEditorTarget : ScriptableObject {}
    public class YesEditorChild : YesEditorTarget {}
    public class MultiTarget : ScriptableObject {}
    [CustomEditor(typeof(NoEditorTarget), false)] public class NoChildEditor : Editor {}
    public class UnmarkedEditorChild : NoChildEditor {}
    [CustomEditor(typeof(YesEditorTarget), true)] public class YesChildEditor : Editor {}
    [CanEditMultipleObjects] public class MultiEditorBase : Editor {}
    [CustomEditor(typeof(MultiTarget))] public class InheritedMultiEditor : MultiEditorBase {}

    [Serializable] public class NoDrawerTarget { public int value; }
    [Serializable] public class NoDrawerChild : NoDrawerTarget {}
    [Serializable] public class YesDrawerTarget { public int value; }
    [Serializable] public class YesDrawerChild : YesDrawerTarget {}
    public class DrawerValues : ScriptableObject
    {
        public NoDrawerTarget no = new NoDrawerTarget();
        public NoDrawerChild noChild = new NoDrawerChild();
        public YesDrawerTarget yes = new YesDrawerTarget();
        public YesDrawerChild yesChild = new YesDrawerChild();
    }
    [CustomPropertyDrawer(typeof(NoDrawerTarget), false)] public class NoChildDrawer : PropertyDrawer
    { public override float GetPropertyHeight(SerializedProperty property, GUIContent label) { return 37; } }
    [CustomPropertyDrawer(typeof(YesDrawerTarget), true)] public class YesChildDrawer : PropertyDrawer
    { public override float GetPropertyHeight(SerializedProperty property, GUIContent label) { return 53; } }
    public class UnmarkedDrawerChild : YesChildDrawer {}

    [Serializable] public class SerializedBase
    {
        [SerializeField, FormerlySerializedAs("oldValue")] protected int value;
        public int Value { get { return value; } }
    }
    [Serializable] public class SerializedChild : SerializedBase {}
    [Serializable] public class PlainRename { [FormerlySerializedAs("oldValue")] public int value; }
    [Serializable] public class NestedRename { public PlainRename item; }
    [UxmlElement] public partial class UxmlBase : VisualElement
    {
        [UxmlAttribute] public string textLabel { get; set; }
        [UxmlAttribute, UxmlTypeReference(typeof(Component))] public Type referenceType { get; set; }
    }
    [UxmlElement] public partial class UxmlChild : UxmlBase {}
    public partial class UnmarkedUxmlChild : UxmlBase {}
    public static class ImageCalls { public static readonly List<string> values = new List<string>(); }
    public class OpaqueEffectBase : MonoBehaviour
    {
        [ImageEffectOpaque] public virtual void OnRenderImage(RenderTexture source, RenderTexture destination)
        { ImageCalls.values.Add("image"); Graphics.Blit(source, destination); }
        public void OnPostRender() { ImageCalls.values.Add("post"); }
    }
    public class OpaqueEffectInherited : OpaqueEffectBase {}
    public class OpaqueEffectOverride : OpaqueEffectBase
    {
        public override void OnRenderImage(RenderTexture source, RenderTexture destination) { base.OnRenderImage(source, destination); }
    }
    public class FieldBase
    {
        [SerializeField, SerializeReference, FormerlySerializedAs("old"), HideInInspector, Tooltip("field"), ReadOnly, WriteOnly,
         DeallocateOnJobCompletion, NativeDisableParallelForRestriction, NativeDisableContainerSafetyRestriction,
         NativeDisableUnsafePtrRestriction, NativeSetClassTypeToNullOnSchedule, NativeSetThreadIndex]
        public object value;
    }
    public class FieldInherited : FieldBase {}
    public class FieldShadow : FieldBase { public new object value; }
    public class SerializedBehaviourBase : MonoBehaviour
    {
        [SerializeField, FormerlySerializedAs("oldValue")] protected int value;
        public int Value { get { return value; } }
    }
    public static class InitCounts { public static int baseType, childType, method, hiddenMethod, enter, hiddenEnter, reload, hiddenReload, scene, hiddenScene, runtime, hiddenRuntime, menu, hiddenMenu; }
    [InitializeOnLoad] public class InitializedBase { static InitializedBase() { InitCounts.baseType++; } }
    public class InitializedChild : InitializedBase { static InitializedChild() { InitCounts.childType++; } }
    public class InitMethodBase
    {
        [InitializeOnLoadMethod] public static void Load() { InitCounts.method++; }
        [InitializeOnEnterPlayMode] public static void Enter(EnterPlayModeOptions options) { InitCounts.enter++; }
        [UnityEditor.Callbacks.DidReloadScripts] public static void Reload() { InitCounts.reload++; }
        [UnityEditor.Callbacks.PostProcessScene] public static void Scene() { InitCounts.scene++; }
        [RuntimeInitializeOnLoadMethod] public static void Runtime() { InitCounts.runtime++; }
        [MenuItem("Q403/Attribute inheritance control")] public static void Menu() { InitCounts.menu++; }
    }
    public class InitMethodChild : InitMethodBase
    {
        public new static void Load() { InitCounts.hiddenMethod++; }
        public new static void Enter(EnterPlayModeOptions options) { InitCounts.hiddenEnter++; }
        public new static void Reload() { InitCounts.hiddenReload++; }
        public new static void Scene() { InitCounts.hiddenScene++; }
        public new static void Runtime() { InitCounts.hiddenRuntime++; }
        public new static void Menu() { InitCounts.hiddenMenu++; }
    }
}

public class AttributeInheritanceProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod]
    static void Install() { new GameObject("AttributeInheritanceProbe").AddComponent<AttributeInheritanceProbe>(); }
    static StreamWriter output;
    static void Row(string key, object value)
    {
        string text = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
        output.WriteLine("{\"key\":\"" + key + "\",\"value\":\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"}");
        output.Flush();
    }
    static void AttributeRows<T>(Type derived) where T : Attribute
    {
        string key = typeof(T).FullName;
        var usage = (AttributeUsageAttribute)Attribute.GetCustomAttribute(typeof(T), typeof(AttributeUsageAttribute), true);
        Row(key + ".inherited", usage == null || usage.Inherited);
        Row(key + ".direct", derived.IsDefined(typeof(T), false));
        Row(key + ".derived", derived.IsDefined(typeof(T), true));
        Row(key + ".cache-base", TypeCache.GetTypesWithAttribute<T>().Contains(derived.BaseType));
        Row(key + ".cache-derived", TypeCache.GetTypesWithAttribute<T>().Contains(derived));
    }
    static void FieldRows<T>() where T : Attribute
    {
        string key = "field." + typeof(T).FullName;
        var inherited = typeof(AttributeInheritanceControls.FieldInherited).GetField("value");
        var shadow = typeof(AttributeInheritanceControls.FieldShadow).GetField("value");
        Row(key + ".direct", inherited.IsDefined(typeof(T), false));
        Row(key + ".inherit", inherited.IsDefined(typeof(T), true));
        Row(key + ".shadow", shadow.IsDefined(typeof(T), true));
        Row(key + ".cache-inherited-member", TypeCache.GetFieldsWithAttribute<T>().Contains(inherited));
        Row(key + ".cache-declaration", TypeCache.GetFieldsWithAttribute<T>().Any(f => f.DeclaringType == typeof(AttributeInheritanceControls.FieldBase) && f.Name == "value"));
        Row(key + ".cache-shadow", TypeCache.GetFieldsWithAttribute<T>().Contains(shadow));
    }
    void Start()
    {
        string path = Environment.GetEnvironmentVariable("ATTRIBUTE_INHERITANCE_OUT");
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            using (output = new StreamWriter(path))
            {
                Capture();
                DeclarationConditionalControls.Record(Row);
                ConditionalEnabledControl.Record(Row);
                DeclarationObsoleteControls.Record(Row);
                DeclarationUiControls.Record(Row);
                DeclarationMemberObsoleteControls.Record(Row);
                DeclarationTextEditorControls.Record(Row);
                DeclarationParameterControls.Record(Row);
        DeclarationEditorShapeControls.Record(Row);
        DeclarationCoreModifierControls.Record(Row);
        DeclarationAccessControls.Record(Row);
        DeclarationAccessorControls.Record(Row);
        DeclarationAnnotationControls.Record(Row);
                Row("complete", 1);
            }
            EditorApplication.Exit(0);
        }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
    }
    static void Capture()
    {
        AttributeRows<AttributeInheritanceControls.InheritedMarker>(typeof(AttributeInheritanceControls.MarkedChild));
        AttributeRows<AttributeInheritanceControls.DirectMarker>(typeof(AttributeInheritanceControls.DirectChild));
        AttributeRows<ExecuteAlways>(typeof(AttributeInheritanceControls.AlwaysChild));
        AttributeRows<ExecuteInEditMode>(typeof(AttributeInheritanceControls.EditChild));
        AttributeRows<DisallowMultipleComponent>(typeof(AttributeInheritanceControls.UniqueChild));
        AttributeRows<RequireComponent>(typeof(AttributeInheritanceControls.RequiredChild));
        AttributeRows<DefaultExecutionOrder>(typeof(AttributeInheritanceControls.OrderChild));
        AttributeRows<SharedBetweenAnimatorsAttribute>(typeof(AttributeInheritanceControls.SharedChild));
        AttributeRows<CreateAssetMenuAttribute>(typeof(AttributeInheritanceControls.MenuChild));
        AttributeRows<IconAttribute>(typeof(AttributeInheritanceControls.IconChild));
        AttributeRows<ExcludeFromPresetAttribute>(typeof(AttributeInheritanceControls.ExcludedChild));
        AttributeRows<CustomEditor>(typeof(AttributeInheritanceControls.UnmarkedEditorChild));
        AttributeRows<CanEditMultipleObjects>(typeof(AttributeInheritanceControls.InheritedMultiEditor));
        AttributeRows<CustomPropertyDrawer>(typeof(AttributeInheritanceControls.UnmarkedDrawerChild));
        AttributeRows<UxmlElementAttribute>(typeof(AttributeInheritanceControls.UnmarkedUxmlChild));
        AttributeRows<InitializeOnLoadAttribute>(typeof(AttributeInheritanceControls.InitializedChild));
        Row("attribute-subclass.direct", TypeCache.GetTypesWithAttribute<AttributeInheritanceControls.InheritedMarker>().Contains(typeof(AttributeInheritanceControls.DerivedAttributeBase)));
        Row("attribute-subclass.derived", TypeCache.GetTypesWithAttribute<AttributeInheritanceControls.InheritedMarker>().Contains(typeof(AttributeInheritanceControls.DerivedAttributeChild)));
        var method = typeof(AttributeInheritanceControls.MarkedChild).GetMethod("Method");
        Row("method.clr-derived", method.IsDefined(typeof(AttributeInheritanceControls.InheritedMarker), true));
        Row("method.cache-derived", TypeCache.GetMethodsWithAttribute<AttributeInheritanceControls.InheritedMarker>().Contains(method));
        Row("method.cache-base", TypeCache.GetMethodsWithAttribute<AttributeInheritanceControls.InheritedMarker>().Contains(typeof(AttributeInheritanceControls.MarkedBase).GetMethod("Method")));
        Row("field.shadowed", typeof(AttributeInheritanceControls.MarkedChild).GetField("field").IsDefined(typeof(AttributeInheritanceControls.InheritedMarker), true));
        Row("initialization.base", AttributeInheritanceControls.InitCounts.baseType);
        Row("initialization.derived", AttributeInheritanceControls.InitCounts.childType);
        Row("initialization.method", AttributeInheritanceControls.InitCounts.method);
        Row("initialization.hidden-method", AttributeInheritanceControls.InitCounts.hiddenMethod);
        Row("initialization.enter", AttributeInheritanceControls.InitCounts.enter);
        Row("initialization.hidden-enter", AttributeInheritanceControls.InitCounts.hiddenEnter);
        Row("initialization.reload", AttributeInheritanceControls.InitCounts.reload);
        Row("initialization.hidden-reload", AttributeInheritanceControls.InitCounts.hiddenReload);
        Row("initialization.scene", AttributeInheritanceControls.InitCounts.scene);
        Row("initialization.hidden-scene", AttributeInheritanceControls.InitCounts.hiddenScene);
        Row("initialization.runtime", AttributeInheritanceControls.InitCounts.runtime);
        Row("initialization.hidden-runtime", AttributeInheritanceControls.InitCounts.hiddenRuntime);
        Row("menu.executed", EditorApplication.ExecuteMenuItem("Q403/Attribute inheritance control"));
        Row("menu.base", AttributeInheritanceControls.InitCounts.menu);
        Row("menu.hidden", AttributeInheritanceControls.InitCounts.hiddenMenu);
        FieldRows<SerializeField>(); FieldRows<SerializeReference>(); FieldRows<FormerlySerializedAsAttribute>();
        FieldRows<HideInInspector>(); FieldRows<TooltipAttribute>(); FieldRows<ReadOnlyAttribute>(); FieldRows<WriteOnlyAttribute>();
        FieldRows<DeallocateOnJobCompletionAttribute>(); FieldRows<NativeDisableParallelForRestrictionAttribute>();
        FieldRows<NativeDisableContainerSafetyRestrictionAttribute>(); FieldRows<NativeDisableUnsafePtrRestrictionAttribute>();
        FieldRows<NativeSetClassTypeToNullOnScheduleAttribute>(); FieldRows<NativeSetThreadIndexAttribute>();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Former.prefab");
        Row("serialization.former-prefab", prefab.GetComponent<DerivedSerializedBehaviour>().Value);
        var go = new GameObject("required-control");
        go.AddComponent<AttributeInheritanceControls.RequiredChild>();
        Row("require.base", go.GetComponent<BoxCollider>() != null);
        Row("require.child", go.GetComponent<SphereCollider>() != null);
        UnityEngine.Object.DestroyImmediate(go);
        go = new GameObject("unique-control");
        go.AddComponent<AttributeInheritanceControls.UniqueBase>();
        go.AddComponent<AttributeInheritanceControls.UniqueChild>();
        Row("disallow.count", go.GetComponents<AttributeInheritanceControls.UniqueBase>().Length);
        UnityEngine.Object.DestroyImmediate(go);
        EditorControl<AttributeInheritanceControls.NoEditorTarget>("editor.base", typeof(AttributeInheritanceControls.NoChildEditor));
        EditorControl<AttributeInheritanceControls.NoEditorChild>("editor.no-children", typeof(AttributeInheritanceControls.NoChildEditor));
        EditorControl<AttributeInheritanceControls.YesEditorChild>("editor.children", typeof(AttributeInheritanceControls.YesChildEditor));
        var first = ScriptableObject.CreateInstance<AttributeInheritanceControls.MultiTarget>();
        var second = ScriptableObject.CreateInstance<AttributeInheritanceControls.MultiTarget>();
        var editor = Editor.CreateEditor(new UnityEngine.Object[] { first, second });
        Row("editor.inherited-multi", editor != null && editor.GetType() == typeof(AttributeInheritanceControls.InheritedMultiEditor));
        UnityEngine.Object.DestroyImmediate(editor); UnityEngine.Object.DestroyImmediate(first); UnityEngine.Object.DestroyImmediate(second);
        var values = ScriptableObject.CreateInstance<AttributeInheritanceControls.DrawerValues>();
        var serialized = new SerializedObject(values);
        Row("drawer.base", EditorGUI.GetPropertyHeight(serialized.FindProperty("no"), GUIContent.none, false) == 37);
        Row("drawer.no-children", EditorGUI.GetPropertyHeight(serialized.FindProperty("noChild"), GUIContent.none, false) == 37);
        Row("drawer.yes-base", EditorGUI.GetPropertyHeight(serialized.FindProperty("yes"), GUIContent.none, false) == 53);
        Row("drawer.children", EditorGUI.GetPropertyHeight(serialized.FindProperty("yesChild"), GUIContent.none, false) == 53);
        UnityEngine.Object.DestroyImmediate(values);
        var elementAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/Control.uxml");
        var elementRoot = elementAsset.CloneTree();
        var element = elementRoot.Q<AttributeInheritanceControls.UxmlChild>();
        Row("uxml.inherited-attribute", element != null && element.textLabel == "inherited-text");
        Row("uxml.inherited-type-reference", element != null && element.referenceType == typeof(BoxCollider));
        var property = typeof(AttributeInheritanceControls.UxmlChild).GetProperty("referenceType");
        Row("uxml.type-reference-direct", property.GetCustomAttribute<UxmlTypeReferenceAttribute>(false) != null);
        Row("uxml.type-reference-derived", property.GetCustomAttribute<UxmlTypeReferenceAttribute>(true) != null);
        ImageControl<AttributeInheritanceControls.OpaqueEffectBase>("image.base");
        ImageControl<AttributeInheritanceControls.OpaqueEffectInherited>("image.inherited");
        ImageControl<AttributeInheritanceControls.OpaqueEffectOverride>("image.override");
        Row("serialization.former-name", JsonUtility.FromJson<AttributeInheritanceControls.SerializedChild>("{\"oldValue\":29}").Value);
        Row("serialization.json-plain", JsonUtility.FromJson<AttributeInheritanceControls.PlainRename>("{\"oldValue\":29}").value);
        Row("serialization.json-nested", JsonUtility.FromJson<AttributeInheritanceControls.NestedRename>("{\"item\":{\"oldValue\":29}}").item.value);
        var plain = new AttributeInheritanceControls.PlainRename { value = 31 };
        JsonUtility.FromJsonOverwrite("{\"oldValue\":41}", plain);
        Row("serialization.json-overwrite-former", plain.value);
        JsonUtility.FromJsonOverwrite("{\"value\":47}", plain);
        Row("serialization.json-overwrite-current", plain.value);
        var asset = ScriptableObject.CreateInstance<AttributeInheritanceControls.RenameAsset>();
        asset.value = 31;
        asset.callbacks = 0;
        EditorJsonUtility.FromJsonOverwrite("{\"oldValue\":41}", asset);
        Row("serialization.editor-json-bare-callback", asset.callbacks);
        Row("serialization.editor-json-former", asset.value);
        EditorJsonUtility.FromJsonOverwrite("{\"value\":47}", asset);
        Row("serialization.editor-json-current", asset.value);
        asset.value = 31;
        EditorJsonUtility.FromJsonOverwrite("{\"MonoBehaviour\":{\"oldValue\":41}}", asset);
        Row("serialization.editor-json-wrapped-former", asset.value);
        asset.callbacks = 0;
        EditorJsonUtility.FromJsonOverwrite("{\"MonoBehaviour\":{\"value\":47}}", asset);
        Row("serialization.editor-json-wrapped-callback", asset.callbacks);
        Row("serialization.editor-json-wrapped-current", asset.value);
        asset.value = 31;
        asset.callbacks = 0;
        EditorJsonUtility.FromJsonOverwrite("{\"Wrong\":{\"value\":59}}", asset);
        Row("serialization.editor-json-wrong-callback", asset.callbacks);
        Row("serialization.editor-json-wrong-wrapper", asset.value);
        asset.value = 31;
        EditorJsonUtility.FromJsonOverwrite("{\"MonoBehaviour\":{\"value\":61},\"other\":2}", asset);
        Row("serialization.editor-json-multiple-root", asset.value);
        var editorPlain = new AttributeInheritanceControls.PlainRename { value = 31 };
        EditorJsonUtility.FromJsonOverwrite("{\"oldValue\":41}", editorPlain);
        Row("serialization.editor-json-plain-former", editorPlain.value);
        EditorJsonUtility.FromJsonOverwrite("{\"value\":47}", editorPlain);
        Row("serialization.editor-json-plain-current", editorPlain.value);
        UnityEngine.Object.DestroyImmediate(asset);
    }
    static void ImageControl<T>(string key) where T : Component
    {
        var go = new GameObject("image-control");
        var camera = go.AddComponent<Camera>(); camera.enabled = false;
        var texture = new RenderTexture(8, 8, 24);
        camera.targetTexture = texture;
        go.AddComponent<T>();
        AttributeInheritanceControls.ImageCalls.values.Clear();
        camera.Render();
        Row(key, string.Join(",", AttributeInheritanceControls.ImageCalls.values));
        UnityEngine.Object.DestroyImmediate(go);
        texture.Release(); UnityEngine.Object.DestroyImmediate(texture);
    }
    static void EditorControl<T>(string key, Type expected) where T : ScriptableObject
    {
        var target = ScriptableObject.CreateInstance<T>();
        var editor = Editor.CreateEditor(target);
        Row(key, editor != null && editor.GetType() == expected);
        UnityEngine.Object.DestroyImmediate(editor); UnityEngine.Object.DestroyImmediate(target);
    }
}
