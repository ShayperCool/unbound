namespace Unbound.Runtime
{
    internal static class GuiCompat
    {
        /// <summary>Separate TextEditor state and focus behavior measured by attributeinheritance
        /// on 6000.0.71f1. Other releases retain the previous, unmeasured legacy field linkage.</summary>
        internal static bool TextEditorStateMeasured => Compat.Release == (6000, 0, 71, 'f', 1);
    }
}
