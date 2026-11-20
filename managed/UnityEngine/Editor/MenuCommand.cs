namespace UnityEditor
{
    /// <summary>
    /// The command a menu item's method receives: the object it targets and the caller's data. A
    /// command run by its path (EditorApplication.ExecuteMenuItem, Menu.GetEnabled) gets one with
    /// no context and user data 0, also for an object's context menu and with a selection; matches
    /// the Unity 6000.0.71f1 Editor (tests/projects/menucommand).
    /// </summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public sealed partial class MenuCommand
    {
        public UnityEngine.Object context;
        public int userData;

        public MenuCommand(UnityEngine.Object inContext, int inUserData)
        {
            context = inContext;
            userData = inUserData;
        }

        public MenuCommand(UnityEngine.Object inContext)
        {
            context = inContext;
            userData = 0;
        }
    }
}
