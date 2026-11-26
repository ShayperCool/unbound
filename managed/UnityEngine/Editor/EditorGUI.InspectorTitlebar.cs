using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityEditor
{
    /// <summary>
    /// The inspector-window titlebar of EditorGUI and EditorGUILayout: Unity's C# (EditorGUI
    /// InspectorTitlebar, DoInspectorTitlebar, DoObjectMouseInteraction, DoObjectFoldoutInternal),
    /// with the object enabled toggle, the header items (preset and help buttons), the object icon,
    /// the title and the settings button. Measured against the Unity 6000.0.71f1 Editor in Play mode
    /// (tests/projects/editortitlebar): returned values, control ids, GUI.changed, layout rects and
    /// the scripted clicks and keys. Menus and windows the titlebar opens (an object's context menu,
    /// the preset selector, the help page) and drags into other views are reported, as Unbound has
    /// no editor views.
    /// </summary>
    public sealed partial class EditorGUI
    {
        static readonly int s_TitlebarHash = "GenericTitlebar".GetHashCode();
        internal const int kInspTitlebarIconWidth = 16;
        internal const int kInspTitlebarFoldoutIconWidth = 13;
        const int kInspTitlebarSpacing = 4;
        const string kEnabledPropertyName = "m_Enabled";
        static GUIContent s_TitleSettingsIcon, s_HelpIcon, s_PresetIcon;

        static GUIContent TitleSettingsIcon => s_TitleSettingsIcon ??= EditorGUIUtility.IconContent("_Popup");
        static GUIContent HelpIcon => s_HelpIcon ??= EditorGUIUtility.IconContent("_Help");
        static GUIContent PresetIcon => s_PresetIcon ??= EditorGUIUtility.IconContent("Preset.Context");

        /// <summary>A titlebar without a foldout.</summary>
        public static void InspectorTitlebar(Rect position, Object[] targetObjs)
        {
            if (!Compat.EditorTitlebarMeasured) { Stubs.NotImplemented("UnityEditor.EditorGUI.InspectorTitlebar"); return; }
            GUIStyle baseStyle = GUIStyle.none;
            int id = GUIUtility.GetControlID(s_TitlebarHash, FocusType.Keyboard, position);
            DoInspectorTitlebar(position, id, true, targetObjs, null, baseStyle);
        }

        public static bool InspectorTitlebar(Rect position, bool foldout, Object targetObj, bool expandable) =>
            InspectorTitlebar(position, foldout, new[] { targetObj }, expandable);

        /// <summary>A foldable titlebar.</summary>
        public static bool InspectorTitlebar(Rect position, bool foldout, Object[] targetObjs, bool expandable)
        {
            if (!Compat.EditorTitlebarMeasured) { Stubs.NotImplemented("UnityEditor.EditorGUI.InspectorTitlebar"); return foldout; }
            GUIStyle baseStyle = EditorStyles.inspectorTitlebar;
            // The foldout's id comes first, so it takes the keyboard focus before the toggle does.
            int id = GUIUtility.GetControlID(s_TitlebarHash, FocusType.Keyboard, position);
            DoInspectorTitlebar(position, id, foldout, targetObjs, null, baseStyle);
            foldout = DoObjectMouseInteraction(foldout, position, targetObjs, id);
            if (expandable)
                DoObjectFoldoutInternal(foldout, GetInspectorTitleBarObjectFoldoutRenderRect(position, baseStyle), id);
            return foldout;
        }

        /// <summary>A foldable titlebar of an editor's targets, its toggle the editor's m_Enabled.</summary>
        public static bool InspectorTitlebar(Rect position, bool foldout, Editor editor)
        {
            if (!Compat.EditorTitlebarMeasured) { Stubs.NotImplemented("UnityEditor.EditorGUI.InspectorTitlebar"); return foldout; }
            GUIStyle baseStyle = EditorStyles.inspectorTitlebar;
            int id = GUIUtility.GetControlID(s_TitlebarHash, FocusType.Keyboard, position);
            DoInspectorTitlebar(position, id, foldout, editor.targets, editor.enabledProperty, baseStyle);
            foldout = DoObjectMouseInteraction(foldout, position, editor.targets, id);
            if (editor.CanBeExpandedViaAFoldout())
                DoObjectFoldoutInternal(foldout, GetInspectorTitleBarObjectFoldoutRenderRect(position, baseStyle), id);
            return foldout;
        }

        internal static Rect GetInspectorTitleBarObjectFoldoutRenderRect(Rect rect) => GetInspectorTitleBarObjectFoldoutRenderRect(rect, null);

        internal static Rect GetInspectorTitleBarObjectFoldoutRenderRect(Rect rect, GUIStyle baseStyle) =>
            new Rect(rect.x + EditorStyles.titlebarFoldout.margin.left + 1f,
                rect.y + (rect.height - kInspTitlebarFoldoutIconWidth) / 2 + (baseStyle != null ? baseStyle.padding.top : 0),
                kInspTitlebarFoldoutIconWidth, kInspTitlebarFoldoutIconWidth);

        static Rect GetIconRect(Rect position, GUIStyle baseStyle) =>
            new Rect(position.x + baseStyle.padding.left,
                position.y + (baseStyle.fixedHeight - kInspTitlebarIconWidth) / 2 + baseStyle.padding.top,
                kInspTitlebarIconWidth, kInspTitlebarIconWidth);

        static Rect GetTextRect(Rect position, Rect iconRect, Rect settingsRect, GUIStyle baseStyle, GUIStyle textStyle) =>
            new Rect(iconRect.xMax + kInspTitlebarSpacing + kInspTitlebarSpacing + kInspTitlebarIconWidth,
                position.y + (baseStyle.fixedHeight - textStyle.fixedHeight) / 2 + baseStyle.padding.top, 100, textStyle.fixedHeight)
            { xMax = settingsRect.xMin - kInspTitlebarSpacing };

        static Rect GetSettingsRect(Rect position, GUIStyle baseStyle, GUIStyle iconButtonStyle)
        {
            Vector2 size = iconButtonStyle.CalcSize(TitleSettingsIcon);
            return new Rect(position.xMax - baseStyle.padding.right - kInspTitlebarSpacing - kInspTitlebarIconWidth,
                position.y + (baseStyle.fixedHeight - size.y) / 2 + baseStyle.padding.top, size.x, size.y);
        }

        internal static bool EnableCheckBoxInTitlebar(Object targetObj) =>
            !(targetObj is ScriptableObject && targetObj is not StateMachineBehaviour);

        /// <summary>The titlebar's background, icon, enabled toggle, header items, title and settings
        /// button (the foldout and its mouse and keys are the callers').</summary>
        internal static void DoInspectorTitlebar(Rect position, int id, bool foldout, Object[] targetObjs, SerializedProperty enabledProperty, GUIStyle baseStyle)
        {
            // Outside OnGUI there is no event: Unity's read of it throws (measured).
            RequireGUI();
            GUIStyle textStyle = EditorStyles.inspectorTitlebarText;
            GUIStyle iconButtonStyle = EditorStyles.iconButton;
            Event evt = Event.current;
            bool pressed = GUIUtility.hotControl == id;
            bool hasFocus = GUIUtility.HasKeyFocus(id);
            bool hovered = position.Contains(evt.mousePosition);

            Rect iconRect = GetIconRect(position, baseStyle);
            Rect settingsRect = GetSettingsRect(position, baseStyle, iconButtonStyle);
            Rect textRect = GetTextRect(position, iconRect, settingsRect, baseStyle, textStyle);

            if (evt.type == EventType.Repaint)
                baseStyle.Draw(position, GUIContent.none, hovered, pressed, foldout, hasFocus);

            Component comp = targetObjs[0] as Component;
            if (ShouldDrawOverrideBackground(targetObjs, evt, comp))
                Stubs.Ignored("UnityEditor.EditorGUI.DoInspectorTitlebar added-component background",
                    "the override background and the added-component icon of a component added to a prefab instance are not drawn");

            // Unity's loop: a MonoBehaviour first target answers its own enabled state for all of
            // them (two behaviours, one disabled, show the first one's state, measured).
            int enabled = -1;
            foreach (Object targetObj in targetObjs)
            {
                if (comp is MonoBehaviour mb)
                {
                    enabled = mb.enabled ? 1 : 0;
                }
                else
                {
                    int thisEnabled = EditorUtility.GetObjectEnabled(targetObj);
                    if (enabled == -1)
                    {
                        if (EnableCheckBoxInTitlebar(targetObj))
                            enabled = thisEnabled;
                    }
                    else if (enabled != thisEnabled)
                    {
                        enabled = -2;
                        break;
                    }
                }
            }

            if (enabled != -1)
            {
                Rect toggleRect = iconRect;
                toggleRect.x = iconRect.xMax + kInspTitlebarSpacing;
                Rect combinedIconAndToggleRect = new Rect(iconRect.x, iconRect.y, toggleRect.xMax - iconRect.xMin, iconRect.height);

                if (enabledProperty != null)
                {
                    enabledProperty.serializedObject.Update();
                    PropertyField(toggleRect, enabledProperty, GUIContent.none);
                    enabledProperty.serializedObject.ApplyModifiedProperties();
                }
                else
                {
                    bool enabledState = enabled != 0;
                    showMixedValue = enabled == -2;
                    BeginChangeCheck();

                    Color previousColor = GUI.backgroundColor;
                    bool animated = AnimationMode.IsPropertyAnimated(targetObjs[0], kEnabledPropertyName);
                    if (animated)
                    {
                        Color animatedColor = AnimationMode.animatedPropertyColor;
                        if (AnimationMode.InAnimationRecording())
                            animatedColor = AnimationMode.recordedPropertyColor;
                        else if (AnimationMode.IsPropertyCandidate(targetObjs[0], kEnabledPropertyName))
                            animatedColor = AnimationMode.candidatePropertyColor;
                        animatedColor.a *= GUI.color.a;
                        GUI.backgroundColor = animatedColor;
                    }

                    int toggleId = GUIUtility.GetControlID(s_TitlebarHash, FocusType.Keyboard, position);
                    enabledState = EditorGUIInternal.DoToggleForward(toggleRect, toggleId, enabledState, GUIContent.none, EditorStyles.toggle);

                    if (animated)
                        GUI.backgroundColor = previousColor;

                    if (EndChangeCheck())
                    {
                        Undo.RecordObjects(targetObjs, (enabledState ? "Enable" : "Disable") + " Component" + (targetObjs.Length > 1 ? "s" : ""));
                        foreach (Object targetObj in targetObjs)
                            EditorUtility.SetObjectEnabled(targetObj, enabledState);
                    }

                    showMixedValue = false;
                }

                if (combinedIconAndToggleRect.Contains(Event.current.mousePosition)
                    && ((evt.type == EventType.MouseDown && evt.button == 1) || evt.type == EventType.ContextClick))
                {
                    Stubs.Ignored("UnityEditor.EditorGUI.DoInspectorTitlebar enabled-property menu",
                        "the m_Enabled property's context menu is not shown: Unbound has no editor views");
                    evt.Use();
                }
            }

            Rect headerItemRect = settingsRect;
            headerItemRect.x -= kInspTitlebarIconWidth + kInspTitlebarSpacing;
            headerItemRect = EditorGUIUtility.DrawEditorHeaderItems(headerItemRect, targetObjs, kInspTitlebarSpacing);
            textRect.xMax = headerItemRect.xMin - kInspTitlebarSpacing;

            if (evt.type == EventType.Repaint)
            {
                var icon = AssetPreview.GetMiniThumbnail(targetObjs[0]);
                GUIStyle.none.Draw(iconRect, GUIContent.Temp(icon), iconRect.Contains(Event.current.mousePosition), false, false, false);
            }

            // The settings button works whatever GUI.enabled is (Unity's case 947218).
            var oldGUIEnabledState = GUI.enabled;
            GUI.enabled = true;
            switch (evt.type)
            {
                case EventType.MouseDown:
                    if (settingsRect.Contains(evt.mousePosition))
                    {
                        EditorUtility.DisplayObjectContextMenu(settingsRect, targetObjs, 0);
                        evt.Use();
                    }
                    break;
                case EventType.Repaint:
                    textStyle.Draw(textRect, EditorGUIUtility.TempContent(ObjectNames.GetInspectorTitle(targetObjs[0], targetObjs.Length > 1)), hovered, pressed, foldout, hasFocus);
                    EditorStyles.optionsButtonStyle.Draw(settingsRect, GUIContent.none, id, foldout, settingsRect.Contains(Event.current.mousePosition));
                    break;
            }
            GUI.enabled = oldGUIEnabledState;
        }

        /// <summary>A component added to a prefab instance (Unity draws an override background for it).</summary>
        internal static bool ShouldDrawOverrideBackground(Object[] targetObjs, Event evt, Component comp) =>
            evt.type == EventType.Repaint
            && targetObjs.Length == 1
            && comp != null
            && PrefabUtility.GetCorrespondingObjectFromSource(comp.gameObject) != null
            && PrefabUtility.GetCorrespondingObjectFromSource(comp) == null;

        static bool IsValidForContextMenu(Object target)
        {
            // A really null reference has no menu; a destroyed or missing script still has one.
            if (target is null) return false;
            return target != null || target is MonoBehaviour || target is ScriptableObject;
        }

        /// <summary>A titlebar's mouse and keys: a click on it folds it out or in, the arrow keys
        /// fold it when it has the keyboard focus, a drag from it drags its objects.</summary>
        internal static bool DoObjectMouseInteraction(bool foldout, Rect interactionRect, Object[] targetObjs, int id)
        {
            // Always enabled, whatever the editor's enabled state.
            var enabled = GUI.enabled;
            GUI.enabled = true;
            Event evt = Event.current;

            switch (evt.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (interactionRect.Contains(evt.mousePosition)
                        && evt.button == 0 && !(Application.platform == RuntimePlatform.OSXEditor && evt.control))
                    {
                        GUIUtility.hotControl = id;
                        GUIUtility.keyboardControl = id;
                        var delay = (DragAndDropDelay)GUIUtility.GetStateObject(typeof(DragAndDropDelay), id);
                        delay.mouseDownPosition = evt.mousePosition;
                        evt.Use();
                    }
                    break;

                case EventType.ContextClick:
                    if (interactionRect.Contains(evt.mousePosition) && IsValidForContextMenu(targetObjs[0]))
                    {
                        EditorUtility.DisplayObjectContextMenu(new Rect(evt.mousePosition.x, evt.mousePosition.y, 0, 0), targetObjs, 0);
                        evt.Use();
                    }
                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        evt.Use();
                        if (interactionRect.Contains(evt.mousePosition))
                        {
                            GUI.changed = true;
                            foldout = !foldout;
                        }
                    }
                    break;

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id)
                    {
                        var delay = (DragAndDropDelay)GUIUtility.GetStateObject(typeof(DragAndDropDelay), id);
                        if (delay.CanStartDrag())
                        {
                            GUIUtility.hotControl = 0;
                            DragAndDrop.PrepareStartDrag();
                            DragAndDrop.objectReferences = targetObjs;
                            // The title only labels the Editor's drag, which no view shows in Unbound.
                            DragAndDrop.StartDrag(targetObjs.Length > 1 ? "<Multiple>" : targetObjs[0].name);
                        }
                        evt.Use();
                    }
                    break;

                case EventType.DragUpdated:
                    if (s_DragUpdatedOverID == id)
                    {
                        if (interactionRect.Contains(evt.mousePosition))
                        {
                            if (Time.realtimeSinceStartupAsDouble > s_FoldoutDestTime)
                            {
                                foldout = true;
                                HandleUtility.Repaint();
                            }
                        }
                        else
                        {
                            s_DragUpdatedOverID = 0;
                        }
                    }
                    else if (interactionRect.Contains(evt.mousePosition))
                    {
                        s_DragUpdatedOverID = id;
                        s_FoldoutDestTime = Time.realtimeSinceStartupAsDouble + kFoldoutExpandTimeout;
                    }
                    if (interactionRect.Contains(evt.mousePosition))
                    {
                        DragAndDrop.visualMode = DropOnInspector(targetObjs);
                        Event.current.Use();
                    }
                    break;

                case EventType.DragPerform:
                    if (interactionRect.Contains(evt.mousePosition))
                    {
                        DragAndDrop.visualMode = DropOnInspector(targetObjs);
                        DragAndDrop.AcceptDrag();
                        Event.current.Use();
                    }
                    break;

                case EventType.KeyDown:
                    if (GUIUtility.keyboardControl == id)
                    {
                        if (evt.keyCode == KeyCode.LeftArrow)
                        {
                            foldout = false;
                            evt.Use();
                        }
                        if (evt.keyCode == KeyCode.RightArrow)
                        {
                            foldout = true;
                            evt.Use();
                        }
                    }
                    break;
            }

            GUI.enabled = enabled;
            return foldout;
        }

        /// <summary>A drop onto the titlebar adds the dragged scripts or components to its objects in
        /// the Editor's inspector, which Unbound does not have.</summary>
        static DragAndDropVisualMode DropOnInspector(Object[] targetObjs)
        {
            Stubs.Ignored("UnityEditor.DragAndDrop.DropOnInspectorWindow",
                "a drag over an inspector titlebar adds nothing to its objects: Unbound has no inspector window");
            return DragAndDropVisualMode.None;
        }

        /// <summary>The titlebar's foldout arrow, pressed while the titlebar is the hot control.</summary>
        static void DoObjectFoldoutInternal(bool foldout, Rect renderRect, int id)
        {
            var enabled = GUI.enabled;
            GUI.enabled = true;
            if (Event.current.GetTypeForControl(id) == EventType.Repaint)
            {
                bool isPressed = GUIUtility.hotControl == id;
                EditorStyles.titlebarFoldout.Draw(renderRect, isPressed, isPressed, foldout, false);
            }
            GUI.enabled = enabled;
        }

        internal static bool DoObjectFoldout(bool foldout, Rect interactionRect, Rect renderRect, Object[] targetObjs, int id)
        {
            foldout = DoObjectMouseInteraction(foldout, interactionRect, targetObjs, id);
            DoObjectFoldoutInternal(foldout, renderRect, id);
            return foldout;
        }

        /// <summary>The preset button of a titlebar's header: shown for an editable object a preset
        /// can be made of.</summary>
        static bool DrawPresetButton(Rect rectangle, Object[] targets)
        {
            var target = targets[0];
            if (!target || !new UnityEditor.Presets.PresetType(target).IsValid() || (target.hideFlags & HideFlags.NotEditable) != 0)
                return false;
            if (DropdownButton(rectangle, PresetIcon, FocusType.Passive, EditorStyles.iconButton))
                Stubs.Ignored("UnityEditor.Presets.PresetSelector.ShowSelector", "the preset selector window is not shown: Unbound has no editor windows");
            return true;
        }

        /// <summary>The help button of a titlebar's header, for an object with a reference page.</summary>
        static bool DrawHelpButton(Rect position, Object[] objs)
        {
            var obj = objs[0];
            if (!Help.HasHelpForObject(obj)) return false;
            bool wasEditorDisabled = GUI.enabled;
            GUI.enabled = true;
            if (GUI.Button(position, GUIContent.Temp(HelpIcon.image), EditorStyles.iconButton))
                Stubs.Ignored("UnityEditor.Help.ShowHelpForObject", "the reference page is not opened: Unbound opens no browser");
            GUI.enabled = wasEditorDisabled;
            return true;
        }

        /// <summary>The header items, right to left in their callback order: the preset button, then
        /// the help button.</summary>
        internal static readonly Func<Rect, Object[], bool>[] s_EditorHeaderItems = { DrawPresetButton, DrawHelpButton };
    }

    public sealed partial class EditorGUIUtility
    {
        /// <summary>Draws the titlebar's header items from right to left, each one item width and
        /// spacing left of the one before, and answers the last one's rect.</summary>
        internal static Rect DrawEditorHeaderItems(Rect rectangle, Object[] targetObjs, float spacing = 0)
        {
            if (targetObjs.Length == 0 || (targetObjs.Length == 1 && targetObjs[0].GetType() == typeof(object)))
                return rectangle;
            float spacingToRemove = 0;
            foreach (var item in EditorGUI.s_EditorHeaderItems)
            {
                if (item(rectangle, targetObjs))
                {
                    rectangle.x -= rectangle.width + spacing;
                    spacingToRemove = rectangle.width + spacing;
                }
            }
            // The step after the last item positions nothing.
            rectangle.x += spacingToRemove;
            return rectangle;
        }
    }

    public partial class EditorUtility
    {
        /// <summary>An object's context menu (its component commands): not shown, as Unbound has no
        /// editor views.</summary>
        internal static void DisplayObjectContextMenu(Rect position, Object context, int contextUserData) =>
            DisplayObjectContextMenu(position, new[] { context }, contextUserData);

        internal static void DisplayObjectContextMenu(Rect position, Object[] context, int contextUserData) =>
            Stubs.Ignored("UnityEditor.EditorUtility.DisplayObjectContextMenu", "an object's context menu is not shown: Unbound has no editor views");
    }

    public sealed partial class AssetPreview
    {
        static readonly Dictionary<Type, bool> s_HasIconAttribute = new Dictionary<Type, bool>();

        /// <summary>An object's small icon (Scripting API: a Texture2D is its own thumbnail, an
        /// object with a custom icon has that icon): measured on Unity 6000.0.71f1
        /// (tests/projects/editortitlebar thumbnails, thumbnails/more): a texture, null, a GameObject
        /// with an icon set, a behaviour of a script ("cs Script Icon"), a renderer, a Transform, a
        /// GameObject and a ScriptableObject (their class icons).</summary>
        public static Texture2D GetMiniThumbnail(Object obj)
        {
            if (!Compat.EditorTitlebarMeasured) { Stubs.NotImplemented("UnityEditor.AssetPreview.GetMiniThumbnail"); return null; }
            return MiniThumbnail(obj);
        }

        internal static Texture2D MiniThumbnail(Object obj)
        {
            if (obj == null) return null;
            if (obj is Texture2D texture) return texture;
            if ((obj is GameObject || obj is ScriptableObject) && EditorGUIUtility.GetIconForObject(obj) is Texture2D custom) return custom;
            var type = obj.GetType();
            if (!s_HasIconAttribute.TryGetValue(type, out bool attributed))
                s_HasIconAttribute[type] = attributed = Attribute.IsDefined(type, typeof(IconAttribute), true);
            if (attributed)
                Stubs.Ignored("UnityEditor.AssetPreview.GetMiniThumbnail of an [Icon] class", "an [Icon] attribute's icon is not used: the class's default icon stands for it");
            return MiniTypeThumbnail(type);
        }

        /// <summary>A class's icon: a script's behaviours "cs Script Icon", else the icon of the class
        /// or its nearest engine base class (a script's ScriptableObject "ScriptableObject Icon").
        /// Icons a script's MonoScript is given (packages' scripts among them) are unmeasured.</summary>
        internal static Texture2D MiniTypeThumbnail(Type type)
        {
            if (type == null) return null;
            if (typeof(MonoBehaviour).IsAssignableFrom(type) && type.Assembly != typeof(Object).Assembly)
                return EditorIcons.Find("cs Script Icon");
            return EditorIcons.ClassIcon(type);
        }
    }

    public partial class Editor
    {
        internal SerializedProperty m_EnabledProperty;
        SerializedObject m_EnabledPropertyOwner;

        internal bool alwaysAllowExpansion { get; set; }

        /// <summary>The serialized object of the targets, made without the preview-time errors of
        /// <see cref="serializedObject"/>.</summary>
        internal virtual SerializedObject GetSerializedObjectInternal() =>
            m_SerializedObject ??= m_Targets.Length > 0 && Array.TrueForAll(m_Targets, t => t != null) ? new SerializedObject(m_Targets, m_Context) : null;

        /// <summary>The targets' m_Enabled property (null when they have none).</summary>
        internal SerializedProperty enabledProperty
        {
            get
            {
                var so = GetSerializedObjectInternal();
                if (so == null) return null;
                if (!ReferenceEquals(m_EnabledPropertyOwner, so))
                {
                    m_EnabledPropertyOwner = so;
                    m_EnabledProperty = so.FindProperty("m_Enabled");
                }
                return m_EnabledProperty;
            }
        }

        /// <summary>Whether a foldout can show the editor: some visible property has a height.</summary>
        internal bool CanBeExpandedViaAFoldout()
        {
            if (alwaysAllowExpansion) return true;
            var so = GetSerializedObjectInternal();
            if (so == null) return false;
            so.Update();
            return CanBeExpandedViaAFoldoutWithoutUpdate();
        }

        internal bool CanBeExpandedViaAFoldoutWithoutUpdate()
        {
            if (alwaysAllowExpansion) return true;
            var so = GetSerializedObjectInternal();
            if (so == null) return false;
            SerializedProperty property = so.GetIterator();
            bool analyzePropertyChildren = true;
            while (property.NextVisible(analyzePropertyChildren))
            {
                if (EditorGUI.GetPropertyHeight(property, null, true) > 0) return true;
                analyzePropertyChildren = false;
            }
            return false;
        }
    }

    public sealed partial class EditorGUILayout
    {
        public static bool InspectorTitlebar(bool foldout, Object targetObj) => InspectorTitlebar(foldout, targetObj, true);

        public static bool InspectorTitlebar(bool foldout, Object targetObj, bool expandable) =>
            EditorGUI.InspectorTitlebar(GUILayoutUtility.GetRect(GUIContent.none, EditorStyles.inspectorTitlebar), foldout, targetObj, expandable);

        public static bool InspectorTitlebar(bool foldout, Object[] targetObjs) => InspectorTitlebar(foldout, targetObjs, true);

        public static bool InspectorTitlebar(bool foldout, Object[] targetObjs, bool expandable) =>
            EditorGUI.InspectorTitlebar(GUILayoutUtility.GetRect(GUIContent.none, EditorStyles.inspectorTitlebar), foldout, targetObjs, expandable);

        public static bool InspectorTitlebar(bool foldout, Editor editor) =>
            EditorGUI.InspectorTitlebar(GUILayoutUtility.GetRect(GUIContent.none, EditorStyles.inspectorTitlebar), foldout, editor);

        public static void InspectorTitlebar(Object[] targetObjs) =>
            EditorGUI.InspectorTitlebar(GUILayoutUtility.GetRect(GUIContent.none, EditorStyles.inspectorTitlebar), targetObjs);
    }

    public partial class Help
    {
        /// <summary>Whether an object has a reference page: every live object but a DefaultAsset has
        /// one, its class's or the scripting manual's MonoBehaviour page (matches the Unity
        /// 6000.0.71f1 Editor with its online documentation, tests/projects/editortitlebar hasHelp,
        /// hasHelp/more). Unmeasured: [HelpURL] classes, a local documentation module.</summary>
        public static bool HasHelpForObject(Object obj)
        {
            if (!Compat.EditorTitlebarMeasured) { Stubs.NotImplemented("UnityEditor.Help.HasHelpForObject"); return false; }
            // Unity's C#: a DefaultAsset (a folder) has no page name (hasHelp/more).
            return obj != null && obj is not DefaultAsset;
        }
    }
}

