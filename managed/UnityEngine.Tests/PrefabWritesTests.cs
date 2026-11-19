using System;
using System.Collections.Generic;
using System.IO;
using Unbound.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Xunit;

namespace UnityEngine.Tests
{
    public class PrefabTestHolder : MonoBehaviour
    {
        public int count = 3;
        public string label = "plain";
        public List<string> words = new List<string>();
        public Transform link;
    }

    /// <summary>
    /// Prefab writes as the Unity 6000.0.71f1 Editor does them (tests/projects/scenesave
    /// prefab-writes, prefab-api): overrides applied to the prefab's file and merged into every
    /// loaded instance, each keeping its own overrides; reverts; SetPropertyModifications; a
    /// component added to an instance applied; a prefab asset changed by script saved; and
    /// ReloadScene's argument checks.
    /// </summary>
    [Collection("Frames")]
    public class PrefabWritesTests : IDisposable
    {
        readonly string m_Project = Path.Combine(Path.GetTempPath(), "unbound-prefab-writes-" + Guid.NewGuid().ToString("N"));
        readonly string m_PreviousProject = ProjectInfo.ProjectPath;
        readonly string m_Guid = Guid.NewGuid().ToString("N");
        readonly List<Object> m_Made = new List<Object>();
        readonly LoadedFile m_File;
        readonly GameObject m_Asset;
        readonly GameObject m_AssetPart;

        const string AssetPath = "Assets/Thing.prefab";

        public PrefabWritesTests()
        {
            Directory.CreateDirectory(Path.Combine(m_Project, "Assets"));
            ProjectInfo.ProjectPath = m_Project;
            m_File = new LoadedFile { Path = AssetPath, Guid = m_Guid };
            using (ObjectRegistry.AsAssets())
            {
                m_Asset = AssetObject("Thing", null, 100);
                m_AssetPart = AssetObject("Part", m_Asset.transform, 200);
                var holder = (PrefabTestHolder)AssetComponent(m_Asset, typeof(PrefabTestHolder), 102);
                holder.link = m_AssetPart.transform;
                AssetComponent(m_AssetPart, typeof(BoxCollider), 202);
            }
            m_AssetPart.transform.m_LocalPosition = new Vector3(0f, 1f, 0f);
            m_File.MainObject = m_Asset;
            m_File.Roots.Add(m_Asset);
            AssetDatabaseRuntime.AddLoaded(m_File);
            Assert.True(PrefabAssets.Save(m_File));
        }

        GameObject AssetObject(string name, Transform parent, long id)
        {
            var go = new GameObject(name, bare: true) { m_IsPrefabAsset = true, IsPersistentAsset = true };
            var t = (Transform)ComponentFactory.Create(typeof(Transform), go, deserializing: true);
            t.IsPersistentAsset = true;
            go.m_Transform = t;
            go.m_Components.Add(t);
            if (parent != null)
            {
                t.m_Parent = parent;
                parent.m_Children.Add(t);
            }
            m_File.Objects[id] = go;
            m_File.Objects[id + 1] = t;
            m_Made.Add(go);
            return go;
        }

        Component AssetComponent(GameObject go, Type type, long id)
        {
            var c = ComponentFactory.Create(type, go, deserializing: true);
            c.IsPersistentAsset = true;
            go.m_Components.Add(c);
            m_File.Objects[id] = c;
            return c;
        }

        public void Dispose()
        {
            foreach (var o in m_Made) if (o != null) Object.DestroyImmediate(o, true);
            AssetDatabaseRuntime.Invalidate(m_Guid);
            ProjectInfo.ProjectPath = m_PreviousProject;
            try { Directory.Delete(m_Project, true); } catch (IOException) { }
        }

        GameObject Instance()
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(m_Asset);
            m_Made.Add(go);
            return go;
        }

        string FileText() => File.ReadAllText(Path.Combine(m_Project, AssetPath));

