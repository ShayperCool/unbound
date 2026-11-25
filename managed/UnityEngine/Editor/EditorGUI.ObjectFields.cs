using System;
using Unbound.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityEditor
{
    /// <summary>
    /// EditorGUI's object fields as Unity 6000.0's C# does them: a keyboard-focusable control that
    /// takes a dropped object its validator accepts (a GameObject's component for a component
    /// type; scene objects only where allowed), pings its object on a click, clears it with Delete,
    /// Backspace or the Delete command while focused, and opens the object picker from its button
    /// (Unbound shows no picker: said once, and the GUI exits as the Editor's does); a texture or
    /// sprite field taller than a line is a square thumbnail (measured:
    /// tests/projects/editorobjectfields, values, control ids, GUI.changed, layout rects and
    /// scripted clicks, keys and drops). Other families report the fields not implemented and
    /// return the object they are given.
    /// </summary>
    public sealed partial class EditorGUI
    {
        static readonly int s_PPtrHash = "s_PPtrHash".GetHashCode();
        static readonly int s_ObjectFieldHash = "s_ObjectFieldHash".GetHashCode();

        internal const float kObjectFieldThumbnailHeight = 64;
        internal const float kObjectFieldMiniThumbnailHeight = 18f;
        internal const float kObjectFieldMiniThumbnailWidth = 32f;
        const float k_NullObjectReferenceOpacity = 0.7f;

        [Flags]
        internal enum ObjectFieldValidatorOptions
        {
            None = 0,
            ExactObjectTypeValidation = (1 << 0)
        }

        internal delegate Object ObjectFieldValidator(Object[] references, Type objType, SerializedProperty property, ObjectFieldValidatorOptions options);

        internal enum ObjectFieldVisualType { IconAndText, LargePreview, MiniPreview }

        internal static GUIStyle ObjectFieldStyle => Compat.EditorValueFieldsMeasured ? EditorStyles.objectField : null;

        /// <summary>Whether this family's object, colour, curve, gradient, mask, layer and tag
        /// fields are measured; elsewhere the field says it is not implemented.</summary>
        static bool PickerFieldsMeasured(string member)
        {
            if (Compat.EditorValueFieldsMeasured) return true;
            Stubs.NotImplemented("UnityEditor.EditorGUI." + member);
            return false;
        }

        public static void ObjectField(Rect position, SerializedProperty property) =>
            ObjectField(position, property, null, null, ObjectFieldStyle);

        public static void ObjectField(Rect position, SerializedProperty property, GUIContent label) =>
            ObjectField(position, property, null, label, ObjectFieldStyle);

        public static void ObjectField(Rect position, SerializedProperty property, Type objType) =>
            ObjectField(position, property, objType, null, ObjectFieldStyle);

        public static void ObjectField(Rect position, SerializedProperty property, Type objType, GUIContent label) =>
            ObjectField(position, property, objType, label, ObjectFieldStyle);

        internal static void ObjectField(Rect position, SerializedProperty property, Type objType, GUIContent label, GUIStyle style, ObjectFieldValidator validator = null)
        {
            if (!PickerFieldsMeasured("ObjectField")) return;
            label = BeginProperty(position, label, property);
            ObjectFieldInternal(position, property, objType, label, style, validator);
            EndProperty();
        }

        // Without BeginProperty / EndProperty (PropertyField's object references).
        internal static void ObjectFieldInternal(Rect position, SerializedProperty property, Type objType, GUIContent label, GUIStyle style, ObjectFieldValidator validator = null)
        {
            int id = GUIUtility.GetControlID(s_PPtrHash, FocusType.Keyboard, position);
            position = PrefixLabel(position, id, label);

            bool allowSceneObjects = false;
            if (property != null)
            {
                Object objectBeingEdited = property.serializedObject.targetObject;
                // Scene objects are allowed when the object being edited is not persistent.
                if (objectBeingEdited != null && !EditorUtility.IsPersistent(objectBeingEdited))
                    allowSceneObjects = true;
            }
            DoObjectField(position, position, id, null, null, objType, property, validator, allowSceneObjects, style);
        }

        public static Object ObjectField(Rect position, Object obj, Type objType, Object targetBeingEdited)
        {
            if (!PickerFieldsMeasured("ObjectField")) return obj;
            int id = GUIUtility.GetControlID(s_ObjectFieldHash, FocusType.Keyboard, position);
            return DoObjectField(IndentedRect(position), IndentedRect(position), id, obj, targetBeingEdited, objType, null, null, true, ObjectFieldStyle);
        }

        public static Object ObjectField(Rect position, Object obj, Type objType, bool allowSceneObjects)
        {
            if (!PickerFieldsMeasured("ObjectField")) return obj;
            int id = GUIUtility.GetControlID(s_ObjectFieldHash, FocusType.Keyboard, position);
            return DoObjectField(IndentedRect(position), IndentedRect(position), id, obj, null, objType, null, null, allowSceneObjects, ObjectFieldStyle);
        }

        [Obsolete("Check the docs for the usage of the new parameter 'allowSceneObjects'.")]
        public static Object ObjectField(Rect position, Object obj, Type objType)
        {
            if (!PickerFieldsMeasured("ObjectField")) return obj;
            int id = GUIUtility.GetControlID(s_ObjectFieldHash, FocusType.Keyboard, position);
            return DoObjectField(position, position, id, obj, null, objType, null, null, true, ObjectFieldStyle);
        }

        public static Object ObjectField(Rect position, string label, Object obj, Type objType, Object targetBeingEdited) =>
            ObjectField(position, EditorGUIUtility.TempContent(label), obj, objType, targetBeingEdited);

        public static Object ObjectField(Rect position, string label, Object obj, Type objType, bool allowSceneObjects) =>
            ObjectField(position, EditorGUIUtility.TempContent(label), obj, objType, allowSceneObjects);

        [Obsolete("Check the docs for the usage of the new parameter 'allowSceneObjects'.")]
        public static Object ObjectField(Rect position, string label, Object obj, Type objType) =>
            ObjectField(position, EditorGUIUtility.TempContent(label), obj, objType, true);

        static Rect GetObjectFieldThumbnailRect(Rect position, Type objType)
        {
            if (EditorGUIUtility.HasObjectThumbnail(objType) && position.height > kSingleLineHeight)
            {
                // A thumbnail field is square and aligned to the right.
                float size = Mathf.Min(position.width, position.height);
                position.height = size;
                position.xMin = position.xMax - size;
            }
            return position;
        }

        public static Object ObjectField(Rect position, GUIContent label, Object obj, Type objType, Object targetBeingEdited)
        {
            if (!PickerFieldsMeasured("ObjectField")) return obj;
            int id = GUIUtility.GetControlID(s_ObjectFieldHash, FocusType.Keyboard, position);
            position = PrefixLabel(position, id, label);
            position = GetObjectFieldThumbnailRect(position, objType);
            return DoObjectField(position, position, id, obj, targetBeingEdited, objType, null, null, true, ObjectFieldStyle);
        }

        public static Object ObjectField(Rect position, GUIContent label, Object obj, Type objType, bool allowSceneObjects)
        {
            if (!PickerFieldsMeasured("ObjectField")) return obj;
            int id = GUIUtility.GetControlID(s_ObjectFieldHash, FocusType.Keyboard, position);
            position = PrefixLabel(position, id, label);
            position = GetObjectFieldThumbnailRect(position, objType);
            return DoObjectField(position, position, id, obj, null, objType, null, null, allowSceneObjects, ObjectFieldStyle);
        }

        [Obsolete("Check the docs for the usage of the new parameter 'allowSceneObjects'.")]
        public static Object ObjectField(Rect position, GUIContent label, Object obj, Type objType) =>
            ObjectField(position, label, obj, objType, true);

        internal static GameObject GetGameObjectFromObject(Object obj)
        {
            var go = obj as GameObject;
            if (go == null && obj is Component component) go = component.gameObject;
            return go;
        }

        internal static bool CheckForCrossSceneReferencing(Object obj1, Object obj2)
        {
            GameObject go = GetGameObjectFromObject(obj1);
            if (go == null) return false;
            GameObject go2 = GetGameObjectFromObject(obj2);
            if (go2 == null) return false;
            if (EditorUtility.IsPersistent(go) || EditorUtility.IsPersistent(go2)) return false;
            if (!go.scene.IsValid() || !go2.scene.IsValid()) return false;
            return go.scene != go2.scene;
        }

        static bool ValidateObjectReferenceValue(SerializedProperty property, Object obj, ObjectFieldValidatorOptions options)
        {
            if ((options & ObjectFieldValidatorOptions.ExactObjectTypeValidation) == ObjectFieldValidatorOptions.ExactObjectTypeValidation)
                return property.ValidateObjectReferenceValueExact(obj);
            return property.ValidateObjectReferenceValue(obj);
        }

        /// <summary>The object of <paramref name="references"/> an object field of <paramref name="objType"/>
        /// takes: the first of the right type (a GameObject's components count for a component
        /// type), or the property's referenced type's.</summary>
        internal static Object ValidateObjectFieldAssignment(Object[] references, Type objType, SerializedProperty property, ObjectFieldValidatorOptions options)
        {
            if (references.Length > 0)
            {
                bool dragAssignment = DragAndDrop.objectReferences.Length > 0;
                bool isTextureRef = references[0] != null && references[0] is Texture2D;

                if (objType == typeof(Sprite) && isTextureRef && dragAssignment)
                    return TextureToSprite(references[0] as Texture2D);

                if (property != null)
                {
                    if (references[0] != null && ValidateObjectReferenceValue(property, references[0], options))
                    {
                        if (UnityEditor.SceneManagement.EditorSceneManager.preventCrossSceneReferences && CheckForCrossSceneReferencing(references[0], property.serializedObject.targetObject))
                            return null;

                        if (objType != null)
                        {
                            if (references[0] is GameObject && typeof(Component).IsAssignableFrom(objType))
                                references = ((GameObject)references[0]).GetComponents(typeof(Component));
                            foreach (Object i in references)
                                if (i != null && objType.IsAssignableFrom(i.GetType()))
                                    return i;
                        }
                        else
                        {
                            return references[0];
                        }
                    }

                    // An array is tested against its element type.
                    string testElementType = property.type;
                    if (property.type == "vector") testElementType = property.arrayElementType;
                    if ((testElementType == "PPtr<Sprite>" || testElementType == "PPtr<$Sprite>") && isTextureRef && dragAssignment)
                        return TextureToSprite(references[0] as Texture2D);
                }
                else
                {
                    if (references[0] != null && references[0] is GameObject && typeof(Component).IsAssignableFrom(objType))
                        references = ((GameObject)references[0]).GetComponents(typeof(Component));
                    foreach (Object i in references)
                        if (i != null && objType.IsAssignableFrom(i.GetType()))
                            return i;
                }
            }
            return null;
        }

        /// <summary>A texture dropped on a sprite field: the sprite of the texture's asset. Unbound
        /// does not look the asset's sprites up (said once).</summary>
        static Sprite TextureToSprite(Texture2D texture)
        {
            Stubs.Ignored("UnityEditor.SpriteUtility.TextureToSprite", "a texture dropped on a sprite field gives no sprite: Unbound does not look up the sprites of the texture's asset");
            return null;
        }

        /// <summary>The object a property takes for a dropped one: a GameObject's component of the
        /// property's component type (matches the Unity 6000.0.71f1 Editor,
        /// tests/projects/editorobjectfields script/sp.target.dropGameObject).</summary>
        static Object PropertyReference(SerializedProperty property, Object validated)
        {
            var type = property.ObjectReferenceFieldType;
            if (validated is GameObject go && type != null && typeof(Component).IsAssignableFrom(type))
                return go.GetComponent(type);
            return validated;
        }

        static Rect GetButtonRect(ObjectFieldVisualType visualType, Rect position)
        {
            switch (visualType)
            {
                case ObjectFieldVisualType.IconAndText:
                    return new Rect(position.xMax - 19, position.y, 19, position.height);
                case ObjectFieldVisualType.MiniPreview:
                    return new Rect(position.xMax - 14, position.y, 14, position.height);
                case ObjectFieldVisualType.LargePreview:
                    return new Rect(position.xMax - 36, position.yMax - 14, 36, 14);
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        static bool HasValidScript(Object obj)
        {
            MonoScript script = obj is MonoBehaviour behaviour ? MonoScript.FromMonoBehaviour(behaviour)
                : obj is ScriptableObject scriptable ? MonoScript.FromScriptableObject(scriptable) : null;
            return script != null && script.GetClass() != null;
        }

        static bool ValidDroppedObject(Object[] references, out string errorString)
        {
            errorString = "";
            if (references == null || references.Length == 0) return true;
            var reference = references[0];
            if (reference is MonoBehaviour || reference is ScriptableObject)
            {
                if (!HasValidScript(reference))
                {
                    errorString = $"Type cannot be found: {reference.GetType()}. Containing file and class name must match.";
                    return false;
                }
            }
            return true;
        }

        internal static Object DoObjectField(Rect position, Rect dropRect, int id, Object obj, Object objBeingEdited, Type objType, ObjectFieldValidator validator, bool allowSceneObjects, GUIStyle style = null) =>
            DoObjectField(position, dropRect, id, obj, objBeingEdited, objType, null, validator, allowSceneObjects, style ?? ObjectFieldStyle);

        static Object DoObjectField(Rect position, Rect dropRect, int id, Object obj, Object objBeingEdited, Type objType, SerializedProperty property, ObjectFieldValidator validator, bool allowSceneObjects, GUIStyle style)
        {
            GUIStyle buttonStyle = EditorStyles.objectFieldButton;
            validator ??= ValidateObjectFieldAssignment;
            if (property != null) obj = property.objectReferenceValue;
            Event evt = Event.current;
            EventType eventType = evt.type;

            // A disabled field still pings and selects its object.
            if (!GUI.enabled && GUIClip.enabled && evt.rawType == EventType.MouseDown)
                eventType = evt.rawType;

            bool hasThumbnail = EditorGUIUtility.HasObjectThumbnail(objType);

            ObjectFieldVisualType visualType = ObjectFieldVisualType.IconAndText;
            if (hasThumbnail && position.height <= kObjectFieldMiniThumbnailHeight && position.width <= kObjectFieldMiniThumbnailWidth)
                visualType = ObjectFieldVisualType.MiniPreview;
            else if (hasThumbnail && position.height > kSingleLineHeight)
                visualType = ObjectFieldVisualType.LargePreview;

            Vector2 oldIconSize = EditorGUIUtility.GetIconSize();
            if (visualType == ObjectFieldVisualType.IconAndText)
                EditorGUIUtility.SetIconSize(new Vector2(12, 12));
            else if (visualType == ObjectFieldVisualType.LargePreview)
                EditorGUIUtility.SetIconSize(new Vector2(64, 64));

            if (eventType == EventType.ContextClick && visualType == ObjectFieldVisualType.IconAndText && position.Contains(evt.mousePosition))
            {
                Stubs.Ignored("UnityEditor.EditorGUI.ObjectField's context menu", "is not shown: Unbound shows no editor menus");
                evt.Use();
            }

            switch (eventType)
            {
                case EventType.DragExited:
                    break;
                case EventType.DragUpdated:
                case EventType.DragPerform:
                    if (eventType == EventType.DragPerform && !ValidDroppedObject(DragAndDrop.objectReferences, out string errorString))
                    {
                        EditorUtility.DisplayDialog("Can't assign script", errorString, "OK");
                        break;
                    }
                    if (dropRect.Contains(evt.mousePosition) && GUI.enabled)
                    {
                        Object validatedObject = validator(DragAndDrop.objectReferences, objType, property, ObjectFieldValidatorOptions.None);
                        // Scene objects are cleared where they are not allowed.
                        if (validatedObject != null && !allowSceneObjects && !EditorUtility.IsPersistent(validatedObject))
                            validatedObject = null;

                        if (validatedObject != null)
                        {
                            if (DragAndDrop.visualMode == DragAndDropVisualMode.None)
                                DragAndDrop.visualMode = DragAndDropVisualMode.Generic;

                            if (eventType == EventType.DragPerform)
                            {
                                if (property != null) property.objectReferenceValue = PropertyReference(property, validatedObject);
                                else obj = validatedObject;
                                GUI.changed = true;
                                DragAndDrop.AcceptDrag();
                                DragAndDrop.activeControlID = 0;
                            }
                            else
                            {
                                DragAndDrop.activeControlID = id;
                            }
                            evt.Use();
                        }
                    }
                    break;
                case EventType.MouseDown:
                    if (position.Contains(evt.mousePosition) && evt.button == 0)
                    {
                        Rect buttonRect = GetButtonRect(visualType, position);
                        EditorGUIUtility.editingTextField = false;

                        if (buttonRect.Contains(evt.mousePosition))
                        {
                            if (GUI.enabled)
                            {
                                GUIUtility.keyboardControl = id;
                                ShowObjectSelector();
                                evt.Use();
                                GUIUtility.ExitGUI();
                            }
                        }
                        else
                        {
                            Object actualTargetObject = property != null ? property.objectReferenceValue : obj;
                            if (actualTargetObject is Component com) actualTargetObject = com.gameObject;
                            if (showMixedValue) actualTargetObject = null;

                            if (evt.clickCount == 1)
                            {
                                GUIUtility.keyboardControl = id;
                                PingObjectOrShowPreviewOnClick(actualTargetObject, position);
                                evt.Use();
                            }
                            else if (evt.clickCount == 2)
                            {
                                if (actualTargetObject)
                                {
                                    AssetDatabase.OpenAsset(actualTargetObject);
                                    evt.Use();
                                    GUIUtility.ExitGUI();
                                }
                            }
                        }
                    }
                    break;
                case EventType.ExecuteCommand:
                    if ((evt.commandName == EventCommandNames.Delete || evt.commandName == EventCommandNames.SoftDelete) && GUIUtility.keyboardControl == id)
                    {
                        if (property != null) property.objectReferenceValue = null;
                        else obj = null;
                        GUI.changed = true;
                        evt.Use();
                    }
                    break;
                case EventType.ValidateCommand:
                    if ((evt.commandName == EventCommandNames.Delete || evt.commandName == EventCommandNames.SoftDelete) && GUIUtility.keyboardControl == id)
                        evt.Use();
                    break;
                case EventType.KeyDown:
                    if (GUIUtility.keyboardControl == id)
                    {
                        if (evt.keyCode == KeyCode.Backspace || (evt.keyCode == KeyCode.Delete && (evt.modifiers & EventModifiers.Shift) == 0))
                        {
                            if (property != null)
                            {
                                if (property.propertyPath.EndsWith("]"))
                                    Stubs.Ignored("UnityEditor.EditorGUI.ObjectField on an array element", "Delete clears the element's reference; an element of a list drawn without a reorderable list is not removed");
                                property.objectReferenceValue = null;
                            }
                            else obj = null;
                            GUI.changed = true;
                            evt.Use();
                        }

                        if (MainActionKeyForControl(evt, id))
                        {
                            ShowObjectSelector();
                            evt.Use();
                            GUIUtility.ExitGUI();
                        }
                    }
                    break;
                case EventType.Repaint:
                    GUIContent temp = showMixedValue ? s_MixedValueContent : ObjectFieldContent(obj, objType, property);
                    var contentColor = GUI.contentColor;
                    var oldAlpha = contentColor.a;
                    try
                    {
                        // A null reference's label at 70 % opacity.
                        if (obj == null)
                        {
                            contentColor.a = k_NullObjectReferenceOpacity;
                            GUI.contentColor = contentColor;
                        }
                        switch (visualType)
                        {
                            case ObjectFieldVisualType.IconAndText:
                                BeginHandleMixedValueContentColor();
                                style.Draw(position, temp, id, DragAndDrop.activeControlID == id, position.Contains(evt.mousePosition));
                                Rect buttonRect = buttonStyle.margin.Remove(GetButtonRect(visualType, position));
                                buttonStyle.Draw(buttonRect, GUIContent.none, id, DragAndDrop.activeControlID == id, buttonRect.Contains(evt.mousePosition));
                                EndHandleMixedValueContentColor();
                                break;
                            case ObjectFieldVisualType.LargePreview:
                                DrawObjectFieldLargeThumb(position, id, obj, temp);
                                break;
                            case ObjectFieldVisualType.MiniPreview:
                                DrawObjectFieldMiniThumb(position, id, obj, temp);
                                break;
                        }
                    }
                    finally
                    {
                        contentColor.a = oldAlpha;
                        GUI.contentColor = contentColor;
                    }
                    break;
            }

            EditorGUIUtility.SetIconSize(oldIconSize);
            return obj;
        }

        /// <summary>What an object field shows: a property's reference ("None (Material)",
        /// "Missing (...)", by the property's type when the field has none), else the object's.</summary>
        internal static GUIContent ObjectFieldContent(Object obj, Type objType, SerializedProperty property) =>
            property != null ? EditorGUIUtility.ObjectContentForProperty(obj, objType, property) : EditorGUIUtility.ObjectContent(obj, objType);

        /// <summary>The Editor's object picker: Unbound shows none (said once).</summary>
        static void ShowObjectSelector() =>
            Stubs.Ignored("UnityEditor.ObjectSelector", "the object picker is not shown: Unbound has no editor windows, so the field keeps its object");

        /// <summary>A click pings the object; with Shift or Control a texture opens a preview window
        /// (Unbound shows none, said once).</summary>
        internal static void PingObjectOrShowPreviewOnClick(Object targetObject, Rect position)
        {
            if (targetObject == null) return;
            Event evt = Event.current;
            if (!(evt.shift || evt.control))
            {
                EditorGUIUtility.PingObject(targetObject);
                return;
            }
            if (targetObject is Texture)
                Stubs.Ignored("UnityEditor.ObjectPreviewPopup", "a texture's preview window is not shown: Unbound has no editor windows");
        }

        /// <summary>A thumbnail field's frame (the object's preview is not drawn: said once).</summary>
        static void DrawObjectFieldLargeThumb(Rect position, int id, Object obj, GUIContent content)
        {
            GUIStyle thumbStyle = EditorStyles.objectFieldThumb;
            thumbStyle.Draw(position, GUIContent.none, id, DragAndDrop.activeControlID == id, position.Contains(Event.current.mousePosition));
            if (obj != null && !showMixedValue)
                Stubs.Ignored("UnityEditor.EditorGUI.ObjectField's thumbnail", "an object field's large preview of its object is not drawn");
            else
            {
                BeginHandleMixedValueContentColor();
                GUIStyle overlay = EditorGuiSkins.Current.FindStyle(thumbStyle.name + "Overlay");
                overlay?.Draw(position, content, id);
                EndHandleMixedValueContentColor();
            }
            GUIStyle overlay2 = EditorGuiSkins.Current.FindStyle(thumbStyle.name + "Overlay2");
            overlay2?.Draw(position, s_Select, id);
        }

        static void DrawObjectFieldMiniThumb(Rect position, int id, Object obj, GUIContent content)
        {
            GUIStyle thumbStyle = EditorStyles.objectFieldMiniThumb;
            position.width = kObjectFieldMiniThumbnailWidth;
            BeginHandleMixedValueContentColor();
            thumbStyle.Draw(position, obj != null, false, DragAndDrop.activeControlID == id, GUIUtility.keyboardControl == id);
            EndHandleMixedValueContentColor();
            if (obj != null && !showMixedValue)
                Stubs.Ignored("UnityEditor.EditorGUI.ObjectField's mini thumbnail", "an object field's small preview of its object is not drawn");
        }

        static readonly GUIContent s_Select = new GUIContent("Select");
        static Color s_MixedValueContentColorPrevious = Color.white;

        internal static void BeginHandleMixedValueContentColor()
        {
            s_MixedValueContentColorPrevious = GUI.contentColor;
            GUI.contentColor = showMixedValue ? GUI.contentColor * s_MixedValueContentColor : GUI.contentColor;
        }

        internal static void EndHandleMixedValueContentColor() => GUI.contentColor = s_MixedValueContentColorPrevious;

        internal static GUIContent mixedValueContent => s_MixedValueContent;
    }

    public sealed partial class EditorGUILayout
    {
        [Obsolete("Check the docs for the usage of the new parameter 'allowSceneObjects'.")]
        public static Object ObjectField(Object obj, Type objType, params GUILayoutOption[] options) => ObjectField(obj, objType, true, options);

        public static Object ObjectField(Object obj, Type objType, Object targetBeingEdited, params GUILayoutOption[] options) =>
            EditorGUI.ObjectField(GetControlRect(false, EditorGUI.kSingleLineHeight, options), obj, objType, targetBeingEdited);

        public static Object ObjectField(Object obj, Type objType, bool allowSceneObjects, params GUILayoutOption[] options) =>
            EditorGUI.ObjectField(GetControlRect(false, EditorGUI.kSingleLineHeight, options), obj, objType, allowSceneObjects);

        [Obsolete("Check the docs for the usage of the new parameter 'allowSceneObjects'.")]
        public static Object ObjectField(string label, Object obj, Type objType, params GUILayoutOption[] options) => ObjectField(label, obj, objType, true, options);

        public static Object ObjectField(string label, Object obj, Type objType, Object targetBeingEdited, params GUILayoutOption[] options) =>
            ObjectField(EditorGUIUtility.TempContent(label), obj, objType, targetBeingEdited, options);

        public static Object ObjectField(string label, Object obj, Type objType, bool allowSceneObjects, params GUILayoutOption[] options) =>
            ObjectField(EditorGUIUtility.TempContent(label), obj, objType, allowSceneObjects, options);

        [Obsolete("Check the docs for the usage of the new parameter 'allowSceneObjects'.")]
        public static Object ObjectField(GUIContent label, Object obj, Type objType, params GUILayoutOption[] options) => ObjectField(label, obj, objType, true, options);

        static float ObjectFieldHeight(Type objType) =>
            EditorGUIUtility.HasObjectThumbnail(objType) ? EditorGUI.kObjectFieldThumbnailHeight : EditorGUI.kSingleLineHeight;

        public static Object ObjectField(GUIContent label, Object obj, Type objType, Object targetBeingEdited, params GUILayoutOption[] options) =>
            EditorGUI.ObjectField(GetControlRect(true, ObjectFieldHeight(objType), options), label, obj, objType, targetBeingEdited);

        public static Object ObjectField(GUIContent label, Object obj, Type objType, bool allowSceneObjects, params GUILayoutOption[] options) =>
            EditorGUI.ObjectField(GetControlRect(true, ObjectFieldHeight(objType), options), label, obj, objType, allowSceneObjects);

        public static void ObjectField(SerializedProperty property, params GUILayoutOption[] options) => ObjectField(property, (GUIContent)null, options);

        public static void ObjectField(SerializedProperty property, GUIContent label, params GUILayoutOption[] options) =>
            EditorGUI.ObjectField(GetControlRect(true, EditorGUI.kSingleLineHeight, EditorGUI.ObjectFieldStyle, options), property, label);

        public static void ObjectField(SerializedProperty property, Type objType, params GUILayoutOption[] options) => ObjectField(property, objType, null, options);

        public static void ObjectField(SerializedProperty property, Type objType, GUIContent label, params GUILayoutOption[] options) =>
            EditorGUI.ObjectField(GetControlRect(true, EditorGUI.kSingleLineHeight, EditorGUI.ObjectFieldStyle, options), property, objType, label);
    }

    public partial class SerializedProperty
    {
        /// <summary>Whether an object reference property can reference the object: one of its
        /// referenced type, or a GameObject with a component of it (the object fields take the
        /// component; tests/projects/editorobjectfields script/sp.target.*).</summary>
        internal bool ValidateObjectReferenceValue(Object obj)
        {
            var type = ObjectReferenceFieldType;
            if (obj == null || type == null) return false;
            if (type.IsInstanceOfType(obj)) return true;
            return obj is GameObject go && typeof(Component).IsAssignableFrom(type) && go.GetComponent(type) != null;
        }

        /// <summary>The same for exactly the referenced type (unmeasured: a GameObject's
        /// component of exactly that type).</summary>
        internal bool ValidateObjectReferenceValueExact(Object obj)
        {
            var type = ObjectReferenceFieldType;
            if (obj == null || type == null) return false;
            if (obj.GetType() == type) return true;
            return obj is GameObject go && typeof(Component).IsAssignableFrom(type) && go.GetComponent(type)?.GetType() == type;
        }
    }

    public sealed partial class EditorGUIUtility
    {
        /// <summary>Textures and sprites have a thumbnail in an object field.</summary>
        public static bool HasObjectThumbnail(Type objType) =>
            objType != null && (objType.IsSubclassOf(typeof(Texture)) || objType == typeof(Texture) || objType == typeof(Sprite));
    }
}
