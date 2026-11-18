namespace UnityEditor
{
    public sealed partial class UnityStats
    {
        static int s_ScreenWidth = -1, s_ScreenHeight = -1;
        static string s_ScreenRes;

        /// <summary>The size of the Game view the last frame rendered, "WIDTHxHEIGHT": Unbound's Game
        /// view is its screen. A batch-mode Editor, which has no Game view, answers "0x0"
        /// (Unity 6000.0.71f1, tests/projects/gameinterop stats.screen_res_form). Asked every
        /// frame by UI code, so the text is kept while the size stays.</summary>
        public static string screenRes
        {
            get
            {
                int width = UnityEngine.Screen.width, height = UnityEngine.Screen.height;
                if (width != s_ScreenWidth || height != s_ScreenHeight || s_ScreenRes == null)
                {
                    s_ScreenRes = width + "x" + height;
                    s_ScreenWidth = width;
                    s_ScreenHeight = height;
                }
                return s_ScreenRes;
            }
        }
    }
}
