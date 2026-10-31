using UnityEngine;

namespace UnityEditor
{
    /// <summary>A shader's material inspector (its <c>CustomEditor</c>). Unbound draws no inspector,
    /// but runs <see cref="ValidateMaterial"/> where the Editor does (see
    /// <see cref="Unbound.Runtime.ModelMaterialImport"/>).</summary>
    public abstract partial class ShaderGUI
    {
        protected ShaderGUI() { }

        /// <summary>Brings a material's keywords, render states and flags in line with its
        /// properties.</summary>
        [Unbound.EditorFaithful("Unity's base implementation does nothing")]
        public virtual void ValidateMaterial(Material material) { }
    }
}