namespace UnityEditor.Presets
{
    public partial struct PresetType : IEquatable<PresetType>
    {
        // The object's class; null for an object no preset can be made of.
        readonly Type m_Type;

        /// <summary>The preset type of an object: presets are made of components, assets (and
        /// ScriptableObjects, editors included), importers and settings, not of GameObjects
        /// (Unity 6000.0 Manual, Presets; measured: a behaviour, a renderer, a Transform, a
        /// ScriptableObject and an Editor have one, a GameObject none, tests/projects/editortitlebar
        /// presetValid). Unmeasured: assets and importers (valid here).</summary>
        public PresetType(Object o)
        {
            m_Type = null;
            if (!Compat.EditorTitlebarMeasured) { Stubs.NotImplemented("UnityEditor.Presets.PresetType.ctor"); return; }
            // A [NotNull] binding argument: null throws (presetValid/more).
            if (!o) throw ObjectErrors.Argument(o, "target");
            var type = o.GetType();
            // Scripting API: a class excluded with [ExcludeFromPreset] has no valid preset type
            // (measured for a behaviour and a ScriptableObject).
            m_Type = o is GameObject || Attribute.IsDefined(type, typeof(ExcludeFromPresetAttribute), false) ? null : type;
        }

        public bool IsValid() => m_Type != null;

        /// <summary>Preset types of objects of one class are equal.</summary>
        public bool Equals(PresetType other) => m_Type == other.m_Type;

        public override bool Equals(object obj) => obj is PresetType other && Equals(other);

        public override int GetHashCode() => m_Type?.GetHashCode() ?? 0;

        public static bool operator ==(PresetType a, PresetType b) => a.Equals(b);

        public static bool operator !=(PresetType a, PresetType b) => !a.Equals(b);
    }
}
