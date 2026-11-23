using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

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
            animator.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>(controller);
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
            // A humanoid model below its holder, playing muscle curves.
            Add("human", "HumanA", "HumanLoopMuscles");
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
