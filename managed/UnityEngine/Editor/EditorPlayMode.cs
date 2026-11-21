using System;
using Unbound.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

// These subscriptions stay idle during ordinary Play-mode frames. Operations that would
// publish their payloads retain their own unsupported diagnostics until implemented.
#pragma warning disable CS0067
namespace UnityEditor
{
    [AttributeUsage(AttributeTargets.Class)]
    internal partial class UIFrameworkAttribute : Attribute
    {
        public UIFrameworkAttribute(UIFrameworkUsage frameworkUsage) { this.frameworkUsage = frameworkUsage; }
        public UIFrameworkUsage frameworkUsage { get; private set; }
    }
    internal enum UIFrameworkUsage { AutoDetected = 0, IMGUI = 1, UITK = 2, Mixed = 3, Excluded = 4 }

    public sealed partial class PrefabUtility
    {
        public static event Action<GameObject> prefabInstanceReverting;
    }
    public static partial class Lightmapping
    {
        public static event Action bakeStarted;
        public static event Action bakeCompleted;
        public static event Action bakeCancelled;
        public static event Action lightingDataCleared;
        public static event Action lightingDataUpdated;
        public static event Action lightingDataAssetCleared;
    }
    public static partial class ClipboardUtility
    {
        public static event Action<GameObject[]> pastedGameObjects;
        public static event Action<GameObject[]> duplicatedGameObjects;
    }
    public static partial class Unsupported
    {
        [Unbound.EditorFaithful("retail Editor profile, tests/projects/editorapi")]
        public static bool IsDeveloperMode() => false;
        [Unbound.EditorFaithful("retail Editor profile, tests/projects/editorapi")]
        public static bool IsDeveloperBuild() => false;
    }
    [ExcludeFromPreset]
    internal partial class PrefabImporter : AssetImporter { public PrefabImporter() { } }
    public sealed partial class ShaderImporter : AssetImporter { public ShaderImporter() { } }
    internal sealed partial class ShaderIncludeImporter : AssetImporter { public ShaderIncludeImporter() { } }
    internal partial interface IPropertyView { }
    public partial interface IHasCustomMenu { }
    internal partial class PropertyEditor : EditorWindow, IPropertyView, IHasCustomMenu
    {
        internal PropertyEditor()
        {
            if (Compat.DefaultConstructorStateMeasured)
                minSize = new Vector2(Compat.PropertyEditorMinimumWidth, minSize.y);
        }
    }
    [EditorWindowTitle(title = "Inspector", useTypeNameAsIconName = true)]
    internal partial class InspectorWindow : PropertyEditor, IPropertyView, IHasCustomMenu
    {
        // EditorWindow.Repaint has no drawing work while this host runs a game.
        internal static void RepaintAllInspectors() { }
    }
    [UIFramework(UIFrameworkUsage.IMGUI)]
    internal partial class SpriteUtilityWindow : EditorWindow
    {
        static Func<Object, bool> s_ShowSpriteEditor;
        internal static void SetShowSpriteEditorWindowWithObject(Func<Object, bool> spriteEditorWindow)
        {
            if (spriteEditorWindow != null) s_ShowSpriteEditor = spriteEditorWindow;
        }
        internal static bool ShowSpriteEditorWindow(Object obj = null) => s_ShowSpriteEditor?.Invoke(obj) ?? false;
    }
}
namespace UnityEditor.SceneManagement
{
    public sealed partial class PrefabStage
    {
        public static event Action<PrefabStage> prefabStageOpened;
        public static event Action<PrefabStage> prefabStageClosing;
    }
}
namespace UnityEditor.PackageManager
{
    /// <summary>The Package Manager's registration events: raised when a resolution adds, removes
    /// or changes the project's packages (the Scripting API, 6000.0). A session's packages are
    /// resolved once, before its scripts load, so nothing raises them while it runs; handlers
    /// (Burst's and the Input System's, for two) are kept.</summary>
    public static partial class Events
    {
        public static event Action<PackageRegistrationEventArgs> registeringPackages;
        public static event Action<PackageRegistrationEventArgs> registeredPackages;
    }
    public partial class PackageRegistrationEventArgs
    {
        private PackageRegistrationEventArgs() { }
    }
}
namespace UnityEditor.AI
{
    public static partial class NavMeshEditorHelpers
    {
        internal static event Action<int> agentTypeSettingsClicked;
        internal static event Action areaSettingsClicked;
        internal static event Action<int, int> agentRejectedDebugInfoRequestsCountChanged;
        internal static event Action agentDebugRequestsPending;
        internal static event Action agentDebugRequestsProcessed;
    }
}
namespace UnityEditorInternal
{
    [ExcludeFromObjectFactory]
    internal abstract partial class ProjectSettingsBase : Object
    {
        protected ProjectSettingsBase()
        {
            if (Compat.UncreatedObjectWrappersMeasured && !ObjectConstruction.TakeBacking(this)) MarkUncreated();
        }
    }
}
namespace Unity.Profiling.Memory
{
    public static partial class MemoryProfiler { public static event Action<MemorySnapshotMetadata> CreatingMetadata; }
    public partial class MemorySnapshotMetadata { public string Description { get; set; } }
}
#pragma warning restore CS0067
