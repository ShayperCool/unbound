namespace Probe.Setup.Editor
{
    public static class EditorOnlyApi
    {
        public static string Value() => "editor-only package assembly";

        /// <summary>CodeDom, which Editor-only assemblies compile against.</summary>
        public static string CodeDomExtension() => new Microsoft.CSharp.CSharpCodeProvider().FileExtension;

        public static string CodeDomType() => new System.CodeDom.CodeTypeReference(typeof(int)).BaseType;
    }
}