        [Fact]
        public void ValuesAreSetByTheirPropertyPaths()
        {
            var go = new GameObject("paths");
            m_Made.Add(go);
            var holder = go.AddComponent<PrefabTestHolder>();
            var refs = new ValueReferences();
            PrefabValues.Write(go.transform, new[] { ("m_LocalPosition.y", YText.Scalar("2")) }, refs);
            PrefabValues.Write(holder, new[]
            {
                ("words.Array.size", YText.Scalar("2")),
                ("words.Array.data[1]", YText.Scalar("b")),
                ("count", YText.Scalar("9")),
                ("missing.path", YText.Scalar("1")),
            }, refs);
            Assert.Equal(2f, go.transform.localPosition.y);
            Assert.Equal(9, holder.count);
            Assert.Equal(2, holder.words.Count);
            Assert.Equal("b", holder.words[1]);
            // A disabled state goes through the setter.
            PrefabValues.Write(holder, new[] { ("m_Enabled", YText.Scalar("0")) }, refs);
            Assert.False(holder.enabled);
        }

        [Fact]
        public void AnInstancesChangedEngineValuesAreModificationsInTargetOrder()
        {
            var instance = Instance();
            var part = instance.transform.Find("Part");
            part.GetComponent<BoxCollider>().size = new Vector3(2f, 1f, 1f);
            part.localPosition = new Vector3(0f, 3f, 0f);
            var mods = PrefabUtility.GetPropertyModifications(instance);
            var paths = Array.ConvertAll(mods, m => m.propertyPath);
            // The root's pose and name first (targets 101 and 100 order: 100 < 101), then Part's.
            Assert.Equal("m_Name", paths[0]);
            Assert.Contains("m_LocalPosition.y", paths);
            Assert.Contains("m_Size.x", paths);
            Assert.True(Array.IndexOf(paths, "m_Size.x") > Array.LastIndexOf(paths, "m_LocalEulerAnglesHint.z"));
            // A script's change is one once recorded.
            var holder = instance.GetComponent<PrefabTestHolder>();
            holder.count = 8;
            Assert.DoesNotContain("count", Array.ConvertAll(PrefabUtility.GetPropertyModifications(instance), m => m.propertyPath));
            PrefabUtility.RecordPrefabInstancePropertyModifications(holder);
            Assert.Contains("count", Array.ConvertAll(PrefabUtility.GetPropertyModifications(instance), m => m.propertyPath));
        }

        [Fact]
        public void AnAppliedPropertyIsWrittenAndEveryInstanceTakesItButForItsOwnOverride()
        {
            var first = Instance();
            var second = Instance();
            var third = Instance();
            var updated = new List<GameObject>();
            PrefabUtility.PrefabInstanceUpdated callback = go => updated.Add(go);
            PrefabUtility.prefabInstanceUpdated += callback;
            try
            {
                var holder = first.GetComponent<PrefabTestHolder>();
                holder.count = 40;
                holder.label = "first label";
                PrefabUtility.RecordPrefabInstancePropertyModifications(holder);
                // The second overrides the count itself; the third has no override.
                var other = second.GetComponent<PrefabTestHolder>();
                other.count = 7;
                PrefabUtility.RecordPrefabInstancePropertyModifications(other);
                PrefabUtility.ApplyPropertyOverride(new SerializedObject(holder).FindProperty("count"), AssetPath, InteractionMode.AutomatedAction);
                Assert.Equal(40, m_Asset.GetComponent<PrefabTestHolder>().count);
                Assert.Equal("plain", m_Asset.GetComponent<PrefabTestHolder>().label);
                Assert.Contains("count: 40", FileText());
                Assert.Equal(40, holder.count);
                Assert.Equal("first label", holder.label);
                Assert.Equal(7, other.count);
                Assert.Equal(40, third.GetComponent<PrefabTestHolder>().count);
                Assert.Equal(new[] { first, second, third }, updated);
                var paths = Array.ConvertAll(PrefabUtility.GetPropertyModifications(first), m => m.propertyPath);
                Assert.DoesNotContain("count", paths);
                Assert.Contains("label", paths);
                // A reference inside the instance is the prefab's own object in the asset.
                Assert.Same(m_AssetPart.transform, m_Asset.GetComponent<PrefabTestHolder>().link);
                Assert.Same(third.transform.Find("Part"), third.GetComponent<PrefabTestHolder>().link);
            }
            finally { PrefabUtility.prefabInstanceUpdated -= callback; }
        }

