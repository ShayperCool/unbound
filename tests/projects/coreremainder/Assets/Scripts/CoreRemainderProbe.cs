using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CoreRemainderProbe
{
    /// <summary>
    /// Application.onBeforeRender with BeforeRenderOrderAttribute: callbacks of several orders, ties,
    /// one delegate twice, removals, a throwing callback, callbacks registered while the event runs,
    /// null (run through the helper the engine invokes before rendering, as batch mode renders
    /// nothing); UnityString.Format; UnityAPICompatibilityVersionAttribute;
    /// QualitySettings.GetQualitySettings and GraphicsSettings.GetGraphicsSettings.
    /// </summary>
    public class CoreRemainderBehaviour : MonoBehaviour
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        readonly StringBuilder m_Out = new StringBuilder();
        readonly HashSet<string> m_Keys = new HashSet<string>();
        static readonly List<string> s_Calls = new List<string>();
        static readonly List<string> s_Logs = new List<string>();
        static MethodInfo s_Invoke;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CORE_REMAINDER_OUT"))) return;
            new GameObject("probe").AddComponent<CoreRemainderBehaviour>();
        }

        [BeforeRenderOrder(10)] static void A10() => s_Calls.Add("A10");
        [BeforeRenderOrder(10)] static void D10() => s_Calls.Add("D10");
        [BeforeRenderOrder(-5)] static void Cm5() => s_Calls.Add("C-5");
        [BeforeRenderOrder(-100)] void Mm100() => s_Calls.Add("M-100");
        static void Plain() => s_Calls.Add("B");
        [BeforeRenderOrder(1)] static void Thrower()
        {
            s_Calls.Add("T1");
            throw new InvalidOperationException("probe throw");
        }
        [BeforeRenderOrder(20)] static void N20() => s_Calls.Add("N20");
        [BeforeRenderOrder(-50)] static void Pm50() => s_Calls.Add("P-50");
        static int s_Reentries;
        [BeforeRenderOrder(3)] static void Registrar()
        {
            s_Calls.Add("R3");
            if (s_Reentries++ > 0) return;
            Application.onBeforeRender += N20;
            Application.onBeforeRender += Pm50;
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

        static Type FindType(string name) =>
            AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name, false)).FirstOrDefault(t => t != null);

        /// <summary>Runs the event as the engine does before rendering, and returns the calls.</summary>
        static string Invoke()
        {
            s_Calls.Clear();
            s_Logs.Clear();
            string error = null;
            try { s_Invoke.Invoke(null, null); }
            catch (TargetInvocationException e) { error = e.InnerException.GetType().Name + ": " + e.InnerException.Message; }
            var text = string.Join(" ", s_Calls);
            s_Calls.Clear();
            return error == null ? text : text + " | threw " + error;
        }

        static string Try(Action action)
        {
            try { action(); return "ok"; }
            catch (Exception e) { return e.GetType().Name + ": " + e.Message; }
        }

        IEnumerator Start()
        {
            var dir = Environment.GetEnvironmentVariable("CORE_REMAINDER_OUT");
            Application.logMessageReceived += (condition, stack, type) => s_Logs.Add(type + ": " + condition);
            s_Invoke = FindType("UnityEngine.BeforeRenderHelper").GetMethod("Invoke", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            UnityEngine.Events.UnityAction plain = Plain;
            yield return null;
            Application.onBeforeRender += A10;
            Application.onBeforeRender += plain;
            Application.onBeforeRender += Cm5;
            Application.onBeforeRender += D10;
            Application.onBeforeRender += plain;
            Application.onBeforeRender += Mm100;
            Put("invoke.registered", Invoke());
            Application.onBeforeRender -= A10;
            Application.onBeforeRender -= plain;
            Put("invoke.removedA10AndOnePlain", Invoke());
            Application.onBeforeRender -= D10;
            Application.onBeforeRender += A10;
            Application.onBeforeRender += A10;
            Put("invoke.order10EmptiedThenA10Twice", Invoke());
            Application.onBeforeRender -= A10;
            Put("invoke.oneA10Removed", Invoke());
            Application.onBeforeRender -= N20;
            Put("invoke.unregisteredRemoved", Invoke());
            Application.onBeforeRender += Thrower;
            Put("invoke.throwing", Invoke());
            Application.onBeforeRender -= Thrower;
            Application.onBeforeRender += Registrar;
            Put("invoke.registeringDuring", Invoke());
            Put("invoke.afterRegistering", Invoke());
            Application.onBeforeRender -= Registrar;
            Application.onBeforeRender -= N20;
            Application.onBeforeRender -= Pm50;
            Application.onBeforeRender -= A10;
            Application.onBeforeRender -= plain;
            Application.onBeforeRender -= Cm5;
            Application.onBeforeRender -= Mm100;
            Put("invoke.allRemoved", Invoke());
            Put("event.addNull", Try(() => Application.onBeforeRender += null));
            Put("event.removeNull", Try(() => Application.onBeforeRender -= null));
            Put("event.addLambda", Try(() => Application.onBeforeRender += () => s_Calls.Add("L")));
            Put("invoke.lambda", Invoke());
            Put("attribute.order", new BeforeRenderOrderAttribute(7).order);

            // UnityString.Format: the invariant culture, Mono's number texts.
            var format = FindType("UnityEngine.UnityString").GetMethod("Format", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Put("unityString.numbers", format.Invoke(null, new object[] { "{0} {1:F2} {2} {3} {4:N1}", new object[] { 1.1f, 2.005, 1849.9999f, 0.1, 1234567.25 } }));
            Put("unityString.nullArgs", Try(() =>
            {
                try { format.Invoke(null, new object[] { "{0}", null }); }
                catch (TargetInvocationException e) { throw e.InnerException; }
            }));

            // UnityAPICompatibilityVersionAttribute.
            // The one-string constructor is obsolete as an error; reached by reflection.
            var versionOnly = (UnityAPICompatibilityVersionAttribute)typeof(UnityAPICompatibilityVersionAttribute)
                .GetConstructor(new[] { typeof(string) }).Invoke(new object[] { "2019.4.1f1" });
            Put("compat.versionOnly", versionOnly.version);
            Put("compat.checkOnlyTrue", new UnityAPICompatibilityVersionAttribute("6000.0.71f1", true).version);
            Put("compat.checkOnlyFalse", Try(() => new UnityAPICompatibilityVersionAttribute("6000.0.71f1", false)));
            Put("compat.hashes", new UnityAPICompatibilityVersionAttribute("1.2", new[] { "a", "b" }).version);

            // The settings objects.
            var quality = QualitySettings.GetQualitySettings();
            Put("quality.object", quality == null ? "null" : quality.name + " " + quality.GetType().FullName);
            Put("quality.sameAgain", quality == QualitySettings.GetQualitySettings());
            Put("quality.sameAsSingleton", quality == Unsupported.GetSerializedAssetInterfaceSingleton("QualitySettings"));
            Put("quality.sameAsAsset", quality == AssetDatabase.LoadMainAssetAtPath("ProjectSettings/QualitySettings.asset"));
            var so = new SerializedObject(quality);
            var levels = so.FindProperty("m_QualitySettings");
            Put("quality.levels", levels.arraySize + " first " + levels.GetArrayElementAtIndex(0).FindPropertyRelative("name").stringValue);
            Put("quality.current", so.FindProperty("m_CurrentQuality").intValue);
            var graphics = GraphicsSettings.GetGraphicsSettings();
            Put("graphics.object", graphics == null ? "null" : graphics.name + " " + graphics.GetType().FullName);
            Put("graphics.sameAsSingleton", graphics == Unsupported.GetSerializedAssetInterfaceSingleton("GraphicsSettings"));
            Put("graphics.alwaysIncluded", new SerializedObject(graphics).FindProperty("m_AlwaysIncludedShaders").arraySize);

            Put("complete", true);
            File.WriteAllText(Path.Combine(dir, "values.jsonl"), m_Out.ToString());
            EditorApplication.Exit(0);
        }
    }
}
