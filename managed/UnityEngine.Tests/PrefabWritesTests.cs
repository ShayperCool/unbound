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
            foreach (var f in m_Dependents) AssetDatabaseRuntime.Invalidate(f.Guid);
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

        /// <summary>In Play mode a recorded script's overrides are those of its last record: a
        /// property changed since is listed once recorded again (tests/projects/animatorapi
        /// modify.overridden.*).</summary>
        [Fact]
        public void ARecordedScriptListsALaterChangeOnceRecordedAgain()
        {
            bool playing = EditorSession.IsPlaying;
            EditorSession.IsPlaying = true;
            try
            {
                var instance = Instance();
                var holder = instance.GetComponent<PrefabTestHolder>();
                string[] Paths() => Array.ConvertAll(PrefabUtility.GetPropertyModifications(instance), m => m.propertyPath);
                holder.count = 8;
                PrefabUtility.RecordPrefabInstancePropertyModifications(holder);
                Assert.Contains("count", Paths());
                holder.label = "later";
                Assert.DoesNotContain("label", Paths());
                PrefabUtility.RecordPrefabInstancePropertyModifications(holder);
                Assert.Contains("label", Paths());
            }
            finally
            {
                EditorSession.IsPlaying = playing;
            }
        }

        static string ValueOf(GameObject instance, string path)
        {
            foreach (var m in PrefabUtility.GetPropertyModifications(instance))
                if (m.propertyPath == path) return m.value;
            return null;
        }

        /// <summary>A stored modification set back to the prefab's value and recorded lists the
        /// recorded value, as before records were kept.</summary>
        [Fact]
        public void AStoredModificationTakesTheRecordedValue()
        {
            bool playing = EditorSession.IsPlaying;
            EditorSession.IsPlaying = true;
            try
            {
                var instance = Instance();
                var holder = instance.GetComponent<PrefabTestHolder>();
                // A stored modification (as an instance read from a scene file has).
                PrefabUtility.SetPropertyModifications(instance, new[]
                {
                    new PropertyModification { target = m_Asset.GetComponent<PrefabTestHolder>(), propertyPath = "count", value = "5" },
                });
                Assert.Equal("5", ValueOf(instance, "count"));
                holder.count = 3;
                PrefabUtility.RecordPrefabInstancePropertyModifications(holder);
                Assert.Equal("3", ValueOf(instance, "count"));
            }
            finally
            {
                EditorSession.IsPlaying = playing;
            }
        }

        /// <summary>An engine write to a recorded script (a revert, set modifications, an Undo)
        /// drops the values its record kept: a removed override is not listed from the record.</summary>
        [Fact]
        public void AnEngineWriteDropsARecordsValues()
        {
            bool playing = EditorSession.IsPlaying;
            EditorSession.IsPlaying = true;
            try
            {
                var instance = Instance();
                var holder = instance.GetComponent<PrefabTestHolder>();
                holder.count = 8;
                PrefabUtility.RecordPrefabInstancePropertyModifications(holder);
                Assert.Equal("8", ValueOf(instance, "count"));
                PrefabUtility.RevertPropertyOverride(new SerializedObject(holder).FindProperty("count"), InteractionMode.AutomatedAction);
                Assert.Equal(3, holder.count);
                Assert.Null(ValueOf(instance, "count"));
                holder.count = 9;
                PrefabUtility.RecordPrefabInstancePropertyModifications(holder);
                Assert.Equal("9", ValueOf(instance, "count"));

                PrefabUtility.SetPropertyModifications(instance, new PropertyModification[0]);
                Assert.Equal(3, holder.count);
                Assert.Null(ValueOf(instance, "count"));

                Undo.RecordObject(holder, "count");
                holder.count = 7;
                PrefabUtility.RecordPrefabInstancePropertyModifications(holder);
                Assert.Equal("7", ValueOf(instance, "count"));
                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Assert.Equal(3, holder.count);
                Assert.Null(ValueOf(instance, "count"));
            }
            finally
            {
                EditorSession.IsPlaying = playing;
            }
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

        /// <summary>A dirty prefab asset is written by SaveAssetIfDirty of any of its objects and
        /// by SaveAssets, and is then no longer dirty; the Editor's exit save leaves it dirty and
        /// unwritten; a script's field set alone writes nothing (matches the Unity 6000.0.71f1
        /// Editor, tests/projects/scenesave prefab-dirty-saves single.*, fields.*, exit.*,
        /// unmarked.*).</summary>
        [Fact]
        public void ADirtyPrefabAssetIsWrittenBySavesAndNotAtExit()
        {
            var holder = m_Asset.GetComponent<PrefabTestHolder>();
            holder.label = "unmarked";
            AssetDatabase.SaveAssets();
            Assert.DoesNotContain("label: unmarked", FileText());
            holder.label = "at exit";
            EditorUtility.SetDirty(holder);
            AssetFiles.SaveDirty(exit: true);
            Assert.DoesNotContain("label: at exit", FileText());
            Assert.True(EditorUtility.IsDirty(holder));
            holder.label = "saved alone";
            AssetDatabase.SaveAssetIfDirty(m_Asset);
            Assert.Contains("label: saved alone", FileText());
            Assert.False(EditorUtility.IsDirty(holder));
            holder.label = "saved";
            EditorUtility.SetDirty(holder);
            AssetDatabase.SaveAssets();
            Assert.Contains("label: saved", FileText());
            Assert.False(EditorUtility.IsDirty(holder));
            // SavePrefabAsset writes it clean too: a SaveAssets after it has nothing to write.
            holder.label = "saved by the prefab API";
            EditorUtility.SetDirty(holder);
            PrefabUtility.SavePrefabAsset(m_Asset);
            Assert.False(EditorUtility.IsDirty(holder));
        }

        /// <summary>The objects OnPostprocessPrefab adds get DontSaveInEditor (the 6000.0 Scripting
        /// API, AssetPostprocessor.OnPostprocessPrefab), keeping their other flags, and what the
        /// callbacks mark dirty is the import's: a later save does not write the import's result
        /// back into the prefab's source.</summary>
        [Fact]
        public void APrefabPostprocessorsAdditionsAreNotSavedIntoItsSource()
        {
            var hook = (0, typeof(AddingPostprocessor), typeof(AddingPostprocessor).GetMethod("Add", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic));
            var importer = AssetImporter.FromMeta(AssetPath, m_Guid, "PrefabImporter", TestYaml.DocumentBody("PrefabImporter:\n  userData: \n"));
            using (AssetPostprocessing.HooksForTests("OnPreprocessAsset", new List<(int, Type, System.Reflection.MethodInfo)>()))
            using (AssetPostprocessing.HooksForTests("OnPostprocessPrefab", new List<(int, Type, System.Reflection.MethodInfo)> { hook }))
            using (AssetImporter.Importing(AssetPath, importer))
                HierarchyPostprocessing.Run(m_File, model: false);
            var added = m_Asset.GetComponent<UnityEngine.SphereCollider>();
            Assert.NotNull(added);
            Assert.Equal(HideFlags.DontSaveInEditor | HideFlags.HideInInspector, added.hideFlags);
            Assert.False(EditorUtility.IsDirty(m_Asset));
            var holder = m_Asset.GetComponent<PrefabTestHolder>();
            // An Undo record the callbacks made dirties nothing once flushed.
            Undo.FlushDirtiness();
            Assert.False(EditorUtility.IsDirty(holder));
            holder.label = "after the import";
            EditorUtility.SetDirty(holder);
            AssetDatabase.SaveAssets();
            Assert.Contains("label: after the import", FileText());
            Assert.DoesNotContain("SphereCollider", FileText());
        }

        sealed class AddingPostprocessor : AssetPostprocessor
        {
            // Not a message name: other tests' editor domain must not see a postprocessor.
            void Add(GameObject root)
            {
                root.AddComponent<UnityEngine.SphereCollider>().hideFlags = HideFlags.HideInInspector;
                EditorUtility.SetDirty(root);
                var holder = root.GetComponent<PrefabTestHolder>();
                Undo.RecordObject(holder, "postprocess");
                holder.label = "from the import";
            }
        }

        /// <summary>A loaded prefab asset holding, under <paramref name="parent"/> (null: as its
        /// root, a variant), an instance of <paramref name="source"/>'s hierarchy: each object linked
        /// to its source object, with its values; its file on disk given the instance's PrefabInstance
        /// document (with <paramref name="modifications"/>: source file id, property path, value).</summary>
        (LoadedFile File, GameObject InstanceRoot) Dependent(LoadedFile file, LoadedFile source, GameObject sourceRoot, Transform parent, long baseId,
            params (long Target, string Path, string Value)[] modifications)
        {
            var data = new PrefabInstanceData { InAsset = true, SourceGuid = source.Guid, FileId = baseId };
            File.AppendAllText(Path.Combine(m_Project, file.Path), InstanceDocument(baseId, source.Guid, modifications));
            var ids = new Dictionary<Object, long>(ReferenceEqualityComparer.Instance);
            foreach (var pair in source.Objects) if (pair.Value is not null) ids[pair.Value] = pair.Key;
            long next = baseId;
            GameObject Copy(GameObject from, Transform under)
            {
                GameObject go;
                using (ObjectRegistry.AsAssets())
                {
                    go = new GameObject(from.name, bare: true) { m_IsPrefabAsset = true, IsPersistentAsset = true };
                    var t = (Transform)ComponentFactory.Create(typeof(Transform), go, deserializing: true);
                    t.IsPersistentAsset = true;
                    go.m_Transform = t;
                    go.m_Components.Add(t);
                    if (under != null)
                    {
                        t.m_Parent = under;
                        under.m_Children.Add(t);
                    }
                    t.m_LocalPosition = from.transform.m_LocalPosition;
                    PrefabLinks.Set(go, new PrefabLink { SourceGuid = source.Guid, SourceFileId = ids[from], Instance = data });
                    PrefabLinks.Set(t, new PrefabLink { SourceGuid = source.Guid, SourceFileId = ids[from.transform], Instance = data });
                    file.Objects[++next] = go;
                    file.Objects[++next] = t;
                    foreach (var c in from.m_Components)
                    {
                        if (c is Transform) continue;
                        var copy = ComponentFactory.Create(c.GetType(), go, deserializing: true);
                        copy.IsPersistentAsset = true;
                        go.m_Components.Add(copy);
                        if (c is PrefabTestHolder h) { ((PrefabTestHolder)copy).label = h.label; ((PrefabTestHolder)copy).count = h.count; }
                        PrefabLinks.Set(copy, new PrefabLink { SourceGuid = source.Guid, SourceFileId = ids[c], Instance = data });
                        file.Objects[++next] = copy;
                    }
                }
                m_Made.Add(go);
                foreach (var child in from.transform.m_Children) Copy(child.m_GameObject, go.transform);
                return go;
            }
            var instanceRoot = Copy(sourceRoot, parent);
            if (parent == null)
            {
                file.MainObject = instanceRoot;
                file.Roots.Add(instanceRoot);
            }
            return (file, instanceRoot);
        }

        LoadedFile NewDependentFile(string name)
        {
            var file = new LoadedFile { Path = "Assets/" + name + ".prefab", Guid = Guid.NewGuid().ToString("N") };
            File.WriteAllText(Path.Combine(m_Project, file.Path), UnityYamlText.Preamble);
            m_Dependents.Add(file);
            return file;
        }

        /// <summary>A PrefabInstance document as a prefab's file holds it.</summary>
        static string InstanceDocument(long id, string sourceGuid, (long Target, string Path, string Value)[] modifications)
        {
            var text = new System.Text.StringBuilder();
            text.Append("--- !u!1001 &").Append(id).Append("\nPrefabInstance:\n  m_ObjectHideFlags: 0\n  serializedVersion: 2\n  m_Modification:\n")
                .Append("    serializedVersion: 3\n    m_TransformParent: {fileID: 0}\n");
            if (modifications.Length == 0) text.Append("    m_Modifications: []\n");
            else
            {
                text.Append("    m_Modifications:\n");
                foreach (var (target, path, value) in modifications)
                    text.Append("    - target: {fileID: ").Append(target).Append(", guid: ").Append(sourceGuid).Append(", type: 3}\n      propertyPath: ")
                        .Append(path).Append("\n      value: ").Append(value).Append("\n      objectReference: {fileID: 0}\n");
            }
            text.Append("    m_RemovedComponents: []\n    m_RemovedGameObjects: []\n    m_AddedGameObjects: []\n    m_AddedComponents: []\n")
                .Append("  m_SourcePrefab: {fileID: 100100000, guid: ").Append(sourceGuid).Append(", type: 3}\n");
            return text.ToString();
        }

        /// <summary>A dependent made loaded, its file's text kept as it is then.</summary>
        void Loaded(LoadedFile file)
        {
            AssetDatabaseRuntime.AddLoaded(file);
            m_Seeds[file] = TextOf(file);
        }

        readonly Dictionary<LoadedFile, string> m_Seeds = new Dictionary<LoadedFile, string>();

        readonly List<LoadedFile> m_Dependents = new List<LoadedFile>();

        string TextOf(LoadedFile file) => File.ReadAllText(Path.Combine(m_Project, file.Path));

        List<string> CaptureWarnings(Action action)
        {
            var warnings = new List<string>();
            var logger = Debug.unityLogger;
            var previous = logger.logHandler;
            logger.logHandler = new WarningCapture(previous, warnings);
            try { action(); }
            finally { logger.logHandler = previous; }
            return warnings;
        }

        /// <summary>A variant of a prefab saved in the session reads the saved prefab's values, its
        /// unsaved change dropped, and is not written again in the session, by any save, with a
        /// warning naming both (matches the Unity 6000.0.71f1 Editor, tests/projects/scenesave
        /// prefab-dirty-saves dependentOwn.*, dependent.file: the Editor writes it unchanged).</summary>
        [Fact]
        public void AVariantIsNotWrittenAfterItsBaseIsSaved()
        {
            var (variant, root) = Dependent(NewDependentFile("Variant"), m_File, m_Asset, null, 1000);
            Loaded(variant);
            var own = root.GetComponent<PrefabTestHolder>();
            own.count = 30;
            EditorUtility.SetDirty(own);
            var holder = m_Asset.GetComponent<PrefabTestHolder>();
            holder.count = 21;
            holder.label = "base changed";
            Assert.True(PrefabUtility.SavePrefabAsset(m_Asset) != null);
            Assert.Equal(21, own.count);
            Assert.Equal("base changed", own.label);
            Assert.False(EditorUtility.IsDirty(own));
            own.label = "after";
            EditorUtility.SetDirty(root);
            var warnings = CaptureWarnings(() =>
            {
                AssetDatabase.SaveAssets();
                PrefabUtility.SavePrefabAsset(root, out var saved);
                Assert.False(saved);
            });
            Assert.Equal(m_Seeds[variant], TextOf(variant));
            Assert.Contains(warnings, w => w.Contains("Assets/Variant.prefab is not saved: " + AssetPath + ", which it holds instances of, was saved in this session")
                && w.Contains("its changes since are not saved either"));
        }

        /// <summary>A prefab holding instances of the saved prefab both directly and through another
        /// prefab (a diamond) reads the saved values through both, and neither it nor the middle
        /// prefab is written; nor is one with a component added to its instance.</summary>
        [Fact]
        public void PrefabsHoldingTheSavedPrefabByAnyPathAreNotWritten()
        {
            var (middle, middleRoot) = Dependent(NewDependentFile("Middle"), m_File, m_Asset, null, 2000);
            Loaded(middle);
            var top = NewDependentFile("Top");
            GameObject topRoot;
            using (ObjectRegistry.AsAssets())
            {
                topRoot = new GameObject("Top", bare: true) { m_IsPrefabAsset = true, IsPersistentAsset = true };
                var t = (Transform)ComponentFactory.Create(typeof(Transform), topRoot, deserializing: true);
                t.IsPersistentAsset = true;
                topRoot.m_Transform = t;
                topRoot.m_Components.Add(t);
                top.Objects[3000] = topRoot;
                top.Objects[3001] = t;
            }
            m_Made.Add(topRoot);
            top.MainObject = topRoot;
            top.Roots.Add(topRoot);
            var (_, direct) = Dependent(top, m_File, m_Asset, topRoot.transform, 3100);
            var (_, throughMiddle) = Dependent(top, middle, middleRoot, topRoot.transform, 3200);
            Loaded(top);
            var (added, addedRoot) = Dependent(NewDependentFile("Added"), m_File, m_Asset, null, 4000);
            Loaded(added);
            addedRoot.AddComponent<SphereCollider>();
            m_Asset.GetComponent<PrefabTestHolder>().label = "saved";
            var refreshWarnings = CaptureWarnings(() => Assert.True(PrefabUtility.SavePrefabAsset(m_Asset) != null));
            Assert.Equal("saved", middleRoot.GetComponent<PrefabTestHolder>().label);
            Assert.Equal("saved", direct.GetComponent<PrefabTestHolder>().label);
            Assert.Equal("saved", throughMiddle.GetComponent<PrefabTestHolder>().label);
            // The component added to an instance and not saved is kept, and said.
            Assert.Contains(refreshWarnings, w => w.Contains("Assets/Added.prefab keeps 1 object added to it and not saved after " + AssetPath + " was saved"));
            Assert.DoesNotContain(refreshWarnings, w => w.Contains("Assets/Top.prefab keeps") || w.Contains("Assets/Middle.prefab keeps"));
            // A prefab loaded since, holding the one whose refresh kept something, is not written;
            // one holding a prefab refreshed in full is.
            Object.DestroyImmediate(addedRoot.GetComponent<SphereCollider>(), true);
            var (onAdded, _) = Dependent(NewDependentFile("OnAdded"), added, addedRoot, null, 4500);
            Loaded(onAdded);
            Assert.Contains("Assets/Added.prefab, which it holds instances of, is held as it was before " + AssetPath + " was saved in this session", PrefabAssets.Refusal(onAdded));
            var (onMiddle, _) = Dependent(NewDependentFile("OnMiddle"), middle, middleRoot, null, 4700);
            Loaded(onMiddle);
            Assert.Null(PrefabAssets.Refusal(onMiddle));
            foreach (var root in new[] { middleRoot, topRoot, addedRoot }) EditorUtility.SetDirty(root);
            var warnings = CaptureWarnings(() => AssetDatabase.SaveAssets());
            Assert.Equal(m_Seeds[middle], TextOf(middle));
            Assert.Equal(m_Seeds[top], TextOf(top));
            Assert.Equal(m_Seeds[added], TextOf(added));
            foreach (var name in new[] { "Middle", "Top", "Added" })
                Assert.Contains(warnings, w => w.Contains("Assets/" + name + ".prefab is not saved: " + AssetPath + ", which it holds instances of, was saved in this session"));
        }

        /// <summary>The prefab file of a variant of the test's prefab, not loaded: a
        /// PrefabInstance document naming it, listed by the asset database.</summary>
        LoadedFile UnloadedVariantFile(string name, out LoadedFile asLoaded, out GameObject asLoadedRoot)
        {
            // Its objects, to make the instances of it a loaded prefab holds; never published.
            (asLoaded, asLoadedRoot) = Dependent(NewDependentFile(name), m_File, m_Asset, null, 6000);
            AssetDatabase.List("[{\"guid\":\"" + asLoaded.Guid + "\",\"path\":\"" + asLoaded.Path + "\",\"kind\":\"prefab\"},"
                + "{\"guid\":\"" + m_Guid + "\",\"path\":\"" + AssetPath + "\",\"kind\":\"prefab\"}]");
            return asLoaded;
        }

        /// <summary>A prefab holding instances of the saved prefab only through a prefab that is not
        /// loaded (its variant's variant) is found through the files, and is not written in the
        /// session, by any save, with a warning; finding it loads nothing.</summary>
        [Fact]
        public void APrefabHoldingTheSavedPrefabThroughAnUnloadedOneIsNotWritten()
        {
            try
            {
                var v1 = UnloadedVariantFile("V1", out var v1File, out var v1Root);
                var (v2, v2Root) = Dependent(NewDependentFile("V2"), v1File, v1Root, null, 7000);
                Loaded(v2);
                var chain = PrefabAssets.Chain(v2.Guid);
                Assert.Contains(v1.Guid, chain);
                Assert.Contains(m_Guid, chain);
                Assert.False(AssetDatabaseRuntime.IsLoaded(v1.Guid));
                m_Asset.GetComponent<PrefabTestHolder>().count = 21;
                CaptureWarnings(() => Assert.True(PrefabUtility.SavePrefabAsset(m_Asset) != null));
                EditorUtility.SetDirty(v2Root);
                var warnings = CaptureWarnings(() =>
                {
                    AssetDatabase.SaveAssets();
                    PrefabUtility.SavePrefabAsset(v2Root, out var saved);
                    Assert.False(saved);
                });
                Assert.Equal(m_Seeds[v2], TextOf(v2));
                Assert.Contains(warnings, w => w.Contains("Assets/V2.prefab is not saved: " + AssetPath + ", which it holds instances of, was saved in this session"));
            }
            finally
            {
                AssetDatabase.Invalidate();
            }
        }

        /// <summary>One SaveAssets with the prefab and a prefab holding it through a clean prefab not
        /// loaded both dirty writes the prefab first, whatever their paths' order, then refuses the
        /// other: the order follows the files' nesting.</summary>
        [Fact]
        public void SaveAssetsOrdersThroughPrefabsNotLoaded()
        {
            try
            {
                UnloadedVariantFile("V1", out var v1File, out var v1Root);
                var (v2, v2Root) = Dependent(NewDependentFile("A_V2"), v1File, v1Root, null, 7000);
                Loaded(v2);
                var holder = m_Asset.GetComponent<PrefabTestHolder>();
                holder.count = 45;
                EditorUtility.SetDirty(holder);
                EditorUtility.SetDirty(v2Root);
                var warnings = CaptureWarnings(() => AssetDatabase.SaveAssets());
                Assert.Contains("count: 45", FileText());
                Assert.Equal(m_Seeds[v2], TextOf(v2));
                Assert.Contains(warnings, w => w.Contains("Assets/A_V2.prefab is not saved: " + AssetPath + ", which it holds instances of, was saved in this session"));
            }
            finally
            {
                AssetDatabase.Invalidate();
            }
        }

        /// <summary>A variant whose file holds an override written earlier in the session reads it
        /// after its base is saved (matches the Unity 6000.0.71f1 Editor, tests/projects/scenesave
        /// prefab-dirty-saves own6.afterBaseSave): its instance takes its file's modifications.</summary>
        [Fact]
        public void ADependentKeepsTheOverridesItsFileHasAfterItsSourceIsSaved()
        {
            var (own, root) = Dependent(NewDependentFile("Own"), m_File, m_Asset, null, 8000, (102, "count", "31"));
            Loaded(own);
            var holder = root.GetComponent<PrefabTestHolder>();
            // As written earlier in the session: the file has the override, the loaded instance's
            // modifications are those it was read with.
            holder.count = 31;
            m_Asset.GetComponent<PrefabTestHolder>().count = 26;
            m_Asset.GetComponent<PrefabTestHolder>().label = "own base";
            CaptureWarnings(() => Assert.True(PrefabUtility.SavePrefabAsset(m_Asset) != null));
            Assert.Equal(31, holder.count);
            Assert.Equal("own base", holder.label);
        }

        /// <summary>A component the saved prefab gained is made in its loaded variant, and the
        /// variant's own values are kept: the refresh takes the components the variant removes from
        /// its file, not from what it lacks now.</summary>
        [Fact]
        public void ADependentTakesAComponentItsSourceGained()
        {
            var (variant, root) = Dependent(NewDependentFile("Gains"), m_File, m_Asset, null, 9000);
            Loaded(variant);
            var gained = (SphereCollider)AssetComponent(m_Asset, typeof(SphereCollider), 150);
            m_Asset.GetComponent<PrefabTestHolder>().count = 27;
            var warnings = CaptureWarnings(() => Assert.True(PrefabUtility.SavePrefabAsset(m_Asset) != null));
            Assert.NotNull(root.GetComponent<SphereCollider>());
            Assert.Equal(27, root.GetComponent<PrefabTestHolder>().count);
            Assert.DoesNotContain(warnings, w => w.Contains("Assets/Gains.prefab keeps"));
            Object.DestroyImmediate(gained, true);
        }

        /// <summary>A prefab loaded after the save of a prefab it holds is written as usual.</summary>
        [Fact]
        public void APrefabLoadedAfterItsSourceIsSavedIsWritten()
        {
            m_Asset.GetComponent<PrefabTestHolder>().count = 28;
            Assert.True(PrefabUtility.SavePrefabAsset(m_Asset) != null);
            var (later, root) = Dependent(NewDependentFile("Later"), m_File, m_Asset, null, 9500);
            Loaded(later);
            var warnings = CaptureWarnings(() =>
            {
                PrefabUtility.SavePrefabAsset(root, out var saved);
                Assert.True(saved);
            });
            Assert.DoesNotContain(warnings, w => w.Contains("is not saved"));
            Assert.NotEqual(m_Seeds[later], TextOf(later));
        }

        /// <summary>A prefab loaded after a save, or a new prefab, holding instances of a prefab
        /// whose refresh could not give all its reload gives is not written, and says so; one whose
        /// held prefab was refreshed in full is.</summary>
        [Fact]
        public void APrefabHoldingAnIncompletelyRefreshedOneIsNotWritten()
        {
            var (later, root) = Dependent(NewDependentFile("Later"), m_File, m_Asset, null, 9600);
            Loaded(later);
            PrefabAssets.MarkStaleForTests(m_File, "Assets/Base.prefab", incomplete: true);
            var warnings = CaptureWarnings(() =>
            {
                PrefabUtility.SavePrefabAsset(root, out var saved);
                Assert.False(saved);
                var inst = Instance();
                Assert.Null(PrefabAssets.Create("Assets/New.prefab", inst));
            });
            Assert.Equal(m_Seeds[later], TextOf(later));
            Assert.False(File.Exists(Path.Combine(m_Project, "Assets/New.prefab")));
            Assert.Contains(warnings, w => w.Contains("Assets/Later.prefab is not saved: " + AssetPath + ", which it holds instances of, is held as it was before Assets/Base.prefab was saved in this session"));
            Assert.Contains(warnings, w => w.Contains("Assets/New.prefab is not saved: " + AssetPath + ", which it holds instances of, is held as it was before Assets/Base.prefab was saved in this session"));
            PrefabAssets.MarkStaleForTests(m_File, "Assets/Base.prefab", incomplete: false);
            warnings = CaptureWarnings(() =>
            {
                PrefabUtility.SavePrefabAsset(root, out var saved);
                Assert.True(saved);
            });
            Assert.DoesNotContain(warnings, w => w.Contains("is not saved"));
        }

        /// <summary>A prefab held stale is not given its contents (refused before the asset takes
        /// them); a plain hierarchy saved over it replaces what it held and is written.</summary>
        [Fact]
        public void ContentsOfAStalePrefabAreRefusedAndAHierarchyOverItIsWritten()
        {
            var before = FileText();
            PrefabAssets.MarkStaleForTests(m_File, "Assets/Base.prefab");
            var contentsRoot = PrefabContents.Load(m_File, AssetPath);
            var contents = PrefabContents.Of(contentsRoot);
            contentsRoot.GetComponent<PrefabTestHolder>().count = 55;
            var warnings = CaptureWarnings(() => Assert.False(PrefabContents.Save(contents)));
            PrefabContents.Unload(contentsRoot);
            Assert.Equal(3, m_Asset.GetComponent<PrefabTestHolder>().count);
            Assert.Equal(before, FileText());
            Assert.Contains(warnings, w => w.Contains(AssetPath + " is not saved: Assets/Base.prefab, which it holds instances of, was saved in this session"));
            var plain = new GameObject("Plain");
            m_Made.Add(plain);
            warnings = CaptureWarnings(() => Assert.True(PrefabContents.Replace(plain, m_File)));
            Assert.DoesNotContain(warnings, w => w.Contains("is not saved"));
            Assert.NotEqual(before, FileText());
            Assert.Null(PrefabAssets.Refusal(m_File));
        }

        /// <summary>A hierarchy saved over a prefab whose file nests a prefab held with what its
        /// refresh kept is written: what decides is what the hierarchy holds, not the file it
        /// replaces.</summary>
        [Fact]
        public void AHierarchyOverAPrefabIsJudgedByWhatItHolds()
        {
            var (x, _) = Dependent(NewDependentFile("X"), m_File, m_Asset, null, 9800);
            Loaded(x);
            PrefabAssets.MarkStaleForTests(x, "Assets/Base.prefab", incomplete: true);
            File.AppendAllText(Path.Combine(m_Project, AssetPath), InstanceDocument(9900, x.Guid, Array.Empty<(long, string, string)>()));
            Assert.NotNull(PrefabAssets.Refusal(m_File));
            var plain = new GameObject("Plain");
            m_Made.Add(plain);
            var warnings = CaptureWarnings(() => Assert.True(PrefabContents.Replace(plain, m_File)));
            Assert.DoesNotContain(warnings, w => w.Contains("is not saved"));
        }

        /// <summary>A hierarchy saved over a variant whose base is held with what its refresh kept
        /// is refused before the variant's objects change: the variant's root keeps its base.</summary>
        [Fact]
        public void AHierarchyOverAVariantOfAnIncompletelyRefreshedBaseIsRefusedFirst()
        {
            var (variant, root) = Dependent(NewDependentFile("Over"), m_File, m_Asset, null, 9950);
            Loaded(variant);
            PrefabAssets.MarkStaleForTests(m_File, "Assets/Base.prefab", incomplete: true);
            var plain = new GameObject("Plain");
            m_Made.Add(plain);
            var warnings = CaptureWarnings(() => Assert.False(PrefabContents.Replace(plain, variant)));
            Assert.NotNull(root.GetComponent<PrefabTestHolder>());
            Assert.Equal(1, root.transform.childCount);
            Assert.Equal(m_Seeds[variant], TextOf(variant));
            Assert.Contains(warnings, w => w.Contains("Assets/Over.prefab is not saved: " + AssetPath + ", which it holds instances of, is held as it was before Assets/Base.prefab was saved in this session"));
        }

        /// <summary>A prefab held stale is refused before an apply changes anything: the instance
        /// keeps its override, the prefab its value and its file.</summary>
        [Fact]
        public void AnApplyToAStalePrefabChangesNothing()
        {
            var scene = Instance();
            var before = FileText();
            PrefabAssets.MarkStaleForTests(m_File, "Assets/Base.prefab");
            var instance = scene.GetComponent<PrefabTestHolder>();
            instance.count = 77;
            PrefabUtility.RecordPrefabInstancePropertyModifications(instance);
            var warnings = CaptureWarnings(() => PrefabUtility.ApplyPropertyOverride(new SerializedObject(instance).FindProperty("count"), AssetPath, InteractionMode.AutomatedAction));
            Assert.Equal(3, m_Asset.GetComponent<PrefabTestHolder>().count);
            Assert.Equal(77, instance.count);
            Assert.Contains(PrefabUtility.GetPropertyModifications(scene), m => m.propertyPath == "count" && m.value == "77");
            Assert.Equal(before, FileText());
            Assert.Contains(warnings, w => w.Contains(AssetPath + " is not saved: Assets/Base.prefab, which it holds instances of, was saved in this session"));
        }

        /// <summary>One SaveAssets with a prefab and its variant both dirty writes the prefab first,
        /// then not the variant, whatever their paths' order; the order puts held prefabs first, a
        /// diamond included, the others by path.</summary>
        [Fact]
        public void SaveAssetsWritesAPrefabBeforeThoseHoldingIt()
        {
            var (variant, root) = Dependent(NewDependentFile("A_Variant"), m_File, m_Asset, null, 5000);
            Loaded(variant);
            var holder = m_Asset.GetComponent<PrefabTestHolder>();
            holder.count = 44;
            EditorUtility.SetDirty(holder);
            EditorUtility.SetDirty(root);
            CaptureWarnings(() => AssetDatabase.SaveAssets());
            Assert.Contains("count: 44", FileText());
            Assert.Equal(m_Seeds[variant], TextOf(variant));
            var held = new Dictionary<string, string[]>
            {
                ["top"] = new[] { "s", "middle" },
                ["middle"] = new[] { "s" },
                ["s"] = new string[0],
                ["other"] = new string[0],
            };
            Assert.Equal(new[] { "s", "middle", "top", "other" },
                PrefabAssets.SourcesFirst(new[] { ("top", "Assets/A.prefab"), ("middle", "Assets/B.prefab"), ("s", "Assets/Z.prefab"), ("other", "Assets/Y.asset") }, g => held[g]));
        }

        /// <summary>A prefab that could not take a source prefab it holds instances of as that
        /// was saved is not written, by any save, and says so naming both.</summary>
        [Fact]
        public void APrefabLeftBehindByItsSourceIsNotWritten()
        {
            var before = FileText();
            var warnings = new List<string>();
            var logger = Debug.unityLogger;
            var previous = logger.logHandler;
            logger.logHandler = new WarningCapture(previous, warnings);
            try
            {
                PrefabAssets.MarkStaleForTests(m_File, "Assets/Base.prefab");
                var holder = m_Asset.GetComponent<PrefabTestHolder>();
                holder.label = "stale";
                EditorUtility.SetDirty(holder);
                PrefabUtility.SavePrefabAsset(m_Asset, out var saved);
                Assert.False(saved);
                AssetDatabase.SaveAssets();
                Assert.Equal(before, FileText());
                Assert.Contains(warnings, w => w.Contains(AssetPath + " is not saved: Assets/Base.prefab, which it holds instances of, was saved in this session"));
            }
            finally
            {
                logger.logHandler = previous;
            }
        }

        sealed class WarningCapture : ILogHandler
        {
            readonly ILogHandler m_Inner;
            readonly List<string> m_Warnings;

            internal WarningCapture(ILogHandler inner, List<string> warnings)
            {
                m_Inner = inner;
                m_Warnings = warnings;
            }

            public void LogFormat(LogType type, Object context, string format, params object[] args)
            {
                if (type == LogType.Warning || type == LogType.Error) m_Warnings.Add(args == null || args.Length == 0 ? format : string.Format(format, args));
                m_Inner.LogFormat(type, context, format, args);
            }

            public void LogException(Exception exception, Object context) => m_Inner.LogException(exception, context);
        }

        /// <summary>A component added to a prefab asset's GameObject makes it dirty (matches the
        /// Unity 6000.0.71f1 Editor, tests/projects/scenesave prefab-dirty-saves structure.*), not
        /// one an import's postprocessor adds.</summary>
        [Fact]
        public void AComponentAddedToAPrefabAssetMakesItDirtyOutsideImports()
        {
            using (ScriptedImports.Scope())
                m_AssetPart.AddComponent<UnityEngine.BoxCollider>();
            Assert.False(EditorUtility.IsDirty(m_AssetPart));
            m_AssetPart.AddComponent<UnityEngine.SphereCollider>();
            Assert.True(EditorUtility.IsDirty(m_AssetPart));
            EditorUtility.ClearDirty(m_AssetPart);
            // An imported model's GameObject is not made dirty (unmeasured).
            var model = new LoadedFile { Path = "Assets/Model.fbx", Guid = Guid.NewGuid().ToString("N") };
            GameObject modelRoot;
            using (ObjectRegistry.AsAssets())
            {
                modelRoot = new GameObject("Model", bare: true) { m_IsPrefabAsset = true, IsPersistentAsset = true };
                var t = (Transform)ComponentFactory.Create(typeof(Transform), modelRoot, deserializing: true);
                t.IsPersistentAsset = true;
                modelRoot.m_Transform = t;
                modelRoot.m_Components.Add(t);
            }
            m_Made.Add(modelRoot);
            model.Objects[1] = modelRoot;
            model.MainObject = modelRoot;
            model.Roots.Add(modelRoot);
            AssetDatabaseRuntime.AddLoaded(model);
            try
            {
                modelRoot.AddComponent<UnityEngine.SphereCollider>();
                Assert.False(EditorUtility.IsDirty(modelRoot));
            }
            finally { AssetDatabaseRuntime.Invalidate(model.Guid); }
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
