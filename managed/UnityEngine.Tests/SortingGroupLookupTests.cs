using System.Collections.Generic;
using UnityEngine.Rendering;
using Xunit;

namespace UnityEngine.Tests
{
    public class SortingGroupLookupTests
    {
        [Fact]
        public void UngroupedRenderersNeverResolveToTheFirstActiveGroup()
        {
            var groupOwner = new GameObject("group");
            var group = groupOwner.AddComponent<SortingGroup>();
            var sprite = new GameObject("sprite");
            var mesh = new GameObject("mesh");
            try
            {
                foreach (var renderer in new Renderer[] { sprite.AddComponent<SpriteRenderer>(), mesh.AddComponent<MeshRenderer>() })
                {
                    Assert.Equal(SortingGroup.invalidSortingGroupID, renderer.sortingGroupID);
                    Assert.Null(SortingGroup.GetSortingGroupByIndex(renderer.sortingGroupID));
                }
                Assert.Same(group, SortingGroup.GetSortingGroupByIndex(group.index));
            }
            finally
            {
                Object.DestroyImmediate(groupOwner);
                Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(mesh);
            }
        }

        [Fact]
        public void RemovingAMiddleGroupUpdatesTheMovedGroupsIndex()
        {
            var owners = new List<GameObject>();
            var groups = new List<SortingGroup>();
            try
            {
                for (int i = 0; i < 129; i++)
                {
                    var owner = new GameObject("group");
                    owners.Add(owner);
                    groups.Add(owner.AddComponent<SortingGroup>());
                }
                int removed = groups[17].index;
                var last = groups[groups.Count - 1];
                groups[17].enabled = false;
                Assert.Equal(SortingGroup.invalidSortingGroupID, groups[17].index);
                Assert.Equal(removed, last.index);
                Assert.Same(last, SortingGroup.GetSortingGroupByIndex(removed));
                for (int i = 0; i < groups.Count; i++)
                    if (i != 17) Assert.Same(groups[i], SortingGroup.GetSortingGroupByIndex(groups[i].index));
                Assert.Null(SortingGroup.GetSortingGroupByIndex(-1));
                Assert.Null(SortingGroup.GetSortingGroupByIndex(int.MinValue));
                Assert.Null(SortingGroup.GetSortingGroupByIndex(int.MaxValue));
                Assert.Null(SortingGroup.GetSortingGroupByIndex(SortingGroup.invalidSortingGroupID));
            }
            finally { foreach (var owner in owners) Object.DestroyImmediate(owner); }
        }

        [Fact]
        public void AnInactiveHierarchyRegistersDescendantsBeforeTheirParent()
        {
            var root = new GameObject("root");
            root.SetActive(false);
            var outer = root.AddComponent<SortingGroup>();
            var middle = new GameObject("middle");
            middle.transform.SetParent(root.transform);
            var left = new GameObject("left");
            left.transform.SetParent(middle.transform);
            var a = left.AddComponent<SortingGroup>();
            var right = new GameObject("right");
            right.transform.SetParent(root.transform);
            var b = right.AddComponent<SortingGroup>();
            try
            {
                Assert.Equal(SortingGroup.invalidSortingGroupID, outer.index);
                Assert.Equal(SortingGroup.invalidSortingGroupID, a.index);
                root.SetActive(true);
                Assert.True(a.index < b.index);
                Assert.True(b.index < outer.index);
                Assert.Same(a, SortingGroup.GetSortingGroupByIndex(a.index));
                Assert.Same(outer, SortingGroup.GetSortingGroupByIndex(outer.index));
                int outerIndex = outer.index;
                int childIndex = b.index;
                Object.DestroyImmediate(root);
                Assert.Null(SortingGroup.GetSortingGroupByIndex(outerIndex));
                Assert.Null(SortingGroup.GetSortingGroupByIndex(childIndex));
            }
            finally { if (root != null) Object.DestroyImmediate(root); }
        }

        [Fact]
        public void ClonesReceiveTheirOwnRegistryEntryAndInactiveClonesWait()
        {
            var owner = new GameObject("original");
            var group = owner.AddComponent<SortingGroup>();
            var clone = Object.Instantiate(owner);
            GameObject sleeping = null;
            try
            {
                var copied = clone.GetComponent<SortingGroup>();
                Assert.NotEqual(group.index, copied.index);
                Assert.Same(copied, SortingGroup.GetSortingGroupByIndex(copied.index));
                owner.SetActive(false);
                sleeping = Object.Instantiate(owner);
                Assert.Equal(SortingGroup.invalidSortingGroupID, sleeping.GetComponent<SortingGroup>().index);
                sleeping.SetActive(true);
                var awakened = sleeping.GetComponent<SortingGroup>();
                Assert.Same(awakened, SortingGroup.GetSortingGroupByIndex(awakened.index));
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(clone);
                if (sleeping != null) Object.DestroyImmediate(sleeping);
            }
        }
    }
}
