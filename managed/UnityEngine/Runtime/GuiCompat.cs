namespace Unbound.Runtime
{
    internal static class GuiCompat
    {
        /// <summary>Separate TextEditor state and focus behavior measured by attributeinheritance
        /// on 6000.0.71f1. Other releases retain the previous, unmeasured legacy field linkage.</summary>
        internal static bool TextEditorStateMeasured => Compat.Family == ReleaseFamily.Unity6000_0;
    }
}
