using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace EngineDiscreteProbe
{
    /// <summary>
    /// Discrete curves of engine components' properties: which of the animatable bindings of a few
    /// components the Editor marks discrete, and the values such a property takes when a clip
    /// made with discrete bindings is played by a playable graph, its keys written as the
    /// integer's bits or as plain numbers.
    /// </summary>
    public class EngineDiscreteProbeBehaviour : MonoBehaviour
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        readonly StringBuilder m_Out = new StringBuilder();
        readonly HashSet<string> m_Keys = new HashSet<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ENGINE_DISCRETE_OUT"))) return;
            new GameObject("probe").AddComponent<EngineDiscreteProbeBehaviour>();
        }

        static float Bits(int value) => BitConverter.Int32BitsToSingle(value);

        /// <summary>The object's components and what each case reads.</summary>
        static readonly (string Name, Type Type, string Property, Func<Component, string> Read)[] Cases =
        {
            ("sortingOrder", typeof(SpriteRenderer), "m_SortingOrder", c => ((Renderer)c).sortingOrder.ToString(Inv)),
            ("lightType", typeof(Light), "m_Type", c => ((Light)c).type.ToString()),
            ("shadows", typeof(Light), "m_Shadows.m_Type", c => ((Light)c).shadows.ToString()),
            ("clearFlags", typeof(Camera), "m_ClearFlags", c => ((Camera)c).clearFlags.ToString()),
            ("castShadows", typeof(MeshRenderer), "m_CastShadows", c => ((Renderer)c).shadowCastingMode.ToString()),
            ("priority", typeof(AudioSource), "m_Priority", c => ((AudioSource)c).priority.ToString(Inv)),
        };

        void Start()
        {
            // Which bindings are discrete, per component (one object each).
            var bindings = new List<EditorCurveBinding>();
            foreach (var type in Cases.Select(c => c.Type).Distinct())
            {
                var holder = new GameObject("bindings." + type.Name);
                if (holder.GetComponent(type) == null) holder.AddComponent(type);
                var own = AnimationUtility.GetAnimatableBindings(holder, holder).Where(b => b.type == type).ToList();
                bindings.AddRange(own);
                var discrete = own.Where(b => b.isDiscreteCurve).Select(b => b.propertyName).OrderBy(n => n, StringComparer.Ordinal);
                Put("discrete." + type.Name, string.Join(" ", discrete));
            }
            foreach (var c in Cases)
            {
                var binding = bindings.FirstOrDefault(b => b.type == c.Type && b.propertyName == c.Property);
                Put("binding." + c.Name, binding.type == null ? "none" : binding.isDiscreteCurve ? "discrete" : "float");
            }

            // The values a played clip sets, keys at 0 and 1: bits of 1 then 2, or the numbers 1 then 2.
            foreach (var c in Cases)
                foreach (var (form, first, second) in new[] { ("bits", Bits(1), Bits(2)), ("numbers", 1f, 2f), ("float", 1f, 2.6f) })
                {
                    var go = new GameObject(c.Name + "." + form);
                    var component = go.GetComponent(c.Type);
                    if (component == null) component = go.AddComponent(c.Type);
                    var animator = go.AddComponent<Animator>();
                    var clip = new AnimationClip { name = c.Name + "." + form };
                    var curve = new AnimationCurve(new Keyframe(0f, first), new Keyframe(1f, second));
                    // "float": the same property as a float binding (a control).
                    var binding = form == "float" ? EditorCurveBinding.FloatCurve("", c.Type, c.Property) : EditorCurveBinding.DiscreteCurve("", c.Type, c.Property);
                    AnimationUtility.SetEditorCurve(clip, binding, curve);
                    var graph = PlayableGraph.Create(go.name);
                    graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                    AnimationPlayableOutput.Create(graph, "out", animator).SetSourcePlayable(AnimationClipPlayable.Create(graph, clip));
                    var values = new List<string> { c.Read(component) };
                    foreach (float t in new[] { 0f, 0.5f, 0.5f })
                    {
                        graph.Evaluate(t);
                        values.Add(c.Read(component));
                    }
                    graph.Destroy();
                    Put("play." + c.Name + "." + form, string.Join(" ", values));
                }

            Put("complete", true);
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("ENGINE_DISCRETE_OUT"), "values.jsonl"), m_Out.ToString());
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
            foreach (char ch in text)
            {
                if (ch == '"' || ch == '\\') sb.Append('\\').Append(ch);
                else if (ch < ' ') sb.Append("\\u").Append(((int)ch).ToString("x4", Inv));
                else sb.Append(ch);
            }
            return sb.Append('"').ToString();
        }
    }
}
