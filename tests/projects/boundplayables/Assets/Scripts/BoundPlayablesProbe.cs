using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace BoundPlayablesProbe
{
    /// <summary>
    /// Animator.hasBoundPlayables through an Animator's life: without a controller, with one before
    /// and after its first update and over frames, disabled, inactive, with the controller taken away,
    /// with a playable graph's output added and destroyed, and an Animator in the scene file.
    /// </summary>
    public class BoundPlayablesProbeBehaviour : MonoBehaviour
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        readonly StringBuilder m_Out = new StringBuilder();
        readonly HashSet<string> m_Keys = new HashSet<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BOUND_PLAYABLES_OUT"))) return;
            new GameObject("probe").AddComponent<BoundPlayablesProbeBehaviour>();
        }

        static RuntimeAnimatorController Controller() => Resources.Load<RuntimeAnimatorController>("C_WalkerNoNode_Loop");

        static Animator Make(string name, bool controller, bool active = true)
        {
            var go = new GameObject(name);
            go.SetActive(active);
            var animator = go.AddComponent<Animator>();
            if (controller) animator.runtimeAnimatorController = Controller();
            return animator;
        }

        IEnumerator Start()
        {
            var none = Make("none", false);
            Put("none.made", none.hasBoundPlayables);
            var controlled = Make("controlled", true);
            Put("controlled.made", controlled.hasBoundPlayables);
            var updated = Make("updated", true);
            updated.Update(0f);
            Put("updated.update", updated.hasBoundPlayables);
            var disabled = Make("disabled", true);
            disabled.enabled = false;
            Put("disabled.made", disabled.hasBoundPlayables);
            var inactive = Make("inactive", true, active: false);
            Put("inactive.made", inactive.hasBoundPlayables);
            var removed = Make("removed", true);
            removed.runtimeAnimatorController = null;
            Put("removed.made", removed.hasBoundPlayables);

            var output = Make("output", false);
            var graph = PlayableGraph.Create("bound");
            var playableOutput = AnimationPlayableOutput.Create(graph, "out", output);
            Put("output.created", output.hasBoundPlayables);
            playableOutput.SetSourcePlayable(AnimationClipPlayable.Create(graph, Controller().animationClips[0]));
            Put("output.sourced", output.hasBoundPlayables);
            graph.Destroy();
            Put("output.destroyed", output.hasBoundPlayables);
            var outputWithController = Make("outputWithController", true);
            var graph2 = PlayableGraph.Create("bound2");
            AnimationPlayableOutput.Create(graph2, "out", outputWithController);
            Put("outputWithController.created", outputWithController.hasBoundPlayables);
            graph2.Destroy();
            Put("outputWithController.destroyed", outputWithController.hasBoundPlayables);

            yield return null;
            yield return null;
            foreach (var a in new[] { none, controlled, updated, disabled, inactive, removed, output, outputWithController })
                Put(a.name + ".frames", a.hasBoundPlayables);
            disabled.enabled = true;
            inactive.gameObject.SetActive(true);
            removed.runtimeAnimatorController = Controller();
            controlled.runtimeAnimatorController = null;
            Put("disabled.enabled", disabled.hasBoundPlayables);
            Put("inactive.activated", inactive.hasBoundPlayables);
            Put("removed.given", removed.hasBoundPlayables);
            Put("controlled.taken", controlled.hasBoundPlayables);
            yield return null;
            Put("disabled.enabledFrame", disabled.hasBoundPlayables);
            Put("inactive.activatedFrame", inactive.hasBoundPlayables);
            Put("removed.givenFrame", removed.hasBoundPlayables);
            Put("controlled.takenFrame", controlled.hasBoundPlayables);
            controlled.enabled = false;
            removed.gameObject.SetActive(false);
            Put("controlled.takenDisabled", controlled.hasBoundPlayables);
            Put("removed.deactivated", removed.hasBoundPlayables);
            yield return null;
            Put("removed.deactivatedFrame", removed.hasBoundPlayables);
            removed.gameObject.SetActive(true);
            Put("removed.reactivated", removed.hasBoundPlayables);

            Put("complete", true);
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("BOUND_PLAYABLES_OUT"), "values.jsonl"), m_Out.ToString());
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
