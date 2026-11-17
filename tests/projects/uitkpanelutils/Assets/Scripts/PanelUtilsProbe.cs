using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace PanelUtilsProbe
{
    /// <summary>
    /// RuntimePanelUtils on a screen panel (no target texture): ScreenToPanel, CameraTransformWorldToPanel
    /// and CameraTransformWorldToPanelRect for points on and off the screen, in front of and behind a
    /// perspective camera, a turned camera and an orthographic one, under constant pixel size at scales 1
    /// and 2 and under scale with screen size; and what a null panel throws.
    /// </summary>
    public class PanelUtilsProbeBehaviour : MonoBehaviour
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        readonly StringBuilder m_Out = new StringBuilder();
        readonly HashSet<string> m_Keys = new HashSet<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("UITK_PANELUTILS_OUT"))) return;
            var go = new GameObject("probe");
            var document = go.AddComponent<UIDocument>();
            document.panelSettings = Resources.Load<PanelSettings>("ProbePanelSettings");
            go.AddComponent<PanelUtilsProbeBehaviour>();
        }

        IEnumerator Start()
        {
            var dir = Environment.GetEnvironmentVariable("UITK_PANELUTILS_OUT");
            var document = GetComponent<UIDocument>();
            var settings = document.panelSettings;
            var cameraObject = new GameObject("probeCamera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.transform.position = new Vector3(0f, 1f, -10f);
            camera.fieldOfView = 60f;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 100f;
            for (int i = 0; i < 3; i++) yield return null;
            Put("screen", Screen.width + "x" + Screen.height);
            Put("camera.pixelRect", V(camera.pixelRect.position) + " " + V(camera.pixelRect.size));
            var modes = new (string name, Action apply)[]
            {
                ("constant1", () => { settings.scaleMode = PanelScaleMode.ConstantPixelSize; settings.scale = 1f; }),
                ("constant2", () => { settings.scaleMode = PanelScaleMode.ConstantPixelSize; settings.scale = 2f; }),
                ("screenSize", () =>
                {
                    settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                    settings.referenceResolution = new Vector2Int(1200, 800);
                    settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                    settings.match = 0.5f;
                }),
            };
            foreach (var mode in modes)
            {
                mode.apply();
                yield return null;
                yield return null;
                var panel = document.rootVisualElement.panel;
                string p = mode.name + ".";
                Put(p + "root", V(document.rootVisualElement.layout.size));
                foreach (var s in new[] { Vector2.zero, new Vector2(100f, 50f), new Vector2(Screen.width, Screen.height), new Vector2(-30f, 1000f) })
                    Put(p + "screen " + V(s), V(RuntimePanelUtils.ScreenToPanel(panel, s)));
                camera.transform.rotation = Quaternion.identity;
                camera.orthographic = false;
                foreach (var w in new[] { Vector3.zero, new Vector3(2f, 1f, 3f), new Vector3(0f, 0f, -20f) })
                    Put(p + "world " + V(w), V(RuntimePanelUtils.CameraTransformWorldToPanel(panel, w, camera)));
                Put(p + "rect 0,0,0 1x1", R(RuntimePanelUtils.CameraTransformWorldToPanelRect(panel, Vector3.zero, Vector2.one, camera)));
                Put(p + "rect 2,1,3 2x0.5", R(RuntimePanelUtils.CameraTransformWorldToPanelRect(panel, new Vector3(2f, 1f, 3f), new Vector2(2f, 0.5f), camera)));
                camera.transform.rotation = Quaternion.Euler(10f, 30f, 0f);
                Put(p + "turned world 2,1,3", V(RuntimePanelUtils.CameraTransformWorldToPanel(panel, new Vector3(2f, 1f, 3f), camera)));
                Put(p + "turned rect 2,1,3 2x0.5", R(RuntimePanelUtils.CameraTransformWorldToPanelRect(panel, new Vector3(2f, 1f, 3f), new Vector2(2f, 0.5f), camera)));
                camera.transform.rotation = Quaternion.identity;
                camera.orthographic = true;
                camera.orthographicSize = 5f;
                Put(p + "ortho rect 1,2,0 3x2", R(RuntimePanelUtils.CameraTransformWorldToPanelRect(panel, new Vector3(1f, 2f, 0f), new Vector2(3f, 2f), camera)));
                camera.orthographic = false;
            }
            try
            {
                RuntimePanelUtils.ScreenToPanel(null, Vector2.zero);
                Put("nullPanel", "no exception");
            }
            catch (Exception e)
            {
                Put("nullPanel", e.GetType().Name);
            }
            Put("complete", true);
            File.WriteAllText(Path.Combine(dir, "values.jsonl"), m_Out.ToString());
            EditorApplication.Exit(0);
        }

        static string F(float f) => f.ToString("0.###", Inv);
        static string V(Vector2 v) => F(v.x) + "," + F(v.y);
        static string V(Vector3 v) => F(v.x) + "," + F(v.y) + "," + F(v.z);
        static string R(Rect r) => V(r.position) + " " + V(r.size);

        void Put(string key, object value)
        {
            if (!m_Keys.Add(key)) throw new InvalidOperationException("duplicate key " + key);
            m_Out.Append("{\"k\":").Append(Json(key)).Append(",\"v\":").Append(value is bool b ? (b ? "true" : "false") : Json(Convert.ToString(value, Inv))).Append("}\n");
        }

        static string Json(string text)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in text)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", Inv));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }
    }
}
