using System;
using System.Collections.Generic;

namespace UnityEngine
{
    /// <summary>
    /// The OnMouse* messages, as Unity's own UnityEngine.SendMouseEvents (InputLegacyModule) sends
    /// them at PreUpdate.SendMouseEvents: each camera (by depth) whose pixel rect holds the mouse
    /// casts a ray, 3D and 2D, through its culling mask and event mask; a hit object (its
    /// rigidbody's, if it has one) gets Enter/Over/Exit, Down/Drag/Up/UpAsButton with the left
    /// button. A camera that hits nothing and clears to a skybox or color hides the hits of the
    /// cameras before it. Render-texture cameras are skipped, as in the Editor (measured:
    /// tests/projects/messages). With only the Input System active the mouse reads as at (0, 0),
    /// unpressed.
    /// </summary>
    internal partial class SendMouseEvents
    {
        struct HitInfo
        {
            public GameObject target;
            public Camera camera;

            public void SendMessage(string name) => target.SendMessage(name, null, SendMessageOptions.DontRequireReceiver);

            public static implicit operator bool(HitInfo exists) => exists.target != null && exists.camera != null;

            public static bool Compare(HitInfo lhs, HitInfo rhs) => lhs.target == rhs.target && lhs.camera == rhs.camera;
        }

        public enum LeftMouseButtonState
        {
            NotPressed,
            Pressed,
            PressedThisFrame,
        }

        static bool s_MouseUsed;
        static readonly HitInfo[] m_LastHit = new HitInfo[3];
        static readonly HitInfo[] m_MouseDownHit = new HitInfo[3];
        static readonly HitInfo[] m_CurrentHit = new HitInfo[3];
        static Camera[] m_Cameras;

        public static Func<KeyValuePair<int, Vector2>> s_GetMouseState;

        static Vector2 s_MousePosition;
        static bool s_MouseButtonPressedThisFrame;
        static bool s_MouseButtonIsPressed;

        static void UpdateMouse()
        {
            if (s_GetMouseState != null)
            {
                var state = s_GetMouseState();
                s_MousePosition = state.Value;
                s_MouseButtonPressedThisFrame = state.Key == 2;
                s_MouseButtonIsPressed = state.Key != 0;
            }
            else if (!Input.CheckDisabled())
            {
                s_MousePosition = Input.mousePosition;
                s_MouseButtonPressedThisFrame = Input.GetMouseButtonDown(0);
                s_MouseButtonIsPressed = Input.GetMouseButton(0);
            }
            else
            {
                s_MousePosition = default;
                s_MouseButtonPressedThisFrame = false;
                s_MouseButtonIsPressed = false;
            }
        }

        static void SetMouseMoved() => s_MouseUsed = true;

        /// <summary>Play mode starts with no hits remembered.</summary>
        internal static void Reset()
        {
            for (int i = 0; i < m_LastHit.Length; i++)
                m_LastHit[i] = m_MouseDownHit[i] = m_CurrentHit[i] = default;
            s_MouseUsed = false;
        }

