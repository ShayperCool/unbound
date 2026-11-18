using System;
using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;
using Unbound.Tests;
using UnityEditor.Compilation;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Scripts whose constructors all take arguments, hierarchy edits of loaded prefab
    /// assets, reparenting from inside parent-change messages, Animator warnings, missing-script
    /// asset objects and the compilation pipeline's compiler options, as the Unity 6000.0.71f1
    /// Editor gives them (tests/projects/gameinterop), each next to a control.</summary>
    public sealed class ScriptInteropTests : IDisposable
    {
        readonly List<string> m_Logs = new List<string>();
        readonly List<Object> m_Owned = new List<Object>();

        public ScriptInteropTests() => Application.logMessageReceived += Record;

        public void Dispose()
        {
            Application.logMessageReceived -= Record;
            foreach (var o in m_Owned) if (o != null) Object.DestroyImmediate(o);
        }

        void Record(string message, string stack, LogType type) => m_Logs.Add(type + ": " + message);

        string[] Logs(Action action)
        {
            m_Logs.Clear();
            action();
            return m_Logs.ToArray();
        }

        T Own<T>(T o) where T : Object
        {
            m_Owned.Add(o);
            return o;
        }

        // ------------------------------------------------------------------ constructors

        public class ArgumentOnly : MonoBehaviour
        {
            public static int Constructed, Awakened;
            public int number = 5;
            public List<int> list = new List<int> { 1 };
            [NonSerialized] public int transient = 7;
            public ArgumentOnly(int value) { Constructed++; transient = value; }
            void Awake() => Awakened++;
        }

        public class ScriptBase : MonoBehaviour
        {
            public static int Constructed;
            [NonSerialized] public int baseTransient = 3;
            public ScriptBase() { Constructed++; }
        }

        public class ArgumentOnlyDerived : ScriptBase
        {
            public ArgumentOnlyDerived(string label) { }
        }

        public class ParameterlessControl : MonoBehaviour
        {
            [NonSerialized] public int transient = 8;
        }

        public class ArgumentOnlyData : ScriptableObject
        {
            public static int Constructed;
            public int number = 5;
            public ArgumentOnlyData(int value) { Constructed++; }
        }

        [Fact]
        public void AScriptWithOnlyArgumentConstructorsIsCreatedWithoutRunningOne()
        {
            var go = Own(new GameObject("Args"));
            ArgumentOnly.Constructed = ArgumentOnly.Awakened = 0;
            ArgumentOnly added = null;
            Assert.Empty(Logs(() => added = go.AddComponent<ArgumentOnly>()));
            Assert.True(added != null);
            Assert.Equal(0, ArgumentOnly.Constructed);
            Assert.Equal(1, ArgumentOnly.Awakened);
            Assert.Equal(0, added.number);
            Assert.Null(added.list);
            Assert.Equal(0, added.transient);
            Assert.Same(added, go.GetComponent<ArgumentOnly>());

            // A script base class's parameterless constructor does not run either.
            ScriptBase.Constructed = 0;
            var derived = go.AddComponent<ArgumentOnlyDerived>();
            Assert.True(derived != null);
            Assert.Equal(0, ScriptBase.Constructed);
            Assert.Equal(0, derived.baseTransient);

            // Control: a class with a parameterless constructor runs it, initializers included.
            Assert.Equal(8, go.AddComponent<ParameterlessControl>().transient);
        }

        [Fact]
        public void AScriptableObjectWithOnlyArgumentConstructorsIsCreatedWithoutRunningOne()
        {
            ArgumentOnlyData.Constructed = 0;
            var data = Own(ScriptableObject.CreateInstance<ArgumentOnlyData>());
            Assert.True(data != null);
            Assert.Equal(0, ArgumentOnlyData.Constructed);
            Assert.Equal(0, data.number);
            var copy = Own(Object.Instantiate(data));
            Assert.Equal("(Clone)", copy.name);
        }

        // ------------------------------------------------------------------ hierarchies

        const string Rig = """
%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!1 &1
GameObject:
  m_Component:
  - component: {fileID: 11}
  m_Name: Rig
  m_IsActive: 1
--- !u!4 &11
Transform:
  m_GameObject: {fileID: 1}
  m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}
  m_LocalPosition: {x: 0, y: 0, z: 0}
  m_LocalScale: {x: 1, y: 1, z: 1}
  m_Children:
  - {fileID: 21}
  - {fileID: 31}
  m_Father: {fileID: 0}
--- !u!1 &2
GameObject:
  m_Component:
  - component: {fileID: 21}
  m_Name: Body
  m_IsActive: 1
--- !u!4 &21
Transform:
  m_GameObject: {fileID: 2}
  m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}
  m_LocalPosition: {x: 0, y: 0, z: 0}
  m_LocalScale: {x: 1, y: 1, z: 1}
  m_Children:
  - {fileID: 41}
  m_Father: {fileID: 11}
--- !u!1 &3
GameObject:
  m_Component:
  - component: {fileID: 31}
  m_Name: Base
  m_IsActive: 1
--- !u!4 &31
Transform:
  m_GameObject: {fileID: 3}
  m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}
  m_LocalPosition: {x: 0, y: 0, z: 0}
  m_LocalScale: {x: 1, y: 1, z: 1}
  m_Children: []
  m_Father: {fileID: 11}
--- !u!1 &4
GameObject:
  m_Component:
  - component: {fileID: 41}
  m_Name: Head
  m_IsActive: 1
--- !u!4 &41
Transform:
  m_GameObject: {fileID: 4}
  m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}
  m_LocalPosition: {x: 0, y: 0, z: 0}
  m_LocalScale: {x: 1, y: 1, z: 1}
  m_Children: []
  m_Father: {fileID: 21}
""";

        static GameObject LoadRig() =>
            ObjectGraphBuilder.Build(UnityYaml.Parse(Rig), "rig.prefab", null, ObjectGraphBuilder.Kind.PrefabAsset, null).Roots[0];

        static string Tree(Transform t) =>
            t.name + (t.childCount == 0 ? "" : "(" + string.Join(",", Enumerable.Range(0, t.childCount).Select(i => Tree(t.GetChild(i)))) + ")");

        const string ParentRefused = "Error: Setting the parent of a transform which resides in a Prefab Asset is disabled to prevent data corruption";
        const string TransformRefused = "Error: Transform resides in a Prefab asset and cannot be set to prevent data corruption";

        [Fact]
        public void ALoadedPrefabAssetKeepsItsHierarchy()
        {
            var rig = LoadRig();
            var head = rig.transform.Find("Body/Head");
            var basePart = rig.transform.Find("Base");
            Assert.Equal(new[] { ParentRefused }, Logs(() => head.SetParent(basePart)));
            Assert.Equal(new[] { ParentRefused }, Logs(() => head.parent = basePart));
            Assert.Equal(new[] { TransformRefused }, Logs(() => head.SetParent(null)));
            var scene = Own(new GameObject("SceneParent"));
            Assert.Equal(new[] { TransformRefused }, Logs(() => head.SetParent(scene.transform)));
            var loose = Own(new GameObject("Loose"));
            Assert.Equal(new[] { ParentRefused }, Logs(() => loose.transform.SetParent(basePart)));
            Assert.Null(loose.transform.parent);
            Assert.Equal(new[] { ParentRefused + " (GameObject: 'Head')." }, Logs(() => head.parent.DetachChildren()));
            Assert.Equal(new[] { "Log: Changing Transform on a Prefab instance (Head) is not allowed." },
                Logs(() => Assert.Null(head.gameObject.AddComponent<RectTransform>())));
            Assert.Equal("Rig(Body(Head),Base)", Tree(rig.transform));

            // An instance of the asset copies it as it is; its own transforms reparent freely
            // (the control: the same calls on a scene object).
            var clone = Own(Object.Instantiate(rig));
            Assert.Equal("Rig(Clone)(Body(Head),Base)", Tree(clone.transform));
            Assert.Empty(Logs(() => clone.transform.Find("Body/Head").SetParent(clone.transform.Find("Base"))));
            Assert.Equal("Rig(Clone)(Body,Base(Head))", Tree(clone.transform));
        }

        [Fact]
        public void APersistentParentGivesTheCopyNoParent()
        {
            var rig = LoadRig();
            var piece = Own(new GameObject("Piece"));
            GameObject copy = null;
            Assert.Equal(new[] { "Error: Cannot instantiate objects with a parent which is persistent. New object will be created without a parent." },
                Logs(() => copy = Own(Object.Instantiate(piece, rig.transform.Find("Body")))));
            Assert.Null(copy.transform.parent);
            Assert.True(copy.scene.IsValid());
            Assert.Equal("Rig(Body(Head),Base)", Tree(rig.transform));
            // Control: a scene parent takes the copy.
            var holder = Own(new GameObject("Holder"));
            Assert.Same(holder.transform, Own(Object.Instantiate(piece, holder.transform)).transform.parent);
        }

        [Fact]
        public void AnImporterStillBuildsItsAssetsHierarchy()
        {
            using (ScriptedImports.Scope())
            {
                var root = new GameObject("Imported");
                var child = new GameObject("Child");
                Assert.Empty(Logs(() => child.transform.SetParent(root.transform)));
                Assert.Same(root.transform, child.transform.parent);
                Object.DestroyImmediate(root);
            }
        }

        public class MoveOnBefore : MonoBehaviour
        {
            public Transform target;
            public int enables, disables;
            bool done;
            void OnEnable() => enables++;
            void OnDisable() => disables++;
            void OnBeforeTransformParentChanged()
            {
                if (done) return;
                done = true;
                transform.SetParent(target, false);
            }
        }

        [Fact]
        public void AReparentFromInsideTheBeforeMessageLeavesOneParent()
        {
            var p1 = Own(new GameObject("P1")).transform;
            var p2 = Own(new GameObject("P2")).transform;
            var p3 = Own(new GameObject("P3")).transform;
            var x = new GameObject("X").transform;
            x.SetParent(p1);
            x.gameObject.AddComponent<MoveOnBefore>().target = p2;
            x.SetParent(p3);
            Assert.Same(p3, x.parent);
            Assert.Equal(0, p1.childCount);
            Assert.Equal(0, p2.childCount);
            Assert.Equal(1, p3.childCount);
            Assert.Equal("P2", Tree(Own(Object.Instantiate(p2.gameObject)).transform).Replace("(Clone)", ""));
            Assert.Equal("P3(X)", Tree(Own(Object.Instantiate(p3.gameObject)).transform).Replace("(Clone)", ""));

            // Through an inactive interim parent: disabled there, enabled again where it ends.
            var r2 = Own(new GameObject("R2")).transform;
            r2.gameObject.SetActive(false);
            var z = new GameObject("Z").transform;
            z.SetParent(p1);
            var mover = z.gameObject.AddComponent<MoveOnBefore>();
            mover.target = r2;
            z.SetParent(p3);
            Assert.Same(p3, z.parent);
            Assert.Equal(0, r2.childCount);
            Assert.True(mover.isActiveAndEnabled);
            Assert.Equal((2, 1), (mover.enables, mover.disables));

            // Control: a reparent to the root from inside the message.
            var y = new GameObject("Y").transform;
            y.SetParent(p1);
            y.gameObject.AddComponent<MoveOnBefore>().target = null;
            y.SetParent(p3);
            Assert.Same(p3, y.parent);
            Assert.Equal(0, p1.childCount);
        }

        public class CreatesOnValidate : MonoBehaviour
        {
            void OnValidate() => new GameObject("MadeInValidate");
        }

        [Fact]
        public void AGameObjectMadeInOnValidateWarnsOfItsComponentMessage()
        {
            var go = Own(new GameObject("Validator"));
            var c = go.AddComponent<CreatesOnValidate>();
            Assert.Contains("Warning: SendMessage cannot be called during Awake, CheckConsistency, or OnValidate (MadeInValidate: OnDidAddComponent)",
                Logs(() => ScriptMessages.Validate(c)));
            // Control: outside OnValidate nothing is written.
            Assert.Empty(Logs(() => Own(new GameObject("Outside"))));
            foreach (var made in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                if (made.name == "MadeInValidate") Object.DestroyImmediate(made);
        }

        // ------------------------------------------------------------------ animator

        RuntimeAnimatorController Controller()
        {
            var file = ObjectGraphBuilder.Build(UnityYaml.Parse(AnimationFixtures.PopupButton), "popup.controller", null, ObjectGraphBuilder.Kind.Asset, null);
            var controller = (RuntimeAnimatorController)file.Objects[9100000];
            controller.m_Parameters.Add(new AnimatorControllerParameter { m_Name = "speed", m_Type = AnimatorControllerParameterType.Float });
            return controller;
        }

        [Fact]
        public void AnInvalidControllerLeavesTheAnimatorUninitialized()
        {
            var go = Own(new GameObject("Override"));
            go.SetActive(false);
            var animator = go.AddComponent<Animator>();
            animator.m_Controller = Own(new AnimatorOverrideController());
            Assert.Equal(new[] { "Warning: The Animator Controller () you have used is not valid. Animations will not play" }, Logs(() => go.SetActive(true)));
            Assert.False(animator.isInitialized);
            const string notInitialized = "Warning: Animator has not been initialized.";
            Assert.Equal(new[] { notInitialized }, Logs(() => animator.SetFloat("speed", 1f)));
            Assert.Equal(new[] { notInitialized }, Logs(() => animator.GetFloat("speed")));
            Assert.Empty(Logs(() => Assert.Equal(0, animator.parameterCount)));
            Assert.Empty(Logs(() => animator.GetCurrentAnimatorStateInfo(0)));
            Assert.Empty(Logs(() => animator.Play("x")));
        }

        [Fact]
        public void AnInactiveAnimatorStillReadsItsControllersDefinition()
        {
            var go = Own(new GameObject("Inactive"));
            go.SetActive(false);
            var animator = go.AddComponent<Animator>();
            animator.runtimeAnimatorController = Controller();
            const string notPlaying = "Warning: Animator is not playing an AnimatorController";
            Assert.Empty(Logs(() => Assert.Equal(1, animator.parameterCount)));
            // Its layers are not read.
            Assert.Equal(new[] { notPlaying }, Logs(() => Assert.Equal(0, animator.layerCount)));
            Assert.Equal(new[] { notPlaying }, Logs(() => animator.SetFloat("speed", 1f)));
            Assert.Equal(new[] { "Warning: Game object with animator is inactive" }, Logs(() => animator.Play("PopupShow")));
            // Control: active, nothing is written and the value is kept.
            go.SetActive(true);
            Assert.Empty(Logs(() => animator.SetFloat("speed", 1f)));
            Assert.Equal(1f, animator.GetFloat("speed"));
        }

        // ------------------------------------------------------------------ missing scripts

        const string MissingMarker = """
%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &11400000
MonoBehaviour:
  m_GameObject: {fileID: 0}
  m_Enabled: 1
  m_Script: {fileID: 11500000, guid: 0f0e0d0c0b0a09080706050403020100, type: 3}
  m_Name: Holder
--- !u!114 &22
MonoBehaviour:
  m_GameObject: {fileID: 0}
  m_Enabled: 1
  m_Script: {fileID: 11500000, guid: 0f0e0d0c0b0a0908070605040302ffff, type: 3}
  m_Name: Missing
""";

        [Fact]
        public void AnAssetObjectWhoseScriptIsMissingComparesEqualToNull()
        {
            var file = ObjectGraphBuilder.Build(UnityYaml.Parse(MissingMarker), "missing.asset", null, ObjectGraphBuilder.Kind.Asset, null);
            var missing = file.Objects[22];
            Assert.False(ReferenceEquals(missing, null));
            Assert.True(missing == null);
            // A reference to it keeps the object (a list keeps its entry).
            Assert.Same(missing, file.Resolve(new ObjectRef(22, null, 0), typeof(ScriptableObject)));
        }

        // ------------------------------------------------------------------ compilation

        [Fact]
        public void CompilerOptionsAndAssembliesKeepWhatTheyAreGiven()
        {
            var defaults = new ScriptCompilerOptions();
            Assert.False(defaults.AllowUnsafeCode);
            Assert.Equal(UnityEditor.ApiCompatibilityLevel.NET_Unity_4_8, defaults.ApiCompatibilityLevel);
            Assert.Equal(CodeOptimization.None, defaults.CodeOptimization);
            Assert.Equal("9.0", defaults.LanguageVersion);
            Assert.Empty(defaults.ResponseFiles);
            Assert.Null(defaults.AdditionalCompilerArguments);
            Assert.Empty(defaults.RoslynAnalyzerDllPaths);
            Assert.Null(defaults.AnalyzerConfigPath);
            Assert.Equal(UnityEditor.EditorAssembliesCompatibilityLevel.Default, defaults.EditorAssembliesCompatibilityLevel);

            var plain = new Assembly("A", "Out/A.dll", new[] { "Assets/A.cs" }, null, null, null, AssemblyFlags.None);
            Assert.Null(plain.defines);
            Assert.Null(plain.assemblyReferences);
            Assert.NotNull(plain.compilerOptions);
            Assert.Equal("", plain.rootNamespace);
            Assert.Empty(plain.allReferences);
            var options = new ScriptCompilerOptions { AllowUnsafeCode = true };
            var full = new Assembly("B", "Out/B.dll", new string[0], new string[0], new Assembly[0], new string[0], AssemblyFlags.EditorAssembly, options, "Ns");
            Assert.Same(options, full.compilerOptions);
            Assert.Equal("Ns", full.rootNamespace);
            Assert.Null(new Assembly("C", "Out/C.dll", new string[0], new string[0], new Assembly[0], new string[0], AssemblyFlags.None, null, null).rootNamespace);
        }

        [Fact]
        public void ScreenResIsTheScreenSize()
        {
            Assert.Equal(Screen.width + "x" + Screen.height, UnityEditor.UnityStats.screenRes);
            Assert.Same(UnityEditor.UnityStats.screenRes, UnityEditor.UnityStats.screenRes);
        }
    }
}
