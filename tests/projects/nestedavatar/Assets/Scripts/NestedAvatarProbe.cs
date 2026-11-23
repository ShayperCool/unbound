using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace NestedAvatarProbe
{
    /// <summary>
    /// Generic clips played by an Animator above the model they were made for: the Animator on a
    /// holder object with the model's avatar and a controller looping one clip of the model, the
    /// model (its own Animator removed) a child of the holder. Records, after updates of 0 and
    /// 0.25 s, the avatar root and the animated nodes' local poses.
    /// </summary>
    public class NestedAvatarProbeBehaviour : MonoBehaviour
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        readonly StringBuilder m_Out = new StringBuilder();
        readonly HashSet<string> m_Keys = new HashSet<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NESTED_AVATAR_OUT"))) return;
            new GameObject("probe").AddComponent<NestedAvatarProbeBehaviour>();
        }

        static string F(float f) => f.ToString("0.###", Inv);
        static string V(Vector3 v) => F(v.x) + "," + F(v.y) + "," + F(v.z);
        static string Q(Quaternion q) => F(q.x) + "," + F(q.y) + "," + F(q.z) + "," + F(q.w);

        /// <summary>The case's holder and the model below it: the model's own Animator gives its avatar
        /// to the holder (unless <paramref name="avatar"/> is false) and is removed.</summary>
        static (Animator animator, Transform model) Nested(string modelName, string controller, string name, int depth, string rename, bool avatar, float x)
        {
            var holder = new GameObject(name);
            holder.transform.position = new Vector3(x, 0, 0);
            var parent = holder.transform;
            for (int i = 1; i < depth; i++)
            {
                var mid = new GameObject("Mid" + i).transform;
                mid.SetParent(parent, false);
                parent = mid;
            }
            var model = Instantiate(Resources.Load<GameObject>(modelName), parent, false);
            model.name = rename ?? modelName;
            var own = model.GetComponent<Animator>();
            var source = own != null ? own.avatar : null;
            if (own != null) DestroyImmediate(own);
            var animator = holder.AddComponent<Animator>();
            if (avatar) animator.avatar = source;
            if (controller != null) animator.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>(controller);
            return (animator, model.transform);
        }

        /// <summary>Hips (with a Leg below when <paramref name="leg"/>) under the holder, or under a
        /// new child of it named <paramref name="branch"/>; returns the topmost new transform.</summary>
        static Transform Decoy(Transform holder, string branch, bool leg)
        {
            var parent = holder;
            if (branch != null)
            {
                parent = new GameObject(branch).transform;
                parent.SetParent(holder, false);
            }
            var hips = new GameObject("Hips").transform;
            hips.SetParent(parent, false);
            if (leg && branch == null) new GameObject("Leg").transform.SetParent(hips, false);
            hips.SetAsFirstSibling();
            return branch != null ? parent : hips;
        }

        string Pose(Animator animator, Transform model)
        {
            if (animator.isHuman)
            {
                var upperLeg = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
                var spine = animator.GetBoneTransform(HumanBodyBones.Spine);
                return "avatarRoot " + (animator.avatarRoot != null ? animator.avatarRoot.name : "null")
                    + "|upperLeg " + (upperLeg != null ? upperLeg.name + " " + Q(upperLeg.localRotation) : "none")
                    + "|spine " + (spine != null ? spine.name + " " + Q(spine.localRotation) : "none");
            }
            var hips = model.Find("Hips");
            var leg = hips != null ? hips.Find("Leg") : null;
            var prop = model.Find("Prop");
            return "avatarRoot " + (animator.avatarRoot != null ? animator.avatarRoot.name : "null")
                + "|initialized " + animator.isInitialized
                + "|hips " + (hips != null ? V(hips.localPosition) + " " + Q(hips.localRotation) : "none")
                + "|leg " + (leg != null ? V(leg.localPosition) + " " + Q(leg.localRotation) : "none")
                + "|prop " + (prop != null ? V(prop.localPosition) : "none")
                + "|holder " + V(animator.transform.localPosition);
        }

        void Start()
        {
            var cases = new List<(string, Animator, Transform)>();
            void Add(string key, string model, string controller, int depth = 1, string rename = null, bool avatar = true)
            {
                var (animator, root) = Nested(model, controller, key, depth, rename, avatar, cases.Count * 10);
                cases.Add((key, animator, root));
            }
            Add("nested", "WalkerNoNode", "C_WalkerNoNode_Loop");
            Add("renamed", "WalkerNoNode", "C_WalkerNoNode_Loop", rename: "Renamed");
            Add("noAvatar", "WalkerNoNode", "C_WalkerNoNode_Loop", avatar: false);
            Add("deep", "WalkerNoNode", "C_WalkerNoNode_Loop", depth: 2);
            Add("rootNode", "Walker", "C_Walker_Loop");
            // A decoy branch before the model holding only a Hips: the model matches more paths.
            Add("decoy", "WalkerNoNode", "C_WalkerNoNode_Loop");
            Decoy(cases[cases.Count - 1].Item2.transform, "Decoy", true).SetAsFirstSibling();
            // The holder itself holds Hips/Leg (two of the avatar's paths) before the model.
            Add("holderPartial", "WalkerNoNode", "C_WalkerNoNode_Loop");
            Decoy(cases[cases.Count - 1].Item2.transform, null, true);
            // The holder itself holds a Hips (without its Leg) and a Prop: as many top-level names
            // as the model, fewer paths.
            Add("holderNames", "WalkerNoNode", "C_WalkerNoNode_Loop");
            var namesHolder = cases[cases.Count - 1].Item2.transform;
            Decoy(namesHolder, null, false);
            var decoyProp = new GameObject("Prop").transform;
            decoyProp.SetParent(namesHolder, false);
            decoyProp.SetSiblingIndex(1);
            // Two copies of the model: the first in the hierarchy wins a tie.
            Add("tie", "WalkerNoNode", "C_WalkerNoNode_Loop");
            var second = Instantiate(Resources.Load<GameObject>("WalkerNoNode"), cases[cases.Count - 1].Item2.transform, false);
            second.name = "Second";
            DestroyImmediate(second.GetComponent<Animator>());
            // A humanoid model below its holder, its controller playing muscle curves; the model's
            // own Animator as a control.
            Add("human", "HumanA", "HumanLoopMuscles");
            var humanDirect = Instantiate(Resources.Load<GameObject>("HumanA"), new Vector3(80, 0, 0), Quaternion.identity);
            var humanDirectAnimator = humanDirect.GetComponent<Animator>();
            humanDirectAnimator.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>("HumanLoopMuscles");
            cases.Add(("humanDirect", humanDirectAnimator, humanDirect.transform));
            // Control: the model's own Animator, as imported.
            var direct = Instantiate(Resources.Load<GameObject>("WalkerNoNode"), new Vector3(60, 0, 0), Quaternion.identity);
            var directAnimator = direct.GetComponent<Animator>();
            directAnimator.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>("C_WalkerNoNode_Loop");
            cases.Add(("direct", directAnimator, direct.transform));

            foreach (var (key, animator, model) in cases)
            {
                animator.enabled = false;
                animator.Update(0f);
                Put(key + ".start", Pose(animator, model));
                animator.Update(0.25f);
                Put(key + ".quarter", Pose(animator, model));
            }

            // After a first update, a second copy of the model is added before the first: updated
            // again, then after Rebind.
            var (cached, cachedModel) = Nested("WalkerNoNode", "C_WalkerNoNode_Loop", "cached", 1, null, true, 130);
            cached.enabled = false;
            cached.Update(0f);
            var added = Instantiate(Resources.Load<GameObject>("WalkerNoNode"), cached.transform, false);
            added.name = "Added";
            DestroyImmediate(added.GetComponent<Animator>());
            added.transform.SetAsFirstSibling();
            cached.Update(0.25f);
            Put("cached.added", Pose(cached, cachedModel) + "|added " + Pose(cached, added.transform));
            cached.Rebind();
            cached.Update(0.25f);
            Put("cached.rebound", Pose(cached, cachedModel) + "|added " + Pose(cached, added.transform));

            // A controller made here whose layer's mask keeps the root entry and Hips and leaves out
            // Prop, playing a clip that moves Hips and Prop and the holder itself (an empty path).
            var layerClip = new AnimationClip { name = "LayerClip" };
            layerClip.SetCurve("Hips", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Linear(0f, 0f, 1f, 2f));
            layerClip.SetCurve("Prop", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Linear(0f, 0f, 1f, 2f));
            layerClip.SetCurve("", typeof(Transform), "m_LocalPosition.y", AnimationCurve.Linear(0f, 0f, 1f, 2f));
            var controllerMask = new AvatarMask { transformCount = 3 };
            controllerMask.SetTransformPath(0, "");
            controllerMask.SetTransformActive(0, true);
            controllerMask.SetTransformPath(1, "Hips");
            controllerMask.SetTransformActive(1, true);
            controllerMask.SetTransformPath(2, "Prop");
            controllerMask.SetTransformActive(2, false);
            var layerController = new UnityEditor.Animations.AnimatorController { name = "MaskedLayer" };
            layerController.AddLayer("Base Layer");
            var controllerLayers = layerController.layers;
            controllerLayers[0].avatarMask = controllerMask;
            layerController.layers = controllerLayers;
            layerController.AddMotion(layerClip);
            var (maskedLayer, maskedLayerModel) = Nested("WalkerNoNode", null, "maskedLayer", 1, null, true, 140);
            maskedLayer.runtimeAnimatorController = layerController;
            maskedLayer.enabled = false;
            maskedLayer.Update(0f);
            maskedLayer.Update(0.5f);
            Put("maskedLayer.half", Pose(maskedLayer, maskedLayerModel));

            // Played by playable graphs evaluated by hand: the humanoid's muscle clip below its holder
            // and on the model's own Animator, and the generic clip below its holder through a layer
            // whose mask keeps Hips and leaves out Hips/Leg and Prop.
            var graphs = new List<(string, Animator, Transform, AnimationClip, AvatarMask)>();
            var muscles = Resources.Load<AnimationClip>("LoopMuscles");
            var (humanGraph, humanGraphModel) = Nested("HumanA", null, "humanGraph", 1, null, true, 100);
            graphs.Add(("humanGraph", humanGraph, humanGraphModel, muscles, null));
            var humanGraphDirect = Instantiate(Resources.Load<GameObject>("HumanA"), new Vector3(110, 0, 0), Quaternion.identity);
            graphs.Add(("humanGraphDirect", humanGraphDirect.GetComponent<Animator>(), humanGraphDirect.transform, muscles, null));
            var mask = new AvatarMask { transformCount = 3 };
            mask.SetTransformPath(0, "Hips");
            mask.SetTransformActive(0, true);
            mask.SetTransformPath(1, "Hips/Leg");
            mask.SetTransformActive(1, false);
            mask.SetTransformPath(2, "Prop");
            mask.SetTransformActive(2, false);
            var loop = Resources.Load<RuntimeAnimatorController>("C_WalkerNoNode_Loop").animationClips[0];
            var (masked, maskedModel) = Nested("WalkerNoNode", null, "masked", 1, null, true, 120);
            graphs.Add(("masked", masked, maskedModel, loop, mask));
            foreach (var (key, animator, model, clip, layerMask) in graphs)
            {
                var graph = PlayableGraph.Create(key);
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var output = AnimationPlayableOutput.Create(graph, key, animator);
                var source = AnimationClipPlayable.Create(graph, clip);
                if (layerMask != null)
                {
                    var layers = AnimationLayerMixerPlayable.Create(graph, 1);
                    graph.Connect(source, 0, layers, 0);
                    layers.SetInputWeight(0, 1f);
                    layers.SetLayerMaskFromAvatarMask(0, layerMask);
                    output.SetSourcePlayable(layers);
                }
                else output.SetSourcePlayable(source);
                graph.Evaluate(0f);
                Put(key + ".start", Pose(animator, model));
                graph.Evaluate(0.25f);
                Put(key + ".quarter", Pose(animator, model));
                graph.Destroy();
            }

            Put("complete", true);
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NESTED_AVATAR_OUT"), "values.jsonl"), m_Out.ToString());
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
