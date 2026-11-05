using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unbound.Runtime;
using UnityEngine;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Script messages Unbound never sends warn once per declaring script class and
    /// message (Stubs.Ignored: the first hit of a key logs, later hits only count). Sources: the
    /// Unity 6000.0 Scripting API pages of MonoBehaviour, ScriptableObject and
    /// StateMachineBehaviour, and uGUI 2.0's UIBehaviour (URLs in the classification below).
    /// Unbound's diagnostics do not reach Application.logMessageReceived, so the warnings are
    /// read from Stubs.HitReport, one key per logged warning.</summary>
    public class MessageCoverageTests
    {
        const string Prefix = "UnityEngine.Tests.MessageCoverageTests+";

        /// <summary>The warning keys of the named nested script classes (closed generic names
        /// included, so a per-closed-type key would show).</summary>
        static List<string> KeysFor(params string[] typeNames) =>
            Stubs.HitReport().Select(h => h.Key)
                .Where(k => typeNames.Any(n => k.StartsWith(Prefix + n, StringComparison.Ordinal)
                    && k.Length > Prefix.Length + n.Length && ".`[".IndexOf(k[Prefix.Length + n.Length]) >= 0))
                .OrderBy(k => k, StringComparer.Ordinal)
                .ToList();

        /// <summary>The not-sent check by itself, as the StateMachineBehaviour instantiation site
        /// would call it (looked up by reflection: it is private at 747e3f23).</summary>
        static void WarnNotSent(Type type)
        {
            var method = typeof(ScriptMessages).GetMethod("WarnNotSent",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Type) }, null);
            Assert.NotNull(method);
            method.Invoke(null, new object[] { type });
        }

        // --- scripts ---

        public class ChildDimensionsScript : MonoBehaviour
        {
            void OnChildRectTransformDimensionsChange() { }
            void Update() { }
        }

        public class ParticleJobScript : MonoBehaviour
        {
            void OnParticleUpdateJobScheduled() { }
        }

        public class SentOnlyScript : MonoBehaviour
        {
            void Awake() { }
            void Start() { }
            void Update() { }
            void OnGUI() { }
            void OnRectTransformDimensionsChange() { }
            void OnCanvasHierarchyChanged() { }
            void OnTransformParentChanged() { }
            // Not sent in Play mode by the Editor either: no warning.
            void Reset() { }
            void OnDrawGizmos() { }
            void OnDrawGizmosSelected() { }
            // A StateMachineBehaviour message name on a MonoBehaviour is no message of it.
            void OnStateIK() { }
        }

        public class ImplicitReceiverScript : MonoBehaviour, ISerializationCallbackReceiver
        {
            public void OnBeforeSerialize() { }
            public void OnAfterDeserialize() { }
        }

        public class ExplicitReceiverScript : MonoBehaviour, ISerializationCallbackReceiver
        {
            void ISerializationCallbackReceiver.OnBeforeSerialize() { }
            void ISerializationCallbackReceiver.OnAfterDeserialize() { }
        }

        public class ChildDimensionsBase : MonoBehaviour
        {
            protected void OnChildRectTransformDimensionsChange() { }
        }

        public class ChildDimensionsLeft : ChildDimensionsBase { }

        public class ChildDimensionsRight : ChildDimensionsBase
        {
            void Update() { }
        }

        public class ParticleJobGenericBase<T> : MonoBehaviour
        {
            void OnParticleUpdateJobScheduled() { }
        }

        public class ParticleJobIntScript : ParticleJobGenericBase<int> { }

        public class ParticleJobTextScript : ParticleJobGenericBase<string> { }

        public class IKStateBehaviour : StateMachineBehaviour
        {
            public override void OnStateIK(Animator animator, AnimatorStateInfo stateInfo, int layerIndex) { }
        }

        public class IKStateDerived : IKStateBehaviour { }

        public class PlainStateBehaviour : StateMachineBehaviour
        {
            public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex) { }
            // A MonoBehaviour message name on a state behaviour is no message of it.
            void OnChildRectTransformDimensionsChange() { }
        }

        // --- per-case tests ---

        /// <summary>RED at 747e3f23: OnChildRectTransformDimensionsChange is not listed, no key.</summary>
        [Fact]
        public void NotSentMessageWarnsOnceForItsMember()
        {
            ScriptMessages.For(typeof(ChildDimensionsScript));
            ScriptMessages.For(typeof(ChildDimensionsScript));
            Assert.Equal(new[] { Prefix + "ChildDimensionsScript.OnChildRectTransformDimensionsChange" },
                KeysFor("ChildDimensionsScript"));
        }

        /// <summary>Control for the already listed entry. GREEN at 747e3f23; red recipe: remove
        /// OnParticleUpdateJobScheduled from s_NotSent.</summary>
        [Fact]
        public void ListedParticleJobMessageStillWarns()
        {
            ScriptMessages.For(typeof(ParticleJobScript));
            Assert.Equal(new[] { Prefix + "ParticleJobScript.OnParticleUpdateJobScheduled" },
                KeysFor("ParticleJobScript"));
        }

        /// <summary>Guard. GREEN at 747e3f23; red recipes: add Reset, OnDrawGizmos, Update or
        /// OnBeforeSerialize to s_NotSent, or list OnStateIK for MonoBehaviour.</summary>
        [Fact]
        public void SentAndEditorAbsentMessagesWarnNothing()
        {
            ScriptMessages.For(typeof(SentOnlyScript));
            ScriptMessages.For(typeof(ImplicitReceiverScript));
            ScriptMessages.For(typeof(ExplicitReceiverScript));
            Assert.Empty(KeysFor("SentOnlyScript", "ImplicitReceiverScript", "ExplicitReceiverScript"));
        }

        /// <summary>RED at 747e3f23: OnChildRectTransformDimensionsChange is not listed, no key.</summary>
        [Fact]
        public void DerivedClassesWarnOnceOnTheDeclaringBase()
        {
            ScriptMessages.For(typeof(ChildDimensionsLeft));
            ScriptMessages.For(typeof(ChildDimensionsRight));
            ScriptMessages.For(typeof(ChildDimensionsBase));
            Assert.Equal(new[] { Prefix + "ChildDimensionsBase.OnChildRectTransformDimensionsChange" },
                KeysFor("ChildDimensionsBase", "ChildDimensionsLeft", "ChildDimensionsRight"));
        }

        /// <summary>RED at 747e3f23: each closed base type gets its own key
        /// (ParticleJobGenericBase`1[[System.Int32, ...]] and [[System.String, ...]]).</summary>
        [Fact]
        public void GenericBaseWarnsOnceForAllClosedTypes()
        {
            ScriptMessages.For(typeof(ParticleJobIntScript));
            ScriptMessages.For(typeof(ParticleJobTextScript));
            Assert.Equal(new[] { Prefix + "ParticleJobGenericBase`1.OnParticleUpdateJobScheduled" },
                KeysFor("ParticleJobGenericBase", "ParticleJobIntScript", "ParticleJobTextScript"));
        }

        /// <summary>Checks the list only: the warning call at the state machine behaviour
        /// instantiation site is still pending, so a running Animator does not warn yet.
        /// RED at 747e3f23 for the IK keys: OnStateIK is not listed. The plain behaviour
        /// and StateMachineBehaviour's own virtual stay silent (GREEN at 747e3f23; red recipe:
        /// let the walk pass StateMachineBehaviour, or drop the host check).</summary>
        [Fact]
        public void WarnNotSentFlagsStateBehaviourOnStateIKOnce()
        {
            WarnNotSent(typeof(IKStateBehaviour));
            WarnNotSent(typeof(IKStateDerived));
            WarnNotSent(typeof(PlainStateBehaviour));
            Assert.Equal(new[] { Prefix + "IKStateBehaviour.OnStateIK" },
                KeysFor("IKStateBehaviour", "IKStateDerived"));
            Assert.Empty(KeysFor("PlainStateBehaviour"));
            Assert.DoesNotContain(Stubs.HitReport(), h => h.Key.StartsWith("UnityEngine.StateMachineBehaviour.", StringComparison.Ordinal));
        }

        // --- classification ---

        enum Delivery { Sent, NotSent, EditorAbsent }

        const string MonoBehaviourPage = "https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MonoBehaviour.html";
        const string ScriptableObjectPage = "https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ScriptableObject.html";
        const string StateMachineBehaviourPage = "https://docs.unity3d.com/6000.0/Documentation/ScriptReference/StateMachineBehaviour.html";
        const string UIBehaviourPage = "https://docs.unity3d.com/Packages/com.unity.ugui@2.0/api/UnityEngine.EventSystems.UIBehaviour.html";

        /// <summary>Every documented script message (Unity 6000.0) and whether Unbound sends it.
        /// Dispatch evidence with file:line is in the receipt of this change. EditorAbsent: the
        /// Editor's Play mode does not send it to the game either (Reset: "only called in Edit
        /// mode"; OnDrawGizmos*: no Scene view, no Game view gizmos, Unbound's choice).</summary>
        static readonly (string Page, string Name, Delivery Class)[] s_Documented =
        {
            (MonoBehaviourPage, "Awake", Delivery.Sent),
            (MonoBehaviourPage, "FixedUpdate", Delivery.Sent),
            (MonoBehaviourPage, "LateUpdate", Delivery.Sent),
            (MonoBehaviourPage, "OnAnimatorIK", Delivery.Sent),
            (MonoBehaviourPage, "OnAnimatorMove", Delivery.Sent),
            (MonoBehaviourPage, "OnApplicationFocus", Delivery.Sent),
            (MonoBehaviourPage, "OnApplicationPause", Delivery.Sent),
            (MonoBehaviourPage, "OnApplicationQuit", Delivery.Sent),
            (MonoBehaviourPage, "OnAudioFilterRead", Delivery.Sent),
            (MonoBehaviourPage, "OnBecameInvisible", Delivery.Sent),
            (MonoBehaviourPage, "OnBecameVisible", Delivery.Sent),
            (MonoBehaviourPage, "OnChildRectTransformDimensionsChange", Delivery.NotSent),
            (MonoBehaviourPage, "OnCollisionEnter", Delivery.Sent),
            (MonoBehaviourPage, "OnCollisionEnter2D", Delivery.Sent),
            (MonoBehaviourPage, "OnCollisionExit", Delivery.Sent),
            (MonoBehaviourPage, "OnCollisionExit2D", Delivery.Sent),
            (MonoBehaviourPage, "OnCollisionStay", Delivery.Sent),
            (MonoBehaviourPage, "OnCollisionStay2D", Delivery.Sent),
            (MonoBehaviourPage, "OnControllerColliderHit", Delivery.Sent),
            (MonoBehaviourPage, "OnDestroy", Delivery.Sent),
            (MonoBehaviourPage, "OnDisable", Delivery.Sent),
            (MonoBehaviourPage, "OnDrawGizmos", Delivery.EditorAbsent),
            (MonoBehaviourPage, "OnDrawGizmosSelected", Delivery.EditorAbsent),
            (MonoBehaviourPage, "OnEnable", Delivery.Sent),
            (MonoBehaviourPage, "OnGUI", Delivery.Sent),
            (MonoBehaviourPage, "OnJointBreak", Delivery.Sent),
            (MonoBehaviourPage, "OnJointBreak2D", Delivery.Sent),
            (MonoBehaviourPage, "OnMouseDown", Delivery.Sent),
            (MonoBehaviourPage, "OnMouseDrag", Delivery.Sent),
            (MonoBehaviourPage, "OnMouseEnter", Delivery.Sent),
            (MonoBehaviourPage, "OnMouseExit", Delivery.Sent),
            (MonoBehaviourPage, "OnMouseOver", Delivery.Sent),
            (MonoBehaviourPage, "OnMouseUp", Delivery.Sent),
            (MonoBehaviourPage, "OnMouseUpAsButton", Delivery.Sent),
            (MonoBehaviourPage, "OnParticleCollision", Delivery.Sent),
            (MonoBehaviourPage, "OnParticleSystemStopped", Delivery.Sent),
            (MonoBehaviourPage, "OnParticleTrigger", Delivery.Sent),
            (MonoBehaviourPage, "OnParticleUpdateJobScheduled", Delivery.NotSent),
            (MonoBehaviourPage, "OnPostRender", Delivery.Sent),
            (MonoBehaviourPage, "OnPreCull", Delivery.Sent),
            (MonoBehaviourPage, "OnPreRender", Delivery.Sent),
            (MonoBehaviourPage, "OnRenderImage", Delivery.Sent),
            (MonoBehaviourPage, "OnRenderObject", Delivery.Sent),
            (MonoBehaviourPage, "OnTransformChildrenChanged", Delivery.Sent),
            (MonoBehaviourPage, "OnTransformParentChanged", Delivery.Sent),
            (MonoBehaviourPage, "OnTriggerEnter", Delivery.Sent),
            (MonoBehaviourPage, "OnTriggerEnter2D", Delivery.Sent),
            (MonoBehaviourPage, "OnTriggerExit", Delivery.Sent),
            (MonoBehaviourPage, "OnTriggerExit2D", Delivery.Sent),
            (MonoBehaviourPage, "OnTriggerStay", Delivery.Sent),
            (MonoBehaviourPage, "OnTriggerStay2D", Delivery.Sent),
            (MonoBehaviourPage, "OnValidate", Delivery.Sent),
            (MonoBehaviourPage, "OnWillRenderObject", Delivery.Sent),
            (MonoBehaviourPage, "Reset", Delivery.EditorAbsent),
            (MonoBehaviourPage, "Start", Delivery.Sent),
            (MonoBehaviourPage, "Update", Delivery.Sent),
            // UIBehaviour's own messages (its Awake/OnEnable/Start/OnDisable/OnDestroy/OnValidate/
            // Reset/OnTransformParentChanged are the MonoBehaviour ones above).
            (UIBehaviourPage, "OnBeforeTransformParentChanged", Delivery.Sent),
            (UIBehaviourPage, "OnCanvasGroupChanged", Delivery.Sent),
            (UIBehaviourPage, "OnCanvasHierarchyChanged", Delivery.Sent),
            (UIBehaviourPage, "OnDidApplyAnimationProperties", Delivery.Sent),
            (UIBehaviourPage, "OnRectTransformDimensionsChange", Delivery.Sent),
            (ScriptableObjectPage, "Awake", Delivery.Sent),
            (ScriptableObjectPage, "OnDestroy", Delivery.Sent),
            (ScriptableObjectPage, "OnDisable", Delivery.Sent),
            (ScriptableObjectPage, "OnEnable", Delivery.Sent),
            (ScriptableObjectPage, "OnValidate", Delivery.Sent),
            (ScriptableObjectPage, "Reset", Delivery.EditorAbsent),
            (StateMachineBehaviourPage, "OnStateEnter", Delivery.Sent),
            (StateMachineBehaviourPage, "OnStateExit", Delivery.Sent),
            (StateMachineBehaviourPage, "OnStateIK", Delivery.NotSent),
            (StateMachineBehaviourPage, "OnStateMachineEnter", Delivery.Sent),
            (StateMachineBehaviourPage, "OnStateMachineExit", Delivery.Sent),
            (StateMachineBehaviourPage, "OnStateMove", Delivery.Sent),
            (StateMachineBehaviourPage, "OnStateUpdate", Delivery.Sent),
        };

        /// <summary>s_NotSent's message names, whether its entries are names (747e3f23) or
        /// (host, name) pairs.</summary>
        static List<string> ListedNames()
        {
            var field = typeof(ScriptMessages).GetField("s_NotSent", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(field);
            var names = new List<string>();
            foreach (var entry in (Array)field.GetValue(null))
                names.Add(entry as string ?? (string)entry.GetType().GetField("Item2").GetValue(entry));
            return names;
        }

        /// <summary>A documented static assertion against the curated table above. RED at 747e3f23:
        /// OnChildRectTransformDimensionsChange and OnStateIK are not listed. The overlap checks
        /// are guards (GREEN at 747e3f23; red recipe: list a Sent or EditorAbsent name, or a
        /// name a ScriptMessages dispatch delegate is named after).</summary>
        [Fact]
        public void NotSentListMatchesTheClassification()
        {
            Assert.Equal(74, s_Documented.Length);
            Assert.Equal(s_Documented.Length, s_Documented.Select(d => (d.Page, d.Name)).Distinct().Count());
            Assert.Equal(67, s_Documented.Count(d => d.Class == Delivery.Sent));
            Assert.Equal(4, s_Documented.Count(d => d.Class == Delivery.EditorAbsent));

            var listed = ListedNames();
            Assert.Equal(listed.Count, listed.Distinct().Count());
            var notSent = s_Documented.Where(d => d.Class == Delivery.NotSent).Select(d => d.Name).OrderBy(n => n, StringComparer.Ordinal);
            Assert.Equal(notSent, listed.OrderBy(n => n, StringComparer.Ordinal));

            var otherwise = new HashSet<string>(s_Documented.Where(d => d.Class != Delivery.NotSent).Select(d => d.Name));
            Assert.DoesNotContain(listed, otherwise.Contains);
            // The cached dispatch delegates of ScriptMessages are messages the engine calls.
            var dispatched = typeof(ScriptMessages).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                .Where(f => typeof(Delegate).IsAssignableFrom(f.FieldType)).Select(f => f.Name).ToList();
            Assert.Contains("Update", dispatched);
            Assert.DoesNotContain(listed, dispatched.Contains);
            Assert.DoesNotContain("OnBeforeSerialize", listed);
            Assert.DoesNotContain("OnAfterDeserialize", listed);
        }
    }
}
