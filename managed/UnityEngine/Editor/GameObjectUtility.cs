using System;
using UnityEngine;

namespace UnityEditor
{
    public sealed partial class GameObjectUtility
    {
        /// <summary>"Gets the StaticEditorFlags of the GameObject specified" (Scripting API 6000.0):
        /// the flags its scene or prefab file gives it (m_StaticEditorFlags).</summary>
        public static StaticEditorFlags GetStaticEditorFlags(GameObject go)
        {
            if (go == null) throw new ArgumentNullException(nameof(go));
            return (StaticEditorFlags)go.m_StaticEditorFlags;
        }
    }
}
