using System;
using System.Collections.Generic;
using UnityEngine;

// Callbacks that package editor code registers when it loads, for Editor features Unbound has no
// counterpart for (inspectors, package imports, player builds, the main toolbar). They are kept
// and never called, as in a Play mode session without those features (measured:
// tests/projects/editorload subscribe.*, register.*, later.events).
namespace UnityEditor
{
    public partial class Editor
    {
        [Unbound.EditorFaithful("raised when an Inspector draws an editor's header; Unbound shows no Inspector (measured: tests/projects/editorload later.events)")]
        public static event Action<Editor> finishedDefaultHeaderGUI;
    }

    public sealed partial class AssetDatabase
    {
        [Unbound.EditorFaithful("raised by a package import (AssetDatabase.ImportPackage, which Unbound does not implement and warns) (measured: tests/projects/editorload later.events)")]
        public static event ImportPackageCallback importPackageStarted;

        [Unbound.EditorFaithful("raised by a package import (AssetDatabase.ImportPackage, which Unbound does not implement and warns) (measured: tests/projects/editorload later.events)")]
        public static event ImportPackageCallback importPackageCompleted;

        [Unbound.EditorFaithful("raised by a package import (AssetDatabase.ImportPackage, which Unbound does not implement and warns) (measured: tests/projects/editorload later.events)")]
        public static event ImportPackageCallback importPackageCancelled;

        [Unbound.EditorFaithful("raised by a package import (AssetDatabase.ImportPackage, which Unbound does not implement and warns) (measured: tests/projects/editorload later.events)")]
        public static event ImportPackageFailedCallback importPackageFailed;

        public unsafe delegate void ImportPackageCallback(string packageName);
        public unsafe delegate void ImportPackageFailedCallback(string packageName, string errorMessage);
    }

    public sealed partial class PrefabUtility
    {
        [Unbound.EditorFaithful("raised by PrefabUtility.RevertPrefabInstance, which Unbound does not implement and warns (measured: tests/projects/editorload later.events)")]
        public static event Action<GameObject> prefabInstanceReverted;
    }

    public sealed partial class PluginImporter
    {
        IncludeInBuildDelegate m_IncludeInBuildDelegate;

        /// <summary>Decides whether the plugin goes into a build instead of the importer's settings.</summary>
        public void SetIncludeInBuildDelegate(IncludeInBuildDelegate includeInBuildDelegate) => m_IncludeInBuildDelegate = includeInBuildDelegate;

        public unsafe delegate bool IncludeInBuildDelegate(string path);
    }

    /// <summary>A part of the main toolbar that a package adds; Unbound draws no toolbar.</summary>
    internal partial class SubToolbar
    {
        public SubToolbar() { }

        public float Width { get; set; }

        [Unbound.EditorFaithful("the main toolbar draws its parts; Unbound draws no toolbar")]
        public virtual void OnGUI(Rect rect) { }
    }

    internal partial class Toolbar
    {
        static readonly List<SubToolbar> s_SubToolbars = new List<SubToolbar>();

        internal static void AddSubToolbar(SubToolbar subToolbar) => s_SubToolbars.Add(subToolbar);

        [Unbound.EditorFaithful("repaints the main toolbar; Unbound draws no toolbar")]
        internal static void RepaintToolbar() { }
    }
}

namespace UnityEditor.Analytics
{
    public static partial class AnalyticsSettings
    {
        [Unbound.EditorFaithful("raised while a player is built; Unbound builds none (measured: tests/projects/editorload later.events)")]
        public static event RequireInBuildDelegate OnRequireInBuildHandler;

        public unsafe delegate bool RequireInBuildDelegate();
    }
}

namespace UnityEditor.Purchasing
{
    public static partial class PurchasingSettings
    {
        [Unbound.EditorFaithful("sets whether the in-app purchasing plugins go into player builds; Unbound builds none")]
        internal static void ApplyEnableSettings(BuildTarget target) { }
    }
}
