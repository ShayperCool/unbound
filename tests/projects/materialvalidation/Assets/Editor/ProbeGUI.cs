using UnityEditor;
using UnityEngine;

/// <summary>A project shader editor named without a namespace: each validation turns
/// <c>_PROBE_VALIDATED</c> on and adds 1 to <c>_Value</c> (so the value counts validations).</summary>
public class ProbeGUI : ShaderGUI
{
    public override void ValidateMaterial(Material material)
    {
        material.EnableKeyword("_PROBE_VALIDATED");
        material.SetFloat("_Value", material.GetFloat("_Value") + 1);
    }
}

namespace Probe.Validation
{
    /// <summary>The same, named by its full name; adds 10 to <c>_Value</c>.</summary>
    public class NamespacedGUI : ShaderGUI
    {
        public override void ValidateMaterial(Material material)
        {
            material.EnableKeyword("_PROBE_VALIDATED");
            material.SetFloat("_Value", material.GetFloat("_Value") + 10);
        }
    }
}