        internal static void DoSendMouseEvents(int skipRTCameras)
        {
            UpdateMouse();
            Vector2 mousePosition = s_MousePosition;
            int count = Camera.allCamerasCount;
            if (m_Cameras == null || m_Cameras.Length != count) m_Cameras = new Camera[count];
            Camera.GetAllCameras(m_Cameras);
            for (int i = 0; i < m_CurrentHit.Length; i++) m_CurrentHit[i] = default;
            if (!s_MouseUsed)
            {
                foreach (var camera in m_Cameras)
                {
                    if (camera == null || (skipRTCameras != 0 && camera.targetTexture != null)) continue;
                    int targetDisplay = camera.targetDisplay;
                    Vector3 position = Display.RelativeMouseAt(mousePosition);
                    if (position != Vector3.zero)
                    {
                        if ((int)position.z != targetDisplay) continue;
                        float width = Screen.width;
                        float height = Screen.height;
                        if (targetDisplay > 0 && targetDisplay < Display.displays.Length)
                        {
                            width = Display.displays[targetDisplay].systemWidth;
                            height = Display.displays[targetDisplay].systemHeight;
                        }
                        var normalized = new Vector2(position.x / width, position.y / height);
                        if (normalized.x < 0f || normalized.x > 1f || normalized.y < 0f || normalized.y > 1f) continue;
                    }
                    else
                    {
                        position = mousePosition;
                        if (Display.activeEditorGameViewTarget != targetDisplay) continue;
                        position.z = Display.activeEditorGameViewTarget;
                    }
                    if (!camera.pixelRect.Contains(position) || camera.eventMask == 0) continue;
                    var ray = camera.ScreenPointToRay(position);
                    float z = ray.direction.z;
                    float distance = Mathf.Approximately(0f, z) ? float.PositiveInfinity : Mathf.Abs((camera.farClipPlane - camera.nearClipPlane) / z);
                    bool clears = camera.clearFlags == CameraClearFlags.Skybox || camera.clearFlags == CameraClearFlags.SolidColor;
                    var hit3D = CameraRaycastHelper.RaycastTry(camera, ray, distance, camera.cullingMask & camera.eventMask);
                    if (hit3D != null)
                    {
                        m_CurrentHit[1].target = hit3D;
                        m_CurrentHit[1].camera = camera;
                    }
                    else if (clears)
                    {
                        m_CurrentHit[1].target = null;
                        m_CurrentHit[1].camera = null;
                    }
                    var hit2D = CameraRaycastHelper.RaycastTry2D(camera, ray, distance, camera.cullingMask & camera.eventMask);
                    if (hit2D != null)
                    {
                        m_CurrentHit[2].target = hit2D;
                        m_CurrentHit[2].camera = camera;
                    }
                    else if (clears)
                    {
                        m_CurrentHit[2].target = null;
                        m_CurrentHit[2].camera = null;
                    }
                }
            }
            for (int i = 0; i < m_CurrentHit.Length; i++) SendEvents(i, m_CurrentHit[i]);
            s_MouseUsed = false;
        }

        static void SendEvents(int i, HitInfo hit)
        {
            bool pressedThisFrame = s_MouseButtonPressedThisFrame;
            bool pressed = s_MouseButtonIsPressed;
            if (pressedThisFrame)
            {
                if (hit)
                {
                    m_MouseDownHit[i] = hit;
                    m_MouseDownHit[i].SendMessage("OnMouseDown");
                }
            }
            else if (!pressed)
            {
                if (m_MouseDownHit[i])
                {
                    if (HitInfo.Compare(hit, m_MouseDownHit[i])) m_MouseDownHit[i].SendMessage("OnMouseUpAsButton");
                    m_MouseDownHit[i].SendMessage("OnMouseUp");
                    m_MouseDownHit[i] = default;
                }
            }
            else if (m_MouseDownHit[i])
            {
                m_MouseDownHit[i].SendMessage("OnMouseDrag");
            }
            if (HitInfo.Compare(hit, m_LastHit[i]))
            {
                if (hit) hit.SendMessage("OnMouseOver");
            }
            else
            {
                if (m_LastHit[i]) m_LastHit[i].SendMessage("OnMouseExit");
                if (hit)
                {
                    hit.SendMessage("OnMouseEnter");
                    hit.SendMessage("OnMouseOver");
                }
            }
            m_LastHit[i] = hit;
        }
    }

    /// <summary>The rays SendMouseEvents casts (native in Unity): the first collider hit through
    /// the layers (never Ignore Raycast), triggers as <c>queriesHitTriggers</c> says; the object
    /// is the hit body's when the collider is attached to one (measured: tests/projects/messages).</summary>
    internal partial class CameraRaycastHelper
    {
        const int IgnoreRaycastLayer = 1 << 2;

        internal static GameObject RaycastTry(Camera cam, Ray ray, float distance, int layerMask)
        {
            if (!Physics.Raycast(ray, out var hit, distance, layerMask & ~IgnoreRaycastLayer, QueryTriggerInteraction.UseGlobal)) return null;
            var body = hit.rigidbody;
            return body != null ? body.gameObject : hit.collider.gameObject;
        }

        internal static GameObject RaycastTry2D(Camera cam, Ray ray, float distance, int layerMask)
        {
            var hit = Physics2D.GetRayIntersection(ray, distance, layerMask & ~IgnoreRaycastLayer);
            if (hit.collider == null) return null;
            var body = hit.rigidbody;
            return body != null ? body.gameObject : hit.collider.gameObject;
        }
    }
}
