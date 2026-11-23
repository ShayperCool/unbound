using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace InitialPointerProbe
{
    /// <summary>
    /// The Input System mouse at the start of Play mode, the system pointer not moved: in each of
    /// the first frames whether a mouse exists, whether its position is still (0, 0), its delta,
    /// and whether the position stays as the frame before (the pointer's place itself depends on
    /// where the window is, so it is not recorded).
    /// </summary>
    public class InitialPointerProbeBehaviour : MonoBehaviour
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        readonly StringBuilder m_Out = new StringBuilder();
        readonly HashSet<string> m_Keys = new HashSet<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("INITIAL_POINTER_OUT"))) return;
            new GameObject("probe").AddComponent<InitialPointerProbeBehaviour>();
        }

        static string F(Vector2 v) => v.x.ToString("0.###", Inv) + "," + v.y.ToString("0.###", Inv);

        IEnumerator Start()
        {
            Vector2? previous = null;
            for (int frame = 1; frame <= 5; frame++)
            {
                var mouse = Mouse.current;
                Put($"frame{frame}.mouse", mouse != null);
                if (mouse != null)
                {
                    var position = mouse.position.ReadValue();
                    Put($"frame{frame}.atOrigin", position == Vector2.zero);
                    Put($"frame{frame}.delta", F(mouse.delta.ReadValue()));
                    if (previous.HasValue) Put($"frame{frame}.unchanged", position == previous.Value);
                    previous = position;
                }
                yield return null;
            }
            Put("complete", true);
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("INITIAL_POINTER_OUT"), "values.jsonl"), m_Out.ToString());
            EditorApplication.Exit(0);
        }

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