        [Fact]
        public void ARevertedInstanceKeepsItsRootsPoseAndName()
        {
            var instance = Instance();
            instance.transform.localPosition = new Vector3(8f, 0f, 0f);
            var part = instance.transform.Find("Part");
            part.localScale = new Vector3(2f, 2f, 2f);
            var holder = instance.GetComponent<PrefabTestHolder>();
            holder.count = 55;
            PrefabUtility.RecordPrefabInstancePropertyModifications(holder);
            PrefabUtility.RevertObjectOverride(part, InteractionMode.AutomatedAction);
            Assert.Equal(Vector3.one, part.localScale);
            Assert.Equal(55, holder.count);
            PrefabUtility.RevertPrefabInstance(instance, InteractionMode.AutomatedAction);
            Assert.Equal(3, holder.count);
            Assert.Equal(new Vector3(8f, 0f, 0f), instance.transform.localPosition);
            Assert.Equal("Thing", instance.name);
            // The prefab is not written by a revert.
            Assert.Contains("count: 3", FileText());
        }

        [Fact]
        public void SetModificationsReplaceTheInstancesOverrides()
        {
            var instance = Instance();
            var holder = instance.GetComponent<PrefabTestHolder>();
            holder.label = "unrecorded";
            var assetHolder = m_Asset.GetComponent<PrefabTestHolder>();
            var mods = new List<PropertyModification>(PrefabUtility.GetPropertyModifications(instance))
            {
                new PropertyModification { target = assetHolder, propertyPath = "count", value = "21" },
                new PropertyModification { target = assetHolder, propertyPath = "link", objectReference = instance.transform },
            };
            PrefabUtility.SetPropertyModifications(instance, mods.ToArray());
            Assert.Equal(21, holder.count);
            Assert.Same(instance.transform, holder.link);
            Assert.Equal("plain", holder.label);
            var stored = PrefabUtility.GetPropertyModifications(instance);
            int count = Array.FindIndex(stored, m => m.propertyPath == "count");
            int name = Array.FindIndex(stored, m => m.propertyPath == "m_Name");
            // Ordered by target: the GameObject (100), its Transform (101), the script (102).
            Assert.True(name < count);
            Assert.Equal("21", stored[count].value);
        }

        [Fact]
        public void AnAppliedAddedComponentIsThePrefabsAndTheInstancesLink()
        {
            var instance = Instance();
            var part = instance.transform.Find("Part").gameObject;
            var sphere = part.AddComponent<SphereCollider>();
            sphere.radius = 3f;
            Assert.True(PrefabUtility.IsAddedComponentOverride(sphere));
            Assert.Single(PrefabUtility.GetAddedComponents(instance));
            PrefabUtility.ApplyAddedComponent(sphere, AssetPath, InteractionMode.AutomatedAction);
            var assetSphere = m_AssetPart.GetComponent<SphereCollider>();
            Assert.NotNull(assetSphere);
            Assert.Equal(3f, assetSphere.radius);
            Assert.True(PrefabUtility.IsPartOfPrefabInstance(sphere));
            Assert.Same(assetSphere, PrefabUtility.GetCorrespondingObjectFromSource(sphere));
            Assert.Contains("m_Radius: 3", FileText());
            Assert.Empty(PrefabUtility.GetAddedComponents(instance));
            // Another instance gets the prefab's new component.
            var other = Instance();
            Assert.NotNull(other.transform.Find("Part").GetComponent<SphereCollider>());
        }

        [Fact]
        public void ARemovedComponentIsListedAndComesBackWhenReverted()
        {
            var instance = Instance();
            var part = instance.transform.Find("Part").gameObject;
            Object.DestroyImmediate(part.GetComponent<BoxCollider>());
            var removed = PrefabUtility.GetRemovedComponents(instance);
            Assert.Single(removed);
            Assert.Same(m_AssetPart.GetComponent<BoxCollider>(), removed[0].assetComponent);
            PrefabUtility.RevertRemovedComponent(part, removed[0].assetComponent, InteractionMode.AutomatedAction);
            Assert.NotNull(part.GetComponent<BoxCollider>());
            Assert.Empty(PrefabUtility.GetRemovedComponents(instance));
        }

