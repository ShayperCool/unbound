using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Unbound.Runtime;
using UnityEngine.Rendering;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The documents of an imported prefab (ImportedPrefabDocuments): each component's
    /// document reads back, through the scene loader's readers, to the state it was written from;
    /// references inside the hierarchy are local, to the import's assets by its GUID; a class
    /// without a writer fails loudly.</summary>
    public class ImportedPrefabDocumentsTests : IDisposable
    {
        /// <summary>Everything a test makes is destroyed after it: colliders would stay in the
        /// physics world other tests share.</summary>
        readonly List<Object> m_Made = new List<Object>();

        T Made<T>(T o) where T : Object
        {
            m_Made.Add(o);
            return o;
        }

        public void Dispose()
        {
            using var engine = MainThreadCheck.Engine();
            foreach (var o in m_Made)
                if (o != null) Object.DestroyImmediate(o, true);
        }
        const string Guid = "9d7c89a9f6418f5886659b04a7e86b6b";

        public sealed class Marker : MonoBehaviour
        {
            public int count;
            public Transform lid;
        }

        sealed class Built
        {
            internal GameObject Root, Child;
            internal LoadedFile File;
            internal Mesh Mesh;
            internal Material Material;
            internal SpeedTreeWindAsset Wind;
        }

        Built Build()
        {
            var b = new Built
            {
                Root = Made(new GameObject("Tree") { layer = 3, tag = "Untagged" }),
                Child = Made(new GameObject("LOD0")),
                Mesh = Made(new Mesh { name = "LOD0_Mesh" }),
                Material = Made(new Material(Shader.Create("Test/Bark", 2000)) { name = "Bark" }),
                Wind = Made(new SpeedTreeWindAsset(1, new SpeedTreeWindConfig9()) { name = "SpeedTreeWind" }),
            };
            b.Child.transform.SetParent(b.Root.transform, false);
            b.Child.transform.localPosition = new Vector3(1f, 2f, 3f);
            b.Child.transform.localRotation = new Quaternion(0f, 0.38268343f, 0f, 0.9238795f);
            b.Child.transform.localScale = new Vector3(2f, 2f, 2f);
            b.Child.AddComponent<MeshFilter>().sharedMesh = b.Mesh;
            var renderer = b.Child.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { b.Material, null };
            renderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            renderer.sortingOrder = 3;
            renderer.renderingLayerMask = 5;
            renderer.scaleInLightmap = 0.5f;
            b.Child.AddComponent<Tree>().windAsset = b.Wind;
            var group = b.Root.AddComponent<LODGroup>();
            group.fadeMode = LODFadeMode.SpeedTree;
            group.animateCrossFading = true;
            group.localReferencePoint = new Vector3(0f, 0.5f, 0f);
            group.size = 1.25f;
            group.SetLODs(new[] { new LOD(0.5f, new Renderer[] { renderer }) { fadeTransitionWidth = 0.25f }, new LOD(0.01f, new Renderer[0]) });
            var body = b.Root.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.mass = 2f;
            body.constraints = RigidbodyConstraints.FreezeRotationY;
            var box = b.Root.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(0.25f, 0.5f, 0.75f);
            box.center = new Vector3(0f, 0.25f, 0f);
            b.Root.AddComponent<SphereCollider>().radius = 0.125f;
            var capsule = b.Root.AddComponent<CapsuleCollider>();
            capsule.direction = 2;
            capsule.height = 3f;
            capsule.radius = 0.2f;
            var marker = b.Root.AddComponent<Marker>();
            marker.count = 7;
            marker.lid = b.Child.transform;
            b.File = new LoadedFile { Guid = Guid, Path = "Assets/Trees/Tree.st9", MainObject = b.Root };
            long id = 100;
            foreach (var o in new Object[] { b.Root, b.Child, b.Mesh, b.Material, b.Wind })
                b.File.Objects[id++] = o;
            foreach (var go in new[] { b.Root, b.Child })
                foreach (var c in go.GetComponents<Component>())
                    b.File.Objects[id++] = c;
            return b;
        }

        static long IdOf(LoadedFile file, Object o) => file.Objects.First(p => ReferenceEquals(p.Value, o)).Key;

        /// <summary>The documents, by file id: class id and body.</summary>
        static Dictionary<long, (int ClassId, string Type, YNode Body)> Parse(string text)
        {
            var docs = new Dictionary<long, (int, string, YNode)>();
            foreach (var chunk in text.Split("--- !u!").Skip(1))
            {
                var header = chunk.Substring(0, chunk.IndexOf('\n'));
                var parts = header.Split(" &");
                var body = chunk.Substring(chunk.IndexOf('\n') + 1);
                var type = body.Substring(0, body.IndexOf(':'));
                docs[long.Parse(parts[1], CultureInfo.InvariantCulture)] = (int.Parse(parts[0], CultureInfo.InvariantCulture), type, TestYaml.DocumentBody(body));
            }
            return docs;
        }

        [Fact]
        public void ComponentsReadBackFromTheirDocuments()
        {
            using var engine = MainThreadCheck.Engine();
            var b = Build();
            var text = ImportedPrefabDocuments.Write(b.File, b.Root);
            var docs = Parse(text);
            // A document per GameObject and component; the import's assets are not documents.
            Assert.Equal(2 + b.Root.GetComponents<Component>().Length + b.Child.GetComponents<Component>().Length, docs.Count);
            Assert.DoesNotContain(IdOf(b.File, b.Mesh), docs.Keys);

            var rootDoc = docs[IdOf(b.File, b.Root)];
            Assert.Equal((1, "GameObject"), (rootDoc.ClassId, rootDoc.Type));
            Assert.Equal("Tree", rootDoc.Body.Str("m_Name"));
            Assert.Equal(3, rootDoc.Body.Int("m_Layer"));
            Assert.Equal(b.Root.GetComponents<Component>().Select(c => IdOf(b.File, c)),
                rootDoc.Body.Get("m_Component").Seq().Select(item => item.Ref("component").FileId));

            // Every component reads back into a fresh one through its reader (as a scene loads it).
            foreach (var original in b.Root.GetComponents<Component>().Concat(b.Child.GetComponents<Component>()))
            {
                var doc = docs[IdOf(b.File, original)];
                Assert.Equal(EngineTypes.EngineClassIdOf(original.GetType()), doc.ClassId);
                Assert.Equal(IdOf(b.File, original.gameObject), doc.Body.Ref("m_GameObject").FileId);
            }
            Component Read(Component original)
            {
                var go = Made(new GameObject("copy"));
                var copy = original is Transform ? go.transform : go.AddComponent(original.GetType());
                ObjectGraphBuilder.ReadComponent(copy, docs[IdOf(b.File, original)].Body, b.File);
                return copy;
            }
            var child = (Transform)Read(b.Child.transform);
            Assert.Equal(b.Child.transform.localPosition, child.localPosition);
            Assert.Equal(b.Child.transform.localRotation, child.localRotation);
            Assert.Equal(b.Child.transform.localScale, child.localScale);
            var childDoc = docs[IdOf(b.File, b.Child.transform)].Body;
            Assert.Equal(IdOf(b.File, b.Root.transform), childDoc.Ref("m_Father").FileId);
            Assert.True(childDoc.Ref("m_Father").IsLocal);
            Assert.Equal(new[] { IdOf(b.File, b.Child.transform) },
                docs[IdOf(b.File, b.Root.transform)].Body.Get("m_Children").Seq().Select(r => r.AsRef().FileId));

            var filterDoc = docs[IdOf(b.File, b.Child.GetComponent<MeshFilter>())].Body.Ref("m_Mesh");
            Assert.Equal((IdOf(b.File, b.Mesh), Guid), (filterDoc.FileId, filterDoc.Guid));
            Assert.Same(b.Mesh, ((MeshFilter)Read(b.Child.GetComponent<MeshFilter>())).sharedMesh);

            var renderer = (MeshRenderer)Read(b.Child.GetComponent<MeshRenderer>());
            var source = b.Child.GetComponent<MeshRenderer>();
            Assert.Equal(new[] { b.Material, null }, renderer.sharedMaterials);
            Assert.Equal(
                (source.shadowCastingMode, source.receiveShadows, source.lightProbeUsage, source.reflectionProbeUsage, source.motionVectorGenerationMode, source.sortingOrder, source.renderingLayerMask, source.scaleInLightmap),
                (renderer.shadowCastingMode, renderer.receiveShadows, renderer.lightProbeUsage, renderer.reflectionProbeUsage, renderer.motionVectorGenerationMode, renderer.sortingOrder, renderer.renderingLayerMask, renderer.scaleInLightmap));

            Assert.Same(b.Wind, ((Tree)Read(b.Child.GetComponent<Tree>())).windAsset);

            var group = (LODGroup)Read(b.Root.GetComponent<LODGroup>());
            var lods = group.GetLODs();
            Assert.Equal(2, lods.Length);
            Assert.Equal((0.5f, 0.25f), (lods[0].screenRelativeTransitionHeight, lods[0].fadeTransitionWidth));
            Assert.Equal(new Renderer[] { source }, lods[0].renderers);
            Assert.Empty(lods[1].renderers);
            Assert.Equal((LODFadeMode.SpeedTree, true, new Vector3(0f, 0.5f, 0f), 1.25f), (group.fadeMode, group.animateCrossFading, group.localReferencePoint, group.size));

            var body = (Rigidbody)Read(b.Root.GetComponent<Rigidbody>());
            Assert.Equal((true, false, 2f, RigidbodyConstraints.FreezeRotationY), (body.isKinematic, body.useGravity, body.mass, body.constraints));

            var box = (BoxCollider)Read(b.Root.GetComponent<BoxCollider>());
            Assert.Equal((true, new Vector3(0.25f, 0.5f, 0.75f), new Vector3(0f, 0.25f, 0f)), (box.isTrigger, box.size, box.center));
            Assert.Equal(0.125f, ((SphereCollider)Read(b.Root.GetComponent<SphereCollider>())).radius);
            var capsule = (CapsuleCollider)Read(b.Root.GetComponent<CapsuleCollider>());
            Assert.Equal((2, 3f, 0.2f), (capsule.direction, capsule.height, capsule.radius));

            // A script's fields, its reference into the hierarchy local.
            var marker = docs[IdOf(b.File, b.Root.GetComponent<Marker>())];
            Assert.Equal(114, marker.ClassId);
            Assert.Equal(7, marker.Body.Int("count"));
            Assert.Equal(IdOf(b.File, b.Child.transform), marker.Body.Ref("lid").FileId);
        }

        /// <summary>A 2D importer's hierarchy (PSD, Aseprite, SVG importers): sprite renderers.</summary>
        [Fact]
        public void SpriteRenderersReadBackFromTheirDocuments()
        {
            using var engine = MainThreadCheck.Engine();
            var texture = Made(new Texture2D(4, 4) { name = "Atlas" });
            var sprite = Made(Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f)));
            sprite.name = "Arm";
            var root = Made(new GameObject("Character"));
            var r = root.AddComponent<SpriteRenderer>();
            r.sprite = sprite;
            r.color = new Color(1f, 0.5f, 0.25f, 0.75f);
            r.flipX = true;
            r.drawMode = SpriteDrawMode.Sliced;
            r.size = new Vector2(2f, 3f);
            r.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
            r.spriteSortPoint = SpriteSortPoint.Pivot;
            r.sortingOrder = -2;
            var file = new LoadedFile { Guid = Guid, Path = "Assets/Art/Character.psb", MainObject = root };
            file.Objects[1] = root;
            file.Objects[2] = root.transform;
            file.Objects[3] = r;
            file.Objects[4] = sprite;
            var docs = Parse(ImportedPrefabDocuments.Write(file, root));
            Assert.Equal(212, docs[3].ClassId);
            Assert.Equal((4L, Guid), (docs[3].Body.Ref("m_Sprite").FileId, docs[3].Body.Ref("m_Sprite").Guid));
            var copy = Made(new GameObject("copy")).AddComponent<SpriteRenderer>();
            ObjectGraphBuilder.ReadComponent(copy, docs[3].Body, file);
            Assert.Same(sprite, copy.sprite);
            Assert.Equal((r.color, r.flipX, r.flipY, r.drawMode, r.size, r.maskInteraction, r.spriteSortPoint, r.sortingOrder),
                (copy.color, copy.flipX, copy.flipY, copy.drawMode, copy.size, copy.maskInteraction, copy.spriteSortPoint, copy.sortingOrder));
        }

        /// <summary>An object of the import outside the written hierarchy (another root) is
        /// referenced by the asset's GUID, as an asset of the import.</summary>
        [Fact]
        public void ObjectsOutsideTheHierarchyAreReferencedAsTheAssets()
        {
            using var engine = MainThreadCheck.Engine();
            var b = Build();
            var other = Made(new GameObject("OtherRoot"));
            b.File.Objects[500] = other;
            b.File.Objects[501] = other.transform;
            b.Root.GetComponent<Marker>().lid = other.transform;
            var docs = Parse(ImportedPrefabDocuments.Write(b.File, b.Root));
            Assert.DoesNotContain(500L, docs.Keys);
            var lid = docs[IdOf(b.File, b.Root.GetComponent<Marker>())].Body.Ref("lid");
            Assert.Equal((501L, Guid), (lid.FileId, lid.Guid));
        }

        /// <summary>Instantiate copies a Tree's wind asset.</summary>
        [Fact]
        public void InstantiateCopiesATreesWindAsset()
        {
            using var engine = MainThreadCheck.Engine();
            var b = Build();
            var clone = Made((GameObject)Object.Instantiate(b.Child));
            Assert.Same(b.Wind, clone.GetComponent<Tree>().windAsset);
        }

        [Fact]
        public void AComponentWithoutItsOwnWriterFailsLoudly()
        {
            using var engine = MainThreadCheck.Engine();
            var b = Build();
            // A SkinnedMeshRenderer derives Renderer's data but has fields of its own.
            var skinned = b.Child.AddComponent<SkinnedMeshRenderer>();
            b.File.Objects[999] = skinned;
            var e = Assert.Throws<NotSupportedException>(() => ImportedPrefabDocuments.Write(b.File, b.Root));
            Assert.Contains("SkinnedMeshRenderer", e.Message);
            Assert.Contains(b.File.Path, e.Message);
        }

        [Fact]
        public void AnObjectOfTheHierarchyWithoutAFileIdFails()
        {
            using var engine = MainThreadCheck.Engine();
            var b = Build();
            Made(new GameObject("Unlisted")).transform.SetParent(b.Root.transform, false);
            var e = Assert.Throws<InvalidOperationException>(() => ImportedPrefabDocuments.Write(b.File, b.Root));
            Assert.Contains("Unlisted", e.Message);
        }
    }
}
