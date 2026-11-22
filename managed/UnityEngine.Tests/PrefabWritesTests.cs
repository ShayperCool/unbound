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
        // Prefab writes are measured for 6000.0 only (other families refuse them): the whole class,
        // its setup's prefab save included, runs as that release.
        readonly IDisposable m_Release = TestRelease.Pin(TestRelease.Measured);
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
            m_Release.Dispose();
        }

        GameObject Instance()
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(m_Asset);
            m_Made.Add(go);
            return go;
        }

        string FileText() => File.ReadAllText(Path.Combine(m_Project, AssetPath));

        static string PositionX(GameObject instance) =>
            Array.Find(PrefabUtility.GetPropertyModifications(instance), m => m.propertyPath == "m_LocalPosition.x" && m.target is Transform)?.value;

        /// <summary>GetPropertyModifications in Edit mode answers a Transform's change once an update
        /// has passed (its pose at the last update until then), and at once after a record, a later
        /// change in the same update answering the recorded pose (tests/projects/scenesave
        /// modification-timing; record-then-change follows the Scripting API's "record those changes").</summary>
        [Fact]
        public void ModificationsTakeATransformsChangeAtTheNextUpdateOrARecord()
        {
            bool playing = EditorSession.IsPlaying;
            EditorSession.IsPlaying = false;
            try { TakeAtTheNextUpdateOrARecord(); }
            finally { EditorSession.IsPlaying = playing; }
        }

        /// <summary>An Undo flush of several objects of one instance, and an apply of a SerializedObject
        /// over several of them, record the instance once (one walk of its objects, not one per object).</summary>
        [Fact]
        public void AFlushAndAnApplyRecordEachInstanceOnce()
        {
            var instance = Instance();
            var root = instance.transform;
            var part = instance.transform.Find("Part");
            var targets = new Object[] { root, part, instance, part.gameObject };
            Undo.RecordObjects(targets, "Move");
            root.localPosition = new Vector3(1f, 0f, 0f);
            part.localPosition = new Vector3(0f, 1f, 0f);
            int before = PrefabInstances.Walks;
            Undo.FlushUndoRecordObjects();
            Assert.Equal(before + 1, PrefabInstances.Walks);

            var so = new SerializedObject(new Object[] { root, part });
            so.FindProperty("m_LocalPosition.z").floatValue = 2f;
            before = PrefabInstances.Walks;
            so.ApplyModifiedPropertiesWithoutUndo();
            Assert.Equal(before + 1, PrefabInstances.Walks);
        }

        void TakeAtTheNextUpdateOrARecord()
        {
            var instance = Instance();
            var t = instance.transform;
            PrefabRecordTiming.FrameBoundary();
            t.localPosition = new Vector3(4f, 0f, 0f);
            Assert.Equal("0", PositionX(instance));
            PrefabRecordTiming.FrameBoundary();
            Assert.Equal("4", PositionX(instance));
            t.localPosition = new Vector3(5f, 0f, 0f);
            Assert.Equal("4", PositionX(instance));
            PrefabRecordTiming.FrameBoundary();
            Assert.Equal("5", PositionX(instance));

            t.localPosition = new Vector3(6f, 0f, 0f);
            PrefabUtility.RecordPrefabInstancePropertyModifications(t);
            Assert.Equal("6", PositionX(instance));
            t.localPosition = new Vector3(7f, 0f, 0f);
            Assert.Equal("6", PositionX(instance));
            PrefabRecordTiming.FrameBoundary();
            Assert.Equal("7", PositionX(instance));
        }

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
        public void ContentsLiveInASceneNamedAfterThePrefabAndClosingItUnloadsThem()
        {
            int previews = EditorSceneManager.previewSceneCount;
            var contents = PrefabUtility.LoadPrefabContents(AssetPath);
            var scene = contents.scene;
            Assert.Equal((System.IO.Path.GetFileNameWithoutExtension(AssetPath), AssetPath, true, 1), (scene.name, scene.path, scene.isLoaded, scene.rootCount));
            Assert.Equal(previews + 1, EditorSceneManager.previewSceneCount);
            Assert.True(EditorSceneManager.ClosePreviewScene(scene));
            Assert.True(contents == null);
            Assert.False(scene.IsValid());
            Assert.Equal(previews, EditorSceneManager.previewSceneCount);
            // They are contents no longer.
            Assert.Equal("Specified object is not part of Prefab contents",
                Assert.Throws<ArgumentException>(() => PrefabUtility.UnloadPrefabContents(contents)).Message);
        }

        [Fact]
        public void TheEditScopeSavesItsContentsToThePrefabAndUnloadsThem()
        {
            int previews = EditorSceneManager.previewSceneCount;
            GameObject root;
            using (var scope = new PrefabUtility.EditPrefabContentsScope(AssetPath))
            {
                root = scope.prefabContentsRoot;
                Assert.Equal(AssetPath, scope.assetPath);
                Assert.True(EditorSceneManager.IsPreviewScene(root.scene));
                root.GetComponent<PrefabTestHolder>().label = "from the scope";
            }
            Assert.True(root == null);
            Assert.Equal(previews, EditorSceneManager.previewSceneCount);
            Assert.Equal("from the scope", m_Asset.GetComponent<PrefabTestHolder>().label);
            Assert.Contains("label: from the scope", FileText());
        }

        /// <summary>A new prefab's file is a copy of the hierarchy: the root named after the file,
        /// references inside it to the copies, to a scene object outside it as none, a random id
        /// per object; the hierarchy itself stays a plain one with its name.</summary>
        [Fact]
        public void ANewPrefabIsACopyOfThePlainHierarchyNamedAfterItsFile()
        {
            var made = new GameObject("Made");
            var part = new GameObject("Made Part");
            var outside = new GameObject("Outside");
            m_Made.Add(made);
            m_Made.Add(outside);
            part.transform.SetParent(made.transform, false);
            var holder = made.AddComponent<PrefabTestHolder>();
            holder.link = part.transform;
            holder.label = "made";
            part.AddComponent<SphereCollider>().radius = 2f;
            var second = made.AddComponent<PrefabTestHolder>();
            second.link = outside.transform;
            var text = PrefabAssets.NewPrefabText("Assets/Other.prefab", made);
            var docs = UnityYamlText.Parse(text);
            Assert.Equal(7, docs.Count);
            Assert.Equal(7, new HashSet<long>(docs.ConvertAll(d => d.FileId)).Count);
            Assert.DoesNotContain(0L, docs.ConvertAll(d => d.FileId));
            var names = docs.FindAll(d => d.ClassName == "GameObject").ConvertAll(d => d.Body.Get("m_Name").Text);
            Assert.Contains("Other", names);
            Assert.Contains("Made Part", names);
            var partTransform = docs.Find(d => d.ClassName == "Transform" && d.Body.Get("m_Father").Get("fileID").Text != "0");
            var scripts = docs.FindAll(d => d.ClassName == "MonoBehaviour");
            Assert.Equal("{fileID: " + partTransform.FileId + "}", scripts.Find(d => d.Body.Get("label").Text == "made").Body.Get("link").Raw);
            Assert.Equal("{fileID: 0}", scripts.Find(d => d.Body.Get("label").Text == "plain").Body.Get("link").Raw);
            Assert.Contains("m_Radius: 2", text);
            Assert.Equal("Made", made.name);
            Assert.False(PrefabUtility.IsPartOfAnyPrefab(made));
            Assert.False(made.IsPersistentAsset);
        }

        /// <summary>A new variant's file is the instance's PrefabInstance alone, with no parent and
        /// its root named after the file; the instance keeps its name and its document's id.</summary>
        [Fact]
        public void ANewVariantIsTheInstancesModificationsWithTheRootNamedAfterItsFile()
        {
            var parent = new GameObject("Parent");
            m_Made.Add(parent);
            var instance = Instance();
            instance.transform.SetParent(parent.transform, false);
            instance.transform.localPosition = new Vector3(8f, 0f, 0f);
            var data = PrefabLinks.Of(instance).Instance;
            data.FileId = 55;
            var text = PrefabAssets.NewPrefabText("Assets/ThingVariant.prefab", instance);
            var docs = UnityYamlText.Parse(text);
            var doc = Assert.Single(docs);
            Assert.Equal("PrefabInstance", doc.ClassName);
            Assert.NotEqual(55L, doc.FileId);
            var modification = doc.Body.Get("m_Modification");
            Assert.Equal("{fileID: 0}", modification.Get("m_TransformParent").Raw);
            var mods = modification.Get("m_Modifications").Items;
            var name = mods.Find(m => m.Get("propertyPath").Text == "m_Name");
            Assert.Equal("ThingVariant", name.Get("value").Text);
            Assert.Equal("{fileID: 100, guid: " + m_Guid + ", type: 3}", name.Get("target").Raw);
            Assert.Equal("8", mods.Find(m => m.Get("propertyPath").Text == "m_LocalPosition.x").Get("value").Text);
            Assert.Equal("{fileID: 100100000, guid: " + m_Guid + ", type: 3}", doc.Body.Get("m_SourcePrefab").Raw);
            Assert.Equal("Thing", instance.name);
            Assert.Equal(55L, data.FileId);
        }

        /// <summary>A hierarchy shaped like a prefab and connected to it keeps its values: a script's
        /// reference that differs from the prefab's is a stored modification (create-assets
        /// outside.*). Afterwards, as Unity documents for RecordPrefabInstancePropertyModifications,
        /// a script change is no modification until recorded (control: once recorded it is).</summary>
        [Fact]
        public void AConnectedHierarchyStoresItsDifferencesAndLaterScriptChangesNeedARecord()
        {
            var outside = new GameObject("Outside");
            var plain = new GameObject("Thing");
            m_Made.Add(outside);
            m_Made.Add(plain);
            var part = new GameObject("Part");
            part.transform.SetParent(plain.transform, false);
            part.transform.localPosition = new Vector3(0f, 1f, 0f);
            part.AddComponent<BoxCollider>();
            var holder = plain.AddComponent<PrefabTestHolder>();
            holder.link = outside.transform;
            PrefabUtility.Connect(plain, m_Asset, InteractionMode.AutomatedAction);
            Assert.True(PrefabUtility.IsOutermostPrefabInstanceRoot(plain));
            Assert.Same(outside.transform, holder.link);
            string Paths() => string.Join(",", Array.ConvertAll(PrefabUtility.GetPropertyModifications(plain), m => m.propertyPath));
            var link = Array.Find(PrefabUtility.GetPropertyModifications(plain), m => m.propertyPath == "link");
            Assert.NotNull(link);
            Assert.Same(outside.transform, link.objectReference);
            Assert.DoesNotContain("label", Paths().Split(','));
            holder.label = "later";
            Assert.DoesNotContain("label", Paths().Split(','));
            PrefabUtility.RecordPrefabInstancePropertyModifications(holder);
            Assert.Contains("label", Paths().Split(','));
        }

        /// <summary>A new prefab holding an instance writes it as a new instance of the file: its
        /// PrefabInstance under the outer Transform with its overrides, the added component and
        /// GameObject listed and written, the instance keeping its own document id (prefab-nested
        /// Outer.prefab); a variant lists its removed component (RichVariant.prefab).</summary>
        [Fact]
        public void NewPrefabsWriteTheInstancesTheyHold()
        {
            var outer = new GameObject("Outer");
            m_Made.Add(outer);
            var nested = Instance();
            nested.transform.SetParent(outer.transform, false);
            nested.transform.localPosition = new Vector3(1f, 2f, 3f);
            nested.AddComponent<SphereCollider>().radius = 4f;
            var extra = new GameObject("Extra");
            extra.transform.SetParent(nested.transform, false);
            var data = PrefabLinks.Of(nested).Instance;
            data.FileId = 77;
            var docs = UnityYamlText.Parse(PrefabAssets.NewPrefabText("Assets/Outer.prefab", outer));
            var instance = Assert.Single(docs, d => d.ClassName == "PrefabInstance");
            Assert.NotEqual(77L, instance.FileId);
            Assert.Equal(77L, data.FileId);
            var outerTransform = docs.Find(d => d.ClassName == "Transform" && d.Body.Get("m_Father").Raw == "{fileID: 0}");
            var modification = instance.Body.Get("m_Modification");
            Assert.Equal("{fileID: " + outerTransform.FileId + "}", modification.Get("m_TransformParent").Raw);
            var addedComponent = Assert.Single(modification.Get("m_AddedComponents").Items);
            var sphere = docs.Find(d => d.ClassName == "SphereCollider");
            Assert.Equal("{fileID: " + sphere.FileId + "}", addedComponent.Get("addedObject").Raw);
            var addedObject = Assert.Single(modification.Get("m_AddedGameObjects").Items);
            var extraTransform = docs.Find(d => d.ClassName == "Transform" && d.FileId == SceneFiles.ParseFileId(addedObject.Get("addedObject")));
            var stand = docs.Find(d => d.FileId == SceneFiles.ParseFileId(extraTransform.Body.Get("m_Father")));
            Assert.EndsWith(" stripped", stand.Header);
            Assert.Equal("Outer", docs.Find(d => d.ClassName == "GameObject" && !d.Header.EndsWith(" stripped") && d.Body.Get("m_Name").Text != "Extra").Body.Get("m_Name").Text);

            var variantSource = Instance();
            Object.DestroyImmediate(variantSource.transform.Find("Part").GetComponent<BoxCollider>());
            var variant = UnityYamlText.Parse(PrefabAssets.NewPrefabText("Assets/RichVariant.prefab", variantSource));
            var removed = Assert.Single(Assert.Single(variant, d => d.ClassName == "PrefabInstance").Body.Get("m_Modification").Get("m_RemovedComponents").Items);
            Assert.Equal("{fileID: 202, guid: " + m_Guid + ", type: 3}", removed.Raw);
        }

        sealed class Errors : ILogHandler
        {
            internal readonly List<string> Lines = new List<string>();
            public void LogException(Exception exception, Object context) => Lines.Add(exception.Message);
            public void LogFormat(LogType logType, Object context, string format, params object[] args) => Lines.Add(string.Format(format, args));
        }

        /// <summary>A save keeps the file's entry of a GameObject added to an instance (its place:
        /// insertIndex 0 stays) while it stays where it was; one moved to another of the instance's
        /// objects, or among the prefab's children with an entry after them, fails the save with
        /// its reason.</summary>
        [Fact]
        public void ASaveKeepsAnAddedObjectsPlaceAndRefusesAMovedOne()
        {
            var holder = new GameObject("Holder");
            m_Made.Add(holder);
            var instance = Instance();
            instance.transform.SetParent(holder.transform, false);
            var added = new GameObject("Added");
            added.transform.SetParent(instance.transform, false);
            var target = new SceneFiles.WriteTarget
            {
                Path = "Assets/Holder.prefab",
                FileIds = new Dictionary<Object, long>(ReferenceEqualityComparer.Instance),
                StrippedIds = new Dictionary<Object, long>(ReferenceEqualityComparer.Instance),
                Roots = new List<GameObject> { holder },
            };
            PrefabLinks.Of(instance).Instance.FileId = 0;
            var first = SceneFiles.Write(target);
            Assert.Contains("    - targetCorrespondingSourceObject: {fileID: 101, guid: " + m_Guid + ", type: 3}\n      insertIndex: -1\n", first);
            var file = Path.Combine(m_Project, "Assets", "Holder.prefab");
            File.WriteAllText(file, first.Replace("insertIndex: -1", "insertIndex: 0"));
            // At index 0, where a load places the entry.
            added.transform.SetSiblingIndex(0);
            var kept = SceneFiles.Write(target);
            Assert.Contains("      insertIndex: 0\n", kept);
            Assert.DoesNotContain("insertIndex: -1", kept);

            var errors = new Errors();
            var previous = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = errors;
            try
            {
                added.transform.SetParent(instance.transform.Find("Part"), false);
                Assert.Null(SceneFiles.Write(target));
                Assert.Contains(errors.Lines, l => l.Contains("GameObjects added to a prefab instance and moved"));
                added.transform.SetParent(instance.transform, false);
                File.WriteAllText(file, first);
                added.transform.SetSiblingIndex(0);
                errors.Lines.Clear();
                Assert.Null(SceneFiles.Write(target));
                Assert.Contains(errors.Lines, l => l.Contains("GameObjects added to a prefab instance and moved"));

                // Two added after the prefab's children, then swapped: a load would place them in the
                // file's order, so the save is refused.
                added.transform.SetAsLastSibling();
                var second = new GameObject("Second");
                second.transform.SetParent(instance.transform, false);
                errors.Lines.Clear();
                var both = SceneFiles.Write(target);
                Assert.NotNull(both);
                File.WriteAllText(file, both);
                second.transform.SetSiblingIndex(added.transform.GetSiblingIndex());
                Assert.Null(SceneFiles.Write(target));
                Assert.Contains(errors.Lines, l => l.Contains("GameObjects added to a prefab instance and reordered"));
            }
            finally
            {
                Debug.unityLogger.logHandler = previous;
            }
        }

        /// <summary>An instance's file entry for another instance placed under its object (that
        /// instance's root Transform) stays through a save while that instance is still there; moved
        /// away, the save is refused with the reason (its new place is not measured).</summary>
        [Fact]
        public void ASaveKeepsTheEntryOfAnInstancePlacedUnderAnother()
        {
            var holder = new GameObject("Holder");
            m_Made.Add(holder);
            var outer = Instance();
            outer.transform.SetParent(holder.transform, false);
            var inner = Instance();
            inner.transform.SetParent(holder.transform, false);
            var target = new SceneFiles.WriteTarget
            {
                Path = "Assets/Holder.prefab",
                FileIds = new Dictionary<Object, long>(ReferenceEqualityComparer.Instance),
                StrippedIds = new Dictionary<Object, long>(ReferenceEqualityComparer.Instance),
                Roots = new List<GameObject> { holder },
            };
            var outerData = PrefabLinks.Of(outer).Instance;
            outerData.FileId = 0;
            PrefabLinks.Of(inner).Instance.FileId = 0;
            var docs = UnityYamlText.Parse(SceneFiles.Write(target));
            // The file lists the inner instance's root Transform (a stand-in) under the outer instance.
            const long InnerRoot = 999;
            target.FileIds[inner.transform] = InnerRoot;
            var outerDoc = docs.Find(d => d.FileId == outerData.FileId);
            var entry = YText.Map();
            entry.Add("targetCorrespondingSourceObject", SceneFiles.FlowValue("{fileID: 101, guid: " + m_Guid + ", type: 3}"));
            entry.Add("insertIndex", YText.Scalar("-1"));
            entry.Add("addedObject", SceneFiles.FlowValue("{fileID: " + InnerRoot + "}"));
            var list = YText.Seq();
            list.Items.Add(entry);
            outerDoc.Body.Get("m_Modification").Set("m_AddedGameObjects", list);
            var stand = new YTextDocument { Header = "--- !u!4 &" + InnerRoot + " stripped", ClassId = 4, FileId = InnerRoot, ClassName = "Transform", Body = YText.Map() };
            docs.Add(stand);
            var file = Path.Combine(m_Project, "Assets", "Holder.prefab");
            File.WriteAllText(file, UnityYamlText.Write(docs));
            inner.transform.SetParent(outer.transform, false);
            Assert.Contains("addedObject: {fileID: " + InnerRoot + "}", SceneFiles.Write(target));

            var errors = new Errors();
            var previous = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = errors;
            try
            {
                inner.transform.SetParent(holder.transform, false);
                Assert.Null(SceneFiles.Write(target));
                Assert.Contains(errors.Lines, l => l.Contains("an instance placed under a prefab instance's object and moved"));
            }
            finally
            {
                Debug.unityLogger.logHandler = previous;
            }
        }

        /// <summary>New prefabs whose instances are not measured are refused or not written: an
        /// instance under another instance's object, an instance whose file lists removed
        /// GameObjects, a GameObject added among an instance's own children.</summary>
        [Fact]
        public void NewPrefabsWithUnmeasuredInstancesAreRefused()
        {
            var outer = new GameObject("Outer");
            m_Made.Add(outer);
            var first = Instance();
            first.transform.SetParent(outer.transform, false);
            var second = Instance();
            second.transform.SetParent(first.transform.Find("Part"), false);
            Assert.StartsWith("Unbound does not make new prefabs holding an instance under another instance's object yet",
                Assert.Throws<NotSupportedException>(() => PrefabAssets.NewPrefabText("Assets/Outer.prefab", outer)).Message);
            second.transform.SetParent(outer.transform, false);
            PrefabLinks.Of(second).Instance.RemovedGameObjects = 1;
            Assert.StartsWith("Unbound does not make new prefabs of instances with removed GameObjects yet",
                Assert.Throws<NotSupportedException>(() => PrefabAssets.NewPrefabText("Assets/Outer.prefab", outer)).Message);
            PrefabLinks.Of(second).Instance.RemovedGameObjects = 0;
            var early = new GameObject("Early");
            early.transform.SetParent(first.transform, false);
            early.transform.SetSiblingIndex(0);
            var errors = new Errors();
            var previous = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = errors;
            try { Assert.Null(PrefabAssets.NewPrefabText("Assets/Outer.prefab", outer)); }
            finally { Debug.unityLogger.logHandler = previous; }
            Assert.Contains(errors.Lines, l => l.Contains("GameObjects added among a prefab instance's own"));
            early.transform.SetSiblingIndex(first.transform.childCount - 1);
            Assert.NotNull(PrefabAssets.NewPrefabText("Assets/Outer.prefab", outer));
        }

        /// <summary>Unity's argument checks (part of an instance, a folder at the path, a null path,
        /// connecting under StreamingAssets or a persistent object), and new prefabs that are not
        /// measured failing loudly before anything is written: a variant of an instance holding another
        /// instance, connecting an instance or onto a prefab that exists.</summary>
        [Fact]
        public void UnmeasuredNewPrefabsAreRefused()
        {
            // An asset listing without the new paths (the engine's is not connected in tests).
            AssetDatabase.List("[]");
            try { RefuseUnmeasuredNewPrefabs(); }
            finally { AssetDatabase.Invalidate(); }
        }

        void RefuseUnmeasuredNewPrefabs()
        {
            var plain = new GameObject("Plain");
            m_Made.Add(plain);
            // A variant of an instance holding another instance is not measured.
            var holding = Instance();
            Instance().transform.SetParent(holding.transform, false);
            Assert.StartsWith("Unbound does not make new prefab variants of instances holding other prefab instances yet",
                Assert.Throws<NotSupportedException>(() => PrefabUtility.SaveAsPrefabAsset(holding, "Assets/Variant.prefab")).Message);
            // Unity's own argument checks, before the path's (its PrefabUtility C#).
            var part = Instance().transform.Find("Part").gameObject;
            Assert.Equal("Can't save part of a Prefab instance as a Prefab",
                Assert.Throws<ArgumentException>(() => PrefabUtility.SaveAsPrefabAsset(part, "Assets/Bad.asset")).Message);
            Assert.Equal("Can't save part of a Prefab instance as a Prefab",
                Assert.Throws<ArgumentException>(() => PrefabUtility.SaveAsPrefabAssetAndConnect(part, "Assets/Part.prefab", InteractionMode.AutomatedAction)).Message);
            Directory.CreateDirectory(Path.Combine(m_Project, "Assets/Folder.prefab"));
            Assert.Equal("Overwriting a folder with an Asset is not allowed: 'Assets/Folder.prefab'",
                Assert.Throws<ArgumentException>(() => PrefabUtility.SaveAsPrefabAsset(plain, "Assets/Folder.prefab")).Message);
            Assert.Equal("Value cannot be null.\nParameter name: path is null or empty",
                MonoExceptions.MessageOf(Assert.Throws<ArgumentNullException>(() => PrefabUtility.SaveAsPrefabAsset(plain, null))));
            Directory.CreateDirectory(Path.Combine(m_Project, "Assets/StreamingAssets"));
            var streamed = new GameObject("Streamed");
            m_Made.Add(streamed);
            Assert.Equal("Can't connect a Prefab in the StreamingAssets folder to GameObjects in the scene. To save a Prefab to the StreamingAssets folder use SaveAsPrefabAsset instead.",
                Assert.Throws<ArgumentException>(() => PrefabUtility.SaveAsPrefabAssetAndConnect(streamed, "Assets/StreamingAssets/Streamed.prefab", InteractionMode.AutomatedAction)).Message);
            // An asset folder of a registry package is immutable; a package the project does not have
            // is no asset folder, so its path does not exist.
            var packages = new List<UnityEditor.PackageManager.PackageInfo>(UnityEditor.PackageManager.PackageInfo.Registered);
            UnityEditor.PackageManager.PackageInfo.Register(new[] { UnityEditor.PackageManager.PackageInfo.Create("com.unity.ugui", "2.0.0",
                UnityEditor.PackageManager.PackageSource.Registry, "/cache/com.unity.ugui", null, null, null, null, null, true) });
            AssetDatabase.List("[{\"guid\":\"0000000000000000000000000000000c\",\"path\":\"Packages/com.unity.ugui/Runtime\",\"kind\":\"folder\"}]");
            try
            {
                Assert.Equal("Saving Prefab to immutable folder is not allowed: 'Packages/com.unity.ugui/Runtime/X.prefab'",
                    Assert.Throws<ArgumentException>(() => PrefabUtility.SaveAsPrefabAsset(plain, "Packages/com.unity.ugui/Runtime/X.prefab")).Message);
                Assert.Equal("Saving Prefab to immutable folder is not allowed: 'Packages/com.unity.ugui/X.prefab'",
                    Assert.Throws<ArgumentException>(() => PrefabUtility.SaveAsPrefabAsset(plain, "Packages/com.unity.ugui/X.prefab")).Message);
                Assert.Equal("Given path does not exist: 'Packages/com.unknown/X.prefab'",
                    Assert.Throws<ArgumentException>(() => PrefabUtility.SaveAsPrefabAsset(plain, "Packages/com.unknown/X.prefab")).Message);
            }
            finally
            {
                UnityEditor.PackageManager.PackageInfo.Register(packages);
                AssetDatabase.List("[]");
            }
            Assert.Equal("Can't save persistent Objects and connect them to the saved Prefab",
                Assert.Throws<ArgumentException>(() => PrefabUtility.SaveAsPrefabAssetAndConnect(m_Asset, "Assets/Copy.prefab", InteractionMode.AutomatedAction)).Message);
            var fresh = new GameObject("Fresh");
            m_Made.Add(fresh);
            Assert.Throws<NotSupportedException>(() => PrefabUtility.SaveAsPrefabAssetAndConnect(fresh, AssetPath, InteractionMode.AutomatedAction));
            Assert.Throws<NotSupportedException>(() => PrefabUtility.SaveAsPrefabAssetAndConnect(Instance(), "Assets/Connected.prefab", InteractionMode.AutomatedAction));
#pragma warning disable CS0618
            // CreatePrefab takes SaveAsPrefabAsset's checks.
            Assert.Equal("Given path is not valid: 'Assets/Bad.asset'",
                Assert.Throws<ArgumentException>(() => PrefabUtility.CreatePrefab("Assets/Bad.asset", fresh)).Message);
            Assert.Equal("Given path is not valid: 'Assets/Bad.asset'",
                Assert.Throws<ArgumentException>(() => PrefabUtility.CreatePrefab("Assets/Bad.asset", fresh, ReplacePrefabOptions.ConnectToPrefab)).Message);
#pragma warning restore CS0618
            Assert.False(File.Exists(Path.Combine(m_Project, "Assets/Variant.prefab")));
            Assert.True(fresh.name == "Fresh" && !PrefabUtility.IsPartOfAnyPrefab(fresh));
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
