namespace UnityEngine.Rendering
{
    internal partial class RayTracingShaderHelpURLAttribute : HelpURLAttribute
    {
        public RayTracingShaderHelpURLAttribute() : base(null) { }
        public override string URL => Unbound.Runtime.Compat.RayTracingShaderHelpURL;
    }
}
