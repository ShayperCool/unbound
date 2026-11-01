using System;
using UnityEngine.SceneManagement;

namespace UnityEditor.Build
{
    /// <summary>An editor callback with an order: lower orders are called first.</summary>
    public partial interface IOrderedCallback
    {
        int callbackOrder { get; }
    }

    /// <summary>Processes each scene loaded in Play mode (honoured by
    /// <c>EditorInitialization.ProcessScene</c>; the report is null there, as in the Editor).</summary>
    public partial interface IProcessSceneWithReport : IOrderedCallback
    {
        void OnProcessScene(Scene scene, Reporting.BuildReport report);
    }

    /// <summary>The obsolete form of <see cref="IProcessSceneWithReport"/>, called alike.</summary>
    [Obsolete("Use IProcessSceneWithReport instead")]
    public partial interface IProcessScene : IOrderedCallback
    {
        void OnProcessScene(Scene scene);
    }
}

namespace UnityEditor.Build.Reporting
{
    /// <summary>A player build's report; Play mode builds none.</summary>
    public sealed partial class BuildReport : UnityEngine.Object
    {
        BuildReport() { }
    }
}
