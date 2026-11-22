using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Search;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Search providers, items and contexts as the Unity 6000.0.71f1 Editor has them
    /// (tests/projects/searchprovider).</summary>
    public sealed class SearchProviderTests
    {
        [Fact]
        public void AProviderStartsWithItsIdAsTypeAndFilterAndANameMadeOfItsWords()
        {
            var provider = new SearchProvider("my_probeProvider2");
            Assert.Equal("my_probeProvider2", provider.id);
            Assert.Equal("my_probeProvider2", provider.type);
            Assert.Equal("My Probe Provider 2", provider.name);
            Assert.Equal("my_probeProvider2:", provider.filterId);
            Assert.True(provider.active);
            Assert.Equal(100, provider.priority);
            Assert.False(provider.isExplicitProvider);
            Assert.Equal(ShowDetailsOptions.Default, provider.showDetailsOptions);
            Assert.Empty(provider.actions);
            Assert.Null(provider.fetchDescription);
            Assert.Null(provider.fetchPreview);
            Assert.Null(provider.toObject);
            Assert.Null(provider.fetchItems(null, new List<SearchItem>(), provider));
            Assert.Equal("L", provider.fetchLabel(new SearchItem("i") { label = "L" }, null));
            Assert.Equal("i", provider.fetchLabel(new SearchItem("i"), null));
            Assert.Equal("", provider.fetchLabel(new SearchItem(null), null));
            Assert.Equal("[my_probeProvider2] My Probe Provider 2 active=True", provider.ToString());
            Assert.Equal("Named", new SearchProvider("probe", "Named").name);
            Assert.Equal("A L L", new SearchProvider("ALL").name);
            Assert.Equal("id", Assert.Throws<ArgumentException>(() => new SearchProvider("")).ParamName);
            Assert.Throws<ArgumentException>(() => new SearchProvider(null));
        }

        [Fact]
        public void CreatedItemsBelongToTheProviderAndADebugSearchDescribesThem()
        {
            var provider = new SearchProvider("probe");
            var context = UnityEditor.Search.SearchService.CreateContext(provider, "text");
            var item = provider.CreateItem(context, "a", 7, "Label", "Description", null, 42);
            Assert.Equal(SearchItemOptions.Ellipsis | SearchItemOptions.Highlight, item.options);
            Assert.Same(provider, item.provider);
            Assert.Same(context, item.context);
            Assert.Equal(42, item.data);
            Assert.Equal("a", item.value);
            Assert.Equal("[7] Label [7] | a (Description)", item.ToString());
            Assert.Equal(0, provider.CreateItem(context, "a", "Label", null, null, null).score);
#pragma warning disable 618
            // Without a context the provider's own stands in.
            Assert.NotNull(provider.CreateItem("a").context);
            Assert.NotSame(context, provider.CreateItem("a").context);
#pragma warning restore 618

            var debug = UnityEditor.Search.SearchService.CreateContext(new[] { provider }, "text", SearchFlags.Debug);
            var described = provider.CreateItem(debug, "a", 7, null, null, null, "data");
            Assert.Equal("a (7)", described.label);
            Assert.Equal("DEBUG: id=a - label= - description= - thumbnail= - data=data", described.description);
        }

        [Fact]
        public void AnItemWithoutAProviderOfItsOwnHasTheDefaultOne()
        {
            var item = new SearchItem("fresh");
            Assert.Equal("default", item.provider.id);
            Assert.Equal(int.MinValue, item.provider.priority);
            Assert.Single(item.provider.actions);
            Assert.Equal(SearchItemOptions.None, item.options);
            Assert.Equal("[0] fresh", item.ToString());
            Assert.Null(item.ToObject());
            item.value = 5;
            Assert.Equal("[0] 5", item.ToString());
#pragma warning disable 618
            Assert.Same(SearchItem.none, SearchItem.clear);
#pragma warning restore 618
            Assert.Equal("None", SearchItem.clear.label);
            Assert.Equal(int.MinValue, SearchItem.clear.score);
            Assert.Equal(SearchItemOptions.CustomAction, SearchItem.clear.options);
        }

        [Fact]
        public void LabelsAndDescriptionsAreFetchedOnceUnlessTheItemsOptionsSayOtherwise()
        {
            int labels = 0, descriptions = 0;
            var provider = new SearchProvider("probe")
            {
                fetchLabel = (item, context) => "label" + ++labels,
                fetchDescription = (item, context) => "description" + ++descriptions,
            };
            var c = UnityEditor.Search.SearchService.CreateContext(provider);
            var plain = provider.CreateItem(c, "a");
            Assert.Equal("label1", plain.GetLabel(c));
            Assert.Equal("label1", plain.GetLabel(c));
            Assert.Equal("label1", plain.label);
            Assert.Equal("Given", provider.CreateItem(c, "a", "Given", null, null, null).GetLabel(c));
            Assert.Equal(1, labels);
            Assert.Equal("Bold &amp; it", provider.CreateItem(c, "a", "<b>Bold</b> &amp; <i>it</i>", null, null, null).GetLabel(c, true));
            Assert.Null(new SearchItem("a") { provider = null }.GetLabel(c));

            Assert.Equal("description1", plain.GetDescription(c));
            Assert.Equal("description1", plain.GetDescription(c));
            Assert.Equal("description1", plain.description);

            var refreshing = provider.CreateItem(c, "a");
            refreshing.options = SearchItemOptions.AlwaysRefresh;
            Assert.Equal("description2", refreshing.GetDescription(c));
            Assert.Equal("description3", refreshing.GetDescription(c));
            Assert.Null(refreshing.description);

            // A compacted or full-description item asks even when it was given a description.
            var compact = provider.CreateItem(c, "a", "Label", "Given", null, null);
            compact.options = SearchItemOptions.Compacted;
            Assert.Equal("description4", compact.GetDescription(c));
            var given = provider.CreateItem(c, "a", "Label", "Given", null, null);
            Assert.Equal("Given", given.GetDescription(c));

            var silent = new SearchProvider("silent");
            Assert.Null(silent.CreateItem(c, "a", "Label", null, null, null).GetDescription(c));
            var valued = silent.CreateItem(c, "a", "Label", null, null, null);
            valued.value = 12;
            Assert.Equal("12", valued.GetDescription(c));
            var compacted = silent.CreateItem(c, "a", "Label", null, null, null);
            compacted.options = SearchItemOptions.Compacted;
            Assert.Equal("Label", compacted.GetDescription(c));
        }

        [Fact]
        public void ThumbnailsPreviewsAndObjectsAreKeptOnlyAsAsked()
        {
            int thumbnails = 0, previews = 0, objects = 0;
            var texture = new Texture2D(4, 4);
            var target = new GameObject("Target");
            try
            {
                var provider = new SearchProvider("probe")
                {
                    fetchThumbnail = (item, context) => { thumbnails++; return item.id == "none" ? null : texture; },
                    fetchPreview = (item, context, size, options) => { previews++; return texture; },
                    toObject = (item, type) => { objects++; return type == typeof(Texture2D) ? (Object)texture : target; },
                };
                var c = UnityEditor.Search.SearchService.CreateContext(provider);
                var a = provider.CreateItem(c, "a");
                Assert.Same(texture, a.GetThumbnail(c));
                Assert.Null(a.thumbnail);
                Assert.Same(texture, a.GetThumbnail(c, true));
                Assert.Same(texture, a.thumbnail);
                Assert.Same(texture, a.GetThumbnail(c, true));
                Assert.Null(provider.CreateItem(c, "none").GetThumbnail(c, true));
                Assert.Equal(3, thumbnails);

                Assert.Same(texture, a.GetPreview(c, new Vector2(64, 64)));
                Assert.Null(a.preview);
                Assert.Same(texture, a.GetPreview(c, new Vector2(64, 64), FetchPreviewOptions.Normal, true));
                Assert.Same(texture, a.GetPreview(c, new Vector2(64, 64), FetchPreviewOptions.Normal, true));
                Assert.Equal(2, previews);
                Assert.Null(new SearchProvider("plain").CreateItem(c, "a").GetPreview(c, new Vector2(64, 64)));

                Assert.Same(target, a.ToObject());
                Assert.Same(target, a.ToObject());
                Assert.Same(texture, a.ToObject(typeof(Texture2D)));
                Assert.Same(texture, a.ToObject<Texture2D>());
                // An object of another type than asked for is none.
                Assert.Null(a.ToObject<Material>());
                Assert.Equal(3, objects);
            }
            finally
            {
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(target);
            }
        }

        [Fact]
        public void ItemsAreEqualAndOrderedByTheirIdAlone()
        {
            var p = new SearchProvider("probe");
            var q = new SearchProvider("other");
            var a = p.CreateItem((SearchContext)null, "same", 1, "A", null, null, null);
            var b = q.CreateItem((SearchContext)null, "same", 9, "B", null, null, "data");
            Assert.True(a.Equals(b));
            Assert.True(a.Equals((object)b));
            Assert.False(a.Equals(p.CreateItem((SearchContext)null, "Same")));
            Assert.False(a.Equals((object)"same"));
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
            var items = new[] { "b", "a", "B", "10", "9", "a b", "_" }.Select(id => p.CreateItem((SearchContext)null, id)).ToList();
            items.Sort();
            Assert.Equal("10,9,B,_,a,a b,b", string.Join(",", items.Select(i => i.id)));
            Assert.Equal(1, Math.Sign(p.CreateItem((SearchContext)null, "b").CompareTo((object)p.CreateItem((SearchContext)null, "a"))));
        }

        static SearchProvider[] Four() => new[]
        {
            new SearchProvider("late", "Late") { priority = 300 },
            new SearchProvider("probe", "Probe"),
            new SearchProvider("early", "Early") { priority = 5 },
            new SearchProvider("explicit", "Explicit") { isExplicitProvider = true },
        };

        static string Ids(SearchContext c) => string.Join(",", c.providers.Select(p => p.id));

        [Fact]
        public void AContextOrdersItsProvidersAndLeavesOutTheExplicitOnes()
        {
            var providers = Four();
            var c = UnityEditor.Search.SearchService.CreateContext(new[] { providers[0], providers[1], providers[0], providers[2], providers[3] }, null, SearchFlags.None);
            Assert.Equal("early,probe,late", Ids(c));
            Assert.Equal("", c.searchText);
            Assert.True(c.empty);
            Assert.Equal(SearchFlags.None, c.options);
            Assert.Equal(SearchFlags.Sorted, UnityEditor.Search.SearchService.CreateContext(providers).options);
            // A context of one provider asks it whatever it is.
            Assert.Equal("explicit", Ids(UnityEditor.Search.SearchService.CreateContext(providers[3], "x")));
            Assert.Equal("", Ids(UnityEditor.Search.SearchService.CreateContext(new SearchProvider[0], "x")));
            c.wantsMore = true;
            Assert.Equal(SearchFlags.WantsMore, c.options);
            c.wantsMore = false;
            Assert.Equal(SearchFlags.None, c.options);
        }

        [Theory]
        [InlineData("", "", 0, "", "", null, "early,probe,late")]
        [InlineData(" ", "", 0, "", "", null, "early,probe,late")]
        [InlineData("Hello  World ", "Hello  World", 0, "hello,world", "", null, "early,probe,late")]
        [InlineData("  lead", "lead", 2, "lead", "", null, "early,probe,late")]
        [InlineData("probe:", "", 0, "", "", "probe:", "probe")]
        [InlineData("PROBE: x", "x", 7, "x", "", "probe:", "probe")]
        [InlineData("explicit: x", "x", 10, "x", "", "explicit:", "explicit")]
        [InlineData("early:late: x", "late: x", 6, "x", "late:", "early:", "early")]
        [InlineData("unknown: x", "unknown: x", 0, "x", "unknown:", null, "early,probe,late")]
        [InlineData("t:Texture hello", "t:Texture hello", 0, "hello", "t:texture", null, "early,probe,late")]
        [InlineData("hello probe: x", "hello probe: x", 0, "hello,x", "probe:", null, "early,probe,late")]
        [InlineData("tab\tsep", "tab\tsep", 0, "tab\tsep", "", null, "early,probe,late")]
        public void TheSearchTextIsTakenApart(string text, string query, int offset, string words, string filters, string filterId, string providers)
        {
            var c = UnityEditor.Search.SearchService.CreateContext(Four(), text);
            Assert.Equal(text, c.searchText);
            Assert.Equal(query, c.searchQuery);
            Assert.Equal(offset, c.searchQueryOffset);
            Assert.Equal(words, string.Join(",", c.searchWords));
            Assert.Equal(filters, string.Join(",", c.textFilters));
            Assert.Equal(filterId, c.filterId);
            Assert.Equal(providers, Ids(c));
        }

        [Fact]
        public void ThePhraseIsKeptFromItsFirstReading()
        {
            var c = UnityEditor.Search.SearchService.CreateContext(Four(), "first");
            c.searchText = "probe: second Third";
            Assert.Equal("second third", c.searchPhrase);
            c.searchText = "plain";
            Assert.Equal("plain", c.searchQuery);
            Assert.Null(c.filterId);
            Assert.Equal("second third", c.searchPhrase);
        }

        [Fact]
        public void ProvidersAreEnabledByTheirFirstContextAndDisabledByTheirLast()
        {
            int enabled = 0, disabled = 0, others = 0;
            var p = new SearchProvider("probe") { onEnable = () => enabled++, onDisable = () => disabled++ };
            var q = new SearchProvider("other") { onEnable = () => others++, onDisable = () => others += 100 };
            var first = UnityEditor.Search.SearchService.CreateContext(p, "a");
            var second = UnityEditor.Search.SearchService.CreateContext(new[] { p, q }, "b");
            Assert.Equal((1, 0, 1), (enabled, disabled, others));
            // Filtering a provider out ends and begins the session of those left.
            second.SetFilter("probe", false);
            Assert.False(second.IsEnabled("probe"));
            Assert.Equal((1, 0, 102), (enabled, disabled, others));
            second.SetFilter("unknown", false);
            second.Dispose();
            second.Dispose();
            Assert.Equal((1, 0, 202), (enabled, disabled, others));
            first.Dispose();
            Assert.Equal((1, 1), (enabled, disabled));
            // A disposed context still answers.
            Assert.Equal("a", first.searchText);
            Assert.Single(first.providers);
        }

        [Fact]
        public void ASearchAsksEachProviderAndSortsOnlyWhenAsked()
        {
            var first = new SearchProvider("first") { priority = 10 };
            first.fetchItems = (context, items, provider) =>
            {
                items.Add(provider.CreateItem(context, "f.high", 100, "high", null, null, null));
                items.Add(provider.CreateItem(context, "f.b", 90, "b", null, null, null));
                items.Add(provider.CreateItem(context, "f.a", 90, "a", null, null, null));
                return null;
            };
            var asked = new List<string>();
            var second = new SearchProvider("second") { priority = 20 };
            second.fetchItems = (context, items, provider) =>
            {
                asked.Add(context.searchText + "/" + context.searchQuery);
                return new[] { provider.CreateItem(context, "s.low", 1, "low", null, null, null), provider.CreateItem(context, "f.a", 0, "same id", null, null, null) };
            };
            var broken = new SearchProvider("broken") { priority = 15, fetchItems = (context, items, provider) => throw new InvalidOperationException("probe") };
            var logs = new List<string>();
            Application.LogCallback onLog = (condition, _, type) => logs.Add(type + ":" + condition);
            Application.logMessageReceived += onLog;
            try
            {
                using var c = UnityEditor.Search.SearchService.CreateContext(new[] { second, broken, first }, "x", SearchFlags.Synchronous);
                Assert.Equal("f.high,f.b,f.a,s.low,f.a", string.Join(",", UnityEditor.Search.SearchService.GetItems(c, SearchFlags.Synchronous).Select(i => i.id)));
                Assert.Equal(SearchFlags.Synchronous, c.options);
                // The provider that threw is logged and the next one still asked.
                Assert.Equal(new[] { "Exception:InvalidOperationException: probe" }, logs);
                Assert.Equal(new[] { "x/x" }, asked);
                // Sorted: by provider priority, score and ID, the first of each ID.
                Assert.Equal("f.a:a,f.b:b,f.high:high,s.low:low", string.Join(",", UnityEditor.Search.SearchService.GetItems(c, SearchFlags.Sorted).Select(i => i.id + ":" + i.label)));
                Assert.Equal(SearchFlags.Synchronous | SearchFlags.Sorted, c.options);
            }
            finally
            {
                Application.logMessageReceived -= onLog;
            }
        }

        [Fact]
        public void AFilteredSearchAsksTheFilteredProviderAlone()
        {
            var asked = new List<string>();
            SearchProvider Make(string id)
            {
                var p = new SearchProvider(id);
                p.fetchItems = (context, items, provider) => { asked.Add(id + ":" + context.searchQuery); return null; };
                return p;
            }
            using var c = UnityEditor.Search.SearchService.CreateContext(new[] { Make("beta"), Make("alpha") }, "alpha: find", SearchFlags.Synchronous);
            Assert.Empty(UnityEditor.Search.SearchService.GetItems(c, SearchFlags.Synchronous));
            Assert.Equal(new[] { "alpha:find" }, asked);
        }

        [Fact]
        public void AnActionTakesItsContentAsGivenOrFromItsName()
        {
            var given = new SearchAction("probe", "my_action", new GUIContent("Text", "Tip"), (SearchItem item) => { });
            Assert.Equal("my_action", given.id);
            Assert.Equal("Tip", given.displayName);
            Assert.True(given.closeWindowAfterExecution);
            Assert.NotNull(given.handler);
            Assert.Null(given.execute);
            Assert.True(given.enabled(new SearchItem[0]));
            var named = new SearchAction("probe", "openThing", null, null, (SearchItem[] items) => { });
            Assert.Equal("Open Thing", named.content.text);
            Assert.Equal("openThing", named.displayName);
            Assert.Null(named.handler);
            Assert.NotNull(named.execute);
            Assert.Equal("Opens it", new SearchAction("probe", "openThing", null, "Opens it").displayName);
            Assert.Throws<NullReferenceException>(() => new SearchAction("probe", "x", (GUIContent)null).displayName);
        }
    }
}
