namespace UnityEngine.UIElements
{
    /// <summary>
    /// Screen and camera positions in a runtime panel's space: a screen position (from the top left)
    /// through the panel's scale and offset; a world position through the camera's screen point, made
    /// top-left on the camera's display; a world rectangle from its top left corner and its size (y
    /// negated) through the camera's world-to-camera matrix. Panels that are not runtime panels are
    /// refused (a cast exception, null a null reference). Matches the Unity 6000.0.71f1 Editor
    /// (tests/projects/uitkpanelutils).
    /// </summary>
    public static partial class RuntimePanelUtils
    {
        public static Vector2 ScreenToPanel(IPanel panel, Vector2 screenPosition) =>
            ((BaseRuntimePanel)panel).ScreenToPanel(screenPosition);

        public static Vector2 CameraTransformWorldToPanel(IPanel panel, Vector3 worldPosition, Camera camera)
        {
            Vector2 screen = camera.WorldToScreenPoint(worldPosition);
            screen = UIElementsRuntimeUtility.FlipY(screen, UIElementsRuntimeUtility.GetEditorDisplayHeight(camera.targetDisplay));
            return ((BaseRuntimePanel)panel).ScreenToPanel(screen);
        }

        public static Rect CameraTransformWorldToPanelRect(IPanel panel, Vector3 worldPosition, Vector2 worldSize, Camera camera)
        {
            worldSize.y = -worldSize.y;
            var topLeft = CameraTransformWorldToPanel(panel, worldPosition, camera);
            var bottomRight = CameraTransformWorldToPanel(panel, worldPosition + camera.worldToCameraMatrix.MultiplyVector(worldSize), camera);
            return new Rect(topLeft, bottomRight - topLeft);
        }
    }
}