        [Fact]
        public void ASavedPrefabAssetsScriptChangeReachesInstancesThatDoNotOverrideIt()
        {
            var plain = Instance();
            var overriding = Instance();
            var own = overriding.GetComponent<PrefabTestHolder>();
            own.label = "own";
            PrefabUtility.RecordPrefabInstancePropertyModifications(own);
            m_Asset.GetComponent<PrefabTestHolder>().label = "from asset";
            PrefabUtility.SavePrefabAsset(m_Asset, out var saved);
            Assert.True(saved);
            Assert.Equal("from asset", plain.GetComponent<PrefabTestHolder>().label);
            Assert.Equal("own", own.label);
            Assert.Contains("label: from asset", FileText());
            Assert.Throws<ArgumentException>(() => PrefabUtility.SavePrefabAsset(m_AssetPart));
        }

        [Fact]
        public void ApplyingOnAPlainObjectOrSavingToABadPathThrowsAsTheEditor()
        {
            var plain = new GameObject("Plain");
            m_Made.Add(plain);
            var e = Assert.Throws<ArgumentException>(() => PrefabUtility.ApplyPrefabInstance(plain, InteractionMode.AutomatedAction));
            Assert.Equal("Calling apply or revert methods on an object which is not part of a Prefab instance is not supported.\nParameter name: prefabInstanceObject",
                MonoExceptions.MessageOf(e));
            Assert.Equal("Given path is not valid: 'Assets/Bad.asset'",
                Assert.Throws<ArgumentException>(() => PrefabUtility.SaveAsPrefabAsset(plain, "Assets/Bad.asset")).Message);
            Assert.Equal("Given path does not exist: 'Assets/Missing/Plain.prefab'",
                Assert.Throws<ArgumentException>(() => PrefabUtility.SaveAsPrefabAsset(plain, "Assets/Missing/Plain.prefab")).Message);
            Assert.Equal("Value cannot be null.\nParameter name: Parameter root is null",
                MonoExceptions.MessageOf(Assert.Throws<ArgumentNullException>(() => PrefabUtility.SaveAsPrefabAsset(null, AssetPath))));
            Assert.Equal("Specified object is not part of Prefab contents",
                Assert.Throws<ArgumentException>(() => PrefabUtility.UnloadPrefabContents(plain)).Message);
        }

        [Fact]
        public void SavedContentsAreThePrefabAndUnloadedContentsAreGone()
        {
            var instance = Instance();
            var contents = PrefabUtility.LoadPrefabContents(AssetPath);
            Assert.True(EditorSceneManager.IsPreviewScene(contents.scene));
            Assert.False(PrefabUtility.IsPartOfPrefabAsset(contents));
            contents.GetComponent<PrefabTestHolder>().label = "from contents";
            Assert.Same(m_Asset, PrefabUtility.SaveAsPrefabAsset(contents, AssetPath));
            PrefabUtility.UnloadPrefabContents(contents);
            Assert.True(contents == null);
            Assert.Equal("from contents", m_Asset.GetComponent<PrefabTestHolder>().label);
            Assert.Equal("from contents", instance.GetComponent<PrefabTestHolder>().label);
            Assert.Contains("label: from contents", FileText());
        }

        [Fact]
        public void ReloadSceneRefusesUntitledAndInvalidScenes()
        {
            var reload = typeof(EditorSceneManager).GetMethod("ReloadScene", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.False((bool)reload.Invoke(null, new object[] { default(Scene) }));
            var untitled = new GameObject("untitled").scene;
            m_Made.Add(SceneManager.GetActiveScene().GetRootGameObjects()[^1]);
            if (string.IsNullOrEmpty(untitled.path))
            {
                var e = Assert.Throws<System.Reflection.TargetInvocationException>(() => reload.Invoke(null, new object[] { untitled }));
                Assert.IsType<ArgumentException>(e.InnerException);
                Assert.StartsWith("ReloadScene cannot be used with a scene that haven't been saved yet: ", e.InnerException.Message);
            }
        }
    }
}
