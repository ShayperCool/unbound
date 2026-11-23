using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace EditorInternalsProbe
{
    public class ExpandedBehaviour : MonoBehaviour { }

    public class ExpandedAsset : ScriptableObject { }

    public class OtherBehaviour : MonoBehaviour { }

    public class OtherAsset : ScriptableObject { }

    /// <summary>
    /// InternalEditorUtility members that editor scripts use: whether a script reload is requested,
    /// repainting, the Inspector's expanded state of objects, and the layer masks of the layer
    /// popups (bit n of a concatenated mask is the n-th named layer).
    /// </summary>
    public class EditorInternalsBehaviour : MonoBehaviour
    {
        const string Variable = "EDITOR_INTERNALS_OUT";
        readonly StringBuilder m_Out = new StringBuilder();
        readonly List<string> m_Logs = new List<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable))) return;
            new GameObject("probe").AddComponent<EditorInternalsBehaviour>();
        }

        void Put(string key, string value) => m_Out.Append("{\"k\":").Append(Json(key)).Append(",\"v\":").Append(Json(value)).Append("}\n");

        static string Json(string text)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in text ?? "<null>")
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        static string Try(Func<object> f)
        {
            try { return Convert.ToString(f(), System.Globalization.CultureInfo.InvariantCulture); }
            catch (TargetInvocationException e) { return e.InnerException.GetType().Name; }
            catch (Exception e) { return e.GetType().Name; }
        }

        void Start()
        {
            Application.logMessageReceived += (condition, stack, type) => m_Logs.Add(type + ": " + condition);
            string output = Environment.GetEnvironmentVariable(Variable);

            var reload = typeof(InternalEditorUtility).GetMethod("IsScriptReloadRequested", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Put("reload.requested", Try(() => reload.Invoke(null, null)));
            Put("repaint", Try(() => { InternalEditorUtility.RepaintAllViews(); InternalEditorUtility.RepaintAllViews(); return "ok"; }));

            // The Inspector's expanded state.
            var go = new GameObject("Expanded");
            var go2 = new GameObject("Expanded2");
            var material = new Material(Shader.Find("Sprites/Default"));
            var material2 = new Material(Shader.Find("Sprites/Default"));
            var asset = ScriptableObject.CreateInstance<ExpandedAsset>();
            var asset2 = ScriptableObject.CreateInstance<ExpandedAsset>();
            var objects = new (string, UnityEngine.Object)[]
            {
                ("gameObject", go), ("transform", go.transform), ("camera", go.AddComponent<Camera>()), ("light", go.AddComponent<Light>()),
                ("behaviour", go.AddComponent<ExpandedBehaviour>()), ("material", material), ("asset", asset), ("texture", new Texture2D(2, 2)),
            };
            Put("expanded.defaults", string.Join("|", objects.Select(o => o.Item1 + "=" + Try(() => InternalEditorUtility.GetIsInspectorExpanded(o.Item2)))));
            Put("expanded.null", Try(() => InternalEditorUtility.GetIsInspectorExpanded(null)) + "|set=" + Try(() => { InternalEditorUtility.SetIsInspectorExpanded(null, false); return "ok"; }) + "|after=" + Try(() => InternalEditorUtility.GetIsInspectorExpanded(null)));
            // Collapsing one object: itself, another of its type, an object of another type.
            foreach (var (name, obj) in objects) InternalEditorUtility.SetIsInspectorExpanded(obj, false);
            var others = new (string, UnityEngine.Object)[]
            {
                ("gameObject2", go2), ("transform2", go2.transform), ("camera2", go2.AddComponent<Camera>()), ("behaviour2", go2.AddComponent<ExpandedBehaviour>()),
                ("material2", material2), ("asset2", asset2), ("otherType", go2.AddComponent<LineRenderer>()),
            };
            Put("expanded.collapsed", string.Join("|", objects.Select(o => o.Item1 + "=" + Try(() => InternalEditorUtility.GetIsInspectorExpanded(o.Item2)))));
            Put("expanded.collapsed.others", string.Join("|", others.Select(o => o.Item1 + "=" + Try(() => InternalEditorUtility.GetIsInspectorExpanded(o.Item2)))));
            InternalEditorUtility.SetIsInspectorExpanded(go2.transform, true);
            InternalEditorUtility.SetIsInspectorExpanded(material2, true);
            InternalEditorUtility.SetIsInspectorExpanded(asset2, true);
            Put("expanded.again", string.Join("|", new (string, UnityEngine.Object)[] { ("transform", go.transform), ("transform2", go2.transform), ("material", material), ("material2", material2), ("asset", asset), ("asset2", asset2), ("camera", objects[2].Item2) }
                .Select(o => o.Item1 + "=" + Try(() => InternalEditorUtility.GetIsInspectorExpanded(o.Item2)))));
            // Scripts: whether another script's objects follow one script's state. At this point
            // ExpandedBehaviour is collapsed and ExpandedAsset expanded again.
            var otherBehaviour = go2.AddComponent<OtherBehaviour>();
            var otherAsset = ScriptableObject.CreateInstance<OtherAsset>();
            Func<string> scripts = () => "behaviour=" + Try(() => InternalEditorUtility.GetIsInspectorExpanded(objects[4].Item2)) + "|otherBehaviour=" + Try(() => InternalEditorUtility.GetIsInspectorExpanded(otherBehaviour))
                + "|asset=" + Try(() => InternalEditorUtility.GetIsInspectorExpanded(asset)) + "|otherAsset=" + Try(() => InternalEditorUtility.GetIsInspectorExpanded(otherAsset));
            Put("expanded.scripts", scripts());
            InternalEditorUtility.SetIsInspectorExpanded(otherBehaviour, true);
            Put("expanded.scripts.otherBehaviourExpanded", scripts());
            InternalEditorUtility.SetIsInspectorExpanded(otherAsset, false);
            Put("expanded.scripts.otherAssetCollapsed", scripts());
            InternalEditorUtility.SetIsInspectorExpanded(objects[4].Item2, true);
            InternalEditorUtility.SetIsInspectorExpanded(otherBehaviour, false);
            Put("expanded.scripts.swapped", scripts());

            // A new object made after its type was collapsed, and a destroyed one.
            var go3 = new GameObject("Expanded3");
            Put("expanded.newAfter", "gameObject=" + Try(() => InternalEditorUtility.GetIsInspectorExpanded(go3)) + "|transform=" + Try(() => InternalEditorUtility.GetIsInspectorExpanded(go3.transform)) + "|camera=" + Try(() => InternalEditorUtility.GetIsInspectorExpanded(go3.AddComponent<Camera>())) + "|asset=" + Try(() => InternalEditorUtility.GetIsInspectorExpanded(ScriptableObject.CreateInstance<ExpandedAsset>())));
            DestroyImmediate(material2);
            Put("expanded.destroyed", Try(() => InternalEditorUtility.GetIsInspectorExpanded(material2)) + "|set=" + Try(() => { InternalEditorUtility.SetIsInspectorExpanded(material2, false); return "ok"; }));

            // Layer masks: bit n of a concatenated mask is the n-th named layer.
            Put("layers.named", string.Join(",", InternalEditorUtility.layers));
            int[] concatenated = { 0, 1, 2, 4, 8, 16, 31, 32, 64, -1, int.MinValue, 5 };
            Put("layers.fromConcatenated", string.Join("|", concatenated.Select(m => m + ">" + Try(() => InternalEditorUtility.ConcatenatedLayersMaskToLayerMask(m).value))));
            int[] masks = { 0, 1, 2, 4, 8, 16, 32, 55, 64, 1 << 31, -1, 17 };
            Put("layers.toConcatenated", string.Join("|", masks.Select(m => m + ">" + Try(() => InternalEditorUtility.LayerMaskToConcatenatedLayersMask(m)))));

            // The release's version and build answers.
            Put("version.full", Try(() => InternalEditorUtility.GetFullUnityVersion()));
            Put("version.digits", Try(() => InternalEditorUtility.GetUnityVersionDigits()));
            Put("version.display", Try(() => InternalEditorUtility.GetUnityDisplayVersion()));
            Put("version.displayVerbose", Try(() => InternalEditorUtility.GetUnityDisplayVersionVerbose()));
            Put("version.branch", Try(() => InternalEditorUtility.GetUnityBuildBranch()));
            Put("version.hash", Try(() => InternalEditorUtility.GetUnityBuildHash()));
            Put("version.revision", Try(() => InternalEditorUtility.GetUnityRevision()));
            Put("version.date", Try(() => InternalEditorUtility.GetUnityVersionDate()));
            Put("version.productName", Try(() => InternalEditorUtility.GetUnityProductName()));
            Put("version.copyright", Try(() => InternalEditorUtility.GetUnityCopyright()));
            Put("version.applicationUnityVersion", Application.unityVersion);
            Put("logs", string.Join(" | ", m_Logs));
            m_Out.Append("{\"k\":\"complete\",\"v\":true}\n");
            File.WriteAllText(Path.Combine(output, "values.jsonl"), m_Out.ToString());
            EditorApplication.Exit(0);
        }
    }
}
