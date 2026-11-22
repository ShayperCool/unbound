using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Search;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Records, in Play mode, the data side of the Search API: what a SearchProvider made by each
/// constructor holds, the items CreateItem makes, how an item fetches and keeps its label,
/// description, thumbnail, preview and object, how items compare, what a SearchContext makes of
/// its providers and search text, and what a synchronous SearchService.GetItems returns.
/// </summary>
static class SearchProbe
{
    static readonly StringBuilder s_Output = new StringBuilder();
    static readonly List<string> s_Logs = new List<string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Record()
    {
        var directory = Environment.GetEnvironmentVariable("SEARCH_PROVIDER_OUT");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        Application.logMessageReceived += (condition, stackTrace, type) => s_Logs.Add(type + ":" + condition.Replace("\n", "\\n"));

        Providers();
        Actions();
        Items();
        Fetching();
        Comparing();
        Contexts();
        Searching();

        s_Output.Append("{\"k\":\"complete\",\"v\":true}\n");
        File.WriteAllText(Path.Combine(directory, "values.jsonl"), s_Output.ToString());
        EditorApplication.Exit(0);
    }

    static string Provider(SearchProvider p) => p == null ? "<null>" : string.Join("|", new[]
    {
        "id=" + Text(p.id), "type=" + Text(p.type), "name=" + Text(p.name), "active=" + p.active, "priority=" + p.priority, "filterId=" + Text(p.filterId),
        "explicit=" + p.isExplicitProvider, "showDetails=" + p.showDetails, "showDetailsOptions=" + p.showDetailsOptions,
        "actions=" + (p.actions == null ? "<null>" : p.actions.Count.ToString()),
        "fetchItems=" + Set(p.fetchItems), "fetchLabel=" + Set(p.fetchLabel), "fetchDescription=" + Set(p.fetchDescription), "fetchThumbnail=" + Set(p.fetchThumbnail),
        "fetchPreview=" + Set(p.fetchPreview), "fetchPropositions=" + Set(p.fetchPropositions), "fetchColumns=" + Set(p.fetchColumns), "startDrag=" + Set(p.startDrag),
        "trackSelection=" + Set(p.trackSelection), "toObject=" + Set(p.toObject), "onEnable=" + Set(p.onEnable), "onDisable=" + Set(p.onDisable),
        "contextual=" + Set(p.isEnabledForContextualSearch), "toString=" + Text(p.ToString()),
    });

    static void Providers()
    {
        Check("provider.id", () => Provider(new SearchProvider("probe")));
        Check("provider.idName", () => Provider(new SearchProvider("probe", "Probe Things")));
        Check("provider.idNullName", () => Provider(new SearchProvider("probe", (string)null)));
        Check("provider.listHandler", () => Provider(new SearchProvider("probe", (context, items, provider) => null)));
        Check("provider.simpleHandler", () => Provider(new SearchProvider("probe", (context, provider) => null)));
        Check("provider.nameListHandler", () => Provider(new SearchProvider("probe", "Named", (context, items, provider) => null)));
        Check("provider.nameSimpleHandler", () => Provider(new SearchProvider("probe", "Named", (context, provider) => null)));
        Check("provider.nullId", () => Provider(new SearchProvider(null)));
        Check("provider.names", () => string.Join("|", new[] { "probe", "my_probe_provider", "myProbeProvider", "MyProbe", "a.b-c d", "x", "", "ALLCAPS", "with2numbers" }
            .Select(id => Text(id) + "=>" + Try(() => Text(new SearchProvider(id).name)))));
        Check("provider.defaultFetchItems", () =>
        {
            var p = new SearchProvider("probe");
            var list = new List<SearchItem>();
            var result = p.fetchItems(SearchService.CreateContext(p), list, p);
            return "result=" + (result == null ? "<null>" : result.GetType().Name) + "|added=" + list.Count;
        });
        Check("provider.defaultFetchLabel", () =>
        {
            var p = new SearchProvider("probe");
            return string.Join("|", new[] { new SearchItem("i") { label = "L" }, new SearchItem("i"), new SearchItem(null) }.Select(i => Text(p.fetchLabel(i, null))));
        });
        Check("provider.defaultFetchThumbnail", () =>
        {
            var p = new SearchProvider("probe");
            var texture = p.fetchThumbnail(new SearchItem("i"), null);
            return texture == null ? "<null>" : "set";
        });
    }

    static string Action(SearchAction a) => string.Join("|", new[]
    {
        "id=" + Text(a.id), "displayName=" + Try(() => Text(a.displayName)), "text=" + Text(a.content?.text), "tooltip=" + Text(a.content?.tooltip),
        "image=" + (a.content?.image == null ? "<null>" : "set"), "close=" + a.closeWindowAfterExecution, "handler=" + Set(a.handler), "execute=" + Set(a.execute),
        "enabled=" + (a.enabled == null ? "<null>" : a.enabled(new SearchItem[0]).ToString()),
    });

    static void Actions()
    {
        var texture = new Texture2D(4, 4);
        Check("action.content", () => Action(new SearchAction("probe", "my_action", new GUIContent("Text", "Tip"))));
        Check("action.nullContent", () => Action(new SearchAction("probe", "my_action", (GUIContent)null)));
        Check("action.contentItem", () => Action(new SearchAction("probe", "my_action", new GUIContent("Text", "Tip"), (SearchItem item) => { })));
        Check("action.contentItems", () => Action(new SearchAction("probe", "my_action", new GUIContent("Text", "Tip"), (SearchItem[] items) => { })));
        Check("action.name", () => Action(new SearchAction("probe", "my_action")));
        Check("action.nameIconTip", () => Action(new SearchAction("probe", "openThing", texture, "Opens it")));
        Check("action.nameItem", () => Action(new SearchAction("probe", "my_action", texture, null, (SearchItem item) => { })));
        Check("action.nameItems", () => Action(new SearchAction("probe", "my_action", null, "Tip", (SearchItem[] items) => { })));
    }

    static string Item(SearchItem i, SearchProvider p, SearchContext c) => i == null ? "<null>" : string.Join("|", new[]
    {
        "id=" + Text(i.id), "score=" + i.score, "label=" + Text(i.label), "description=" + Text(i.description), "options=" + i.options,
        "thumbnail=" + (i.thumbnail == null ? "<null>" : "set"), "preview=" + (i.preview == null ? "<null>" : "set"), "data=" + (i.data == null ? "<null>" : i.data.ToString()),
        "provider=" + (i.provider == null ? "<null>" : i.provider == p ? "own" : Text(i.provider.id)), "context=" + (i.context == null ? "<null>" : i.context == c ? "own" : "other"),
        "value=" + (i.value == null ? "<null>" : i.value.ToString()), "toString=" + Text(i.ToString()),
    });

    static void Items()
    {
        var p = new SearchProvider("probe", "Probe");
        var c = SearchService.CreateContext(p, "text");
        var debug = SearchService.CreateContext(new[] { p }, "text", SearchFlags.Debug);
        var texture = new Texture2D(4, 4);
        Check("item.new", () =>
        {
            var i = new SearchItem("fresh");
            return Item(i, p, c) + "|providerName=" + Text(i.provider.name) + "|providerPriority=" + i.provider.priority + "|providerActions=" + i.provider.actions.Count;
        });
        Check("item.newNullId", () => Item(new SearchItem(null), p, c));
        Check("item.create.id", () => Item(p.CreateItem(c, "a"), p, c));
        Check("item.create.full", () => Item(p.CreateItem(c, "a", "Label", "Description", texture, "data"), p, c));
        Check("item.create.score", () => Item(p.CreateItem(c, "a", 7, "Label", "Description", texture, 42), p, c));
        Check("item.create.nulls", () => Item(p.CreateItem(c, "a", 7, null, null, null, null), p, c));
        Check("item.create.nullContext", () => Item(p.CreateItem(null, "a", 7, "Label", "Description", null, null), p, c));
        Check("item.create.debug", () => Item(p.CreateItem(debug, "a", 7, "Label", "Description", null, "data"), p, debug));
        Check("item.create.debugNoLabel", () => Item(p.CreateItem(debug, "a", 7, null, null, null, null), p, debug));
#pragma warning disable 618
        Check("item.create.oldId", () => Item(p.CreateItem("a"), p, c));
        Check("item.create.oldLabel", () => Item(p.CreateItem("a", "Label"), p, c));
        Check("item.create.oldScore", () => Item(p.CreateItem("a", 7, "Label", "Description", texture, "data"), p, c));
#pragma warning restore 618
        Check("item.create.noContextFull", () => Item(p.CreateItem("a", "Label", "Description", texture, "data"), p, c));
        Check("item.value", () =>
        {
            var i = p.CreateItem(c, "a");
            var before = i.value;
            i.value = 5;
            return "before=" + before + "|after=" + i.value + "|toString=" + Text(i.ToString());
        });
#pragma warning disable 618
        Check("item.clear", () => "same=" + ReferenceEquals(SearchItem.clear, SearchItem.none) + "|label=" + Text(SearchItem.clear.label) + "|score=" + SearchItem.clear.score
            + "|options=" + SearchItem.clear.options + "|provider=" + Text(SearchItem.clear.provider.id) + "|thumbnail=" + (SearchItem.clear.thumbnail == null ? "<null>" : "set"));
#pragma warning restore 618
    }

    static void Fetching()
    {
        int labels = 0, descriptions = 0, thumbnails = 0, previews = 0, objects = 0;
        var texture = new Texture2D(4, 4);
        var target = new GameObject("Target");
        var p = new SearchProvider("probe", "Probe")
        {
            fetchLabel = (item, context) => "label" + ++labels,
            fetchDescription = (item, context) => "description" + ++descriptions,
            fetchThumbnail = (item, context) => { thumbnails++; return item.id == "none" ? null : texture; },
            fetchPreview = (item, context, size, options) => { previews++; return item.id == "none" ? null : texture; },
            toObject = (item, type) => { objects++; return type == typeof(Texture2D) ? (Object)texture : target; },
        };
        var c = SearchService.CreateContext(p);
        Check("fetch.label", () =>
        {
            labels = 0;
            var i = p.CreateItem(c, "a");
            var first = i.GetLabel(c);
            var second = i.GetLabel(c);
            var kept = p.CreateItem(c, "a", "Given", null, null, null).GetLabel(c);
            return "first=" + Text(first) + "|second=" + Text(second) + "|field=" + Text(i.label) + "|given=" + Text(kept) + "|calls=" + labels;
        });
        Check("fetch.labelHtml", () =>
        {
            var i = p.CreateItem(c, "a", "<b>Bold</b> and <color=red>red</color> &amp; <i>it</i>", null, null, null);
            return "plain=" + Text(i.GetLabel(c)) + "|stripped=" + Text(i.GetLabel(c, true)) + "|empty=" + Text(p.CreateItem(c, "a", "", null, null, null).GetLabel(c, true));
        });
        Check("fetch.labelNoProvider", () => Text(new SearchItem("a") { provider = null }.GetLabel(c)));
        Check("fetch.description", () =>
        {
            descriptions = 0;
            var i = p.CreateItem(c, "a");
            var first = i.GetDescription(c);
            var second = i.GetDescription(c);
            return "first=" + Text(first) + "|second=" + Text(second) + "|field=" + Text(i.description) + "|calls=" + descriptions;
        });
        foreach (var options in new[] { SearchItemOptions.AlwaysRefresh, SearchItemOptions.Compacted, SearchItemOptions.FullDescription, SearchItemOptions.Ellipsis, SearchItemOptions.RightToLeft, SearchItemOptions.Highlight, SearchItemOptions.FuzzyHighlight, SearchItemOptions.CustomAction })
        {
            var o = options;
            Check("fetch.description." + o, () =>
            {
                descriptions = 0;
                var i = p.CreateItem(c, "a", "Label", "Given", null, null);
                i.options = o;
                var given = i.GetDescription(c);
                var j = p.CreateItem(c, "a", "Label", null, null, null);
                j.options = o;
                var first = j.GetDescription(c);
                var second = j.GetDescription(c);
                return "given=" + Text(given) + "|first=" + Text(first) + "|second=" + Text(second) + "|field=" + Text(j.description) + "|calls=" + descriptions;
            });
        }
        Check("fetch.descriptionNoFetch", () =>
        {
            var plain = new SearchProvider("plain");
            var none = plain.CreateItem(c, "a", "Label", null, null, null);
            var valued = plain.CreateItem(c, "a", "Label", null, null, null);
            valued.value = 12;
            var compact = plain.CreateItem(c, "a", "Label", null, null, null);
            compact.options = SearchItemOptions.Compacted;
            var html = plain.CreateItem(c, "a", "Label", "<b>D</b>", null, null);
            return "none=" + Text(none.GetDescription(c)) + "|valued=" + Text(valued.GetDescription(c)) + "|compact=" + Text(compact.GetDescription(c)) + "|stripped=" + Text(html.GetDescription(c, true));
        });
        Check("fetch.thumbnail", () =>
        {
            thumbnails = 0;
            var i = p.CreateItem(c, "a");
            var plain = i.GetThumbnail(c);
            var fieldAfterPlain = i.thumbnail;
            var cached = i.GetThumbnail(c, true);
            var fieldAfterCached = i.thumbnail;
            var again = i.GetThumbnail(c, true);
            var none = p.CreateItem(c, "none").GetThumbnail(c, true);
            return "plain=" + (plain == texture) + "|fieldAfterPlain=" + (fieldAfterPlain == null ? "<null>" : "set") + "|cached=" + (cached == texture)
                + "|fieldAfterCached=" + (fieldAfterCached == null ? "<null>" : "set") + "|again=" + (again == texture) + "|none=" + (none == null ? "<null>" : "set") + "|calls=" + thumbnails;
        });
        Check("fetch.preview", () =>
        {
            previews = 0;
            var i = p.CreateItem(c, "a");
            var plain = i.GetPreview(c, new Vector2(64, 64));
            var fieldAfterPlain = i.preview;
            var cached = i.GetPreview(c, new Vector2(64, 64), FetchPreviewOptions.Normal, true);
            var again = i.GetPreview(c, new Vector2(64, 64), FetchPreviewOptions.Normal, true);
            var none = new SearchProvider("plain").CreateItem(c, "a").GetPreview(c, new Vector2(64, 64));
            return "plain=" + (plain == texture) + "|fieldAfterPlain=" + (fieldAfterPlain == null ? "<null>" : "set") + "|cached=" + (cached == texture)
                + "|field=" + (i.preview == null ? "<null>" : "set") + "|again=" + (again == texture) + "|noFetch=" + (none == null ? "<null>" : "set") + "|calls=" + previews;
        });
        Check("fetch.object", () =>
        {
            objects = 0;
            var i = p.CreateItem(c, "a");
            var first = i.ToObject();
            var second = i.ToObject();
            var typed = i.ToObject(typeof(Texture2D));
            var generic = i.ToObject<Texture2D>();
            var wrong = i.ToObject<Material>();
            var none = new SearchProvider("plain").CreateItem(c, "a").ToObject();
            var fresh = new SearchItem("fresh").ToObject();
            return "first=" + Name(first) + "|second=" + Name(second) + "|typed=" + Name(typed) + "|generic=" + Name(generic) + "|wrong=" + Name(wrong)
                + "|noHandler=" + Name(none) + "|fresh=" + Name(fresh) + "|calls=" + objects;
        });
    }

    static void Comparing()
    {
        var p = new SearchProvider("probe");
        var q = new SearchProvider("other");
        var c = SearchService.CreateContext(p);
        Check("compare.equals", () =>
        {
            var a = p.CreateItem(c, "same", 1, "A", null, null, null);
            var b = q.CreateItem(c, "same", 9, "B", null, null, "data");
            var d = p.CreateItem(c, "Same");
            return "sameId=" + a.Equals(b) + "|object=" + a.Equals((object)b) + "|case=" + a.Equals(d) + "|other=" + a.Equals((object)"same") + "|hash=" + (a.GetHashCode() == b.GetHashCode())
                + "|hashIsId=" + (a.GetHashCode() == "same".GetHashCode());
        });
        Check("compare.order", () =>
        {
            var ids = new[] { "b", "a", "B", "10", "9", "a b", "_" };
            var items = ids.Select((id, n) => p.CreateItem(c, id, 100 - n, null, null, null, null)).ToList();
            items.Sort();
            var sorted = string.Join(",", items.Select(i => i.id));
            return "sorted=" + sorted + "|ab=" + Math.Sign(p.CreateItem(c, "a").CompareTo(p.CreateItem(c, "b"))) + "|object=" + Math.Sign(p.CreateItem(c, "b").CompareTo((object)p.CreateItem(c, "a")))
                + "|same=" + p.CreateItem(c, "a", 1, null, null, null, null).CompareTo(p.CreateItem(c, "a", 2, null, null, null, null));
        });
        Check("compare.toString", () => string.Join("|", new[]
        {
            Text(p.CreateItem(c, "a", 3, null, null, null, null).ToString()),
            Text(p.CreateItem(c, "a", 3, "", "D", null, null).ToString()),
            Text(p.CreateItem(c, "a", 3, "L", null, null, null).ToString()),
            Text(p.CreateItem(c, "a", 3, "L", "", null, null).ToString()),
            Text(p.CreateItem(c, "a", 3, "L", "D", null, null).ToString()),
        }));
    }

    static string Context(SearchContext c) => c == null ? "<null>" : string.Join("|", new[]
    {
        "text=" + Text(c.searchText), "query=" + Text(c.searchQuery), "offset=" + c.searchQueryOffset, "words=[" + string.Join(",", c.searchWords.Select(Text)) + "]",
        "phrase=" + Text(c.searchPhrase), "filters=[" + string.Join(",", c.textFilters.Select(Text)) + "]", "options=" + c.options, "wantsMore=" + c.wantsMore, "empty=" + c.empty,
        "filterId=" + Text(c.filterId), "providers=[" + string.Join(",", c.providers.Select(p => p.id)) + "]", "inProgress=" + c.searchInProgress,
    });

    static void Contexts()
    {
        Func<SearchProvider[]> make = () => new[]
        {
            new SearchProvider("late", "Late") { priority = 300 },
            new SearchProvider("probe", "Probe"),
            new SearchProvider("early", "Early") { priority = 5 },
            new SearchProvider("explicit", "Explicit") { isExplicitProvider = true },
        };
        Check("context.provider", () => Context(SearchService.CreateContext(make()[1])));
        Check("context.providerText", () => Context(SearchService.CreateContext(make()[1], "Hello World")));
        Check("context.explicitAlone", () => Context(SearchService.CreateContext(make()[3], "x")));
        Check("context.providers", () => Context(SearchService.CreateContext(make(), "Hello World")));
        Check("context.providersDefaults", () => Context(SearchService.CreateContext((IEnumerable<SearchProvider>)make())));
        Check("context.flags", () => Context(SearchService.CreateContext(make(), "x", SearchFlags.Synchronous | SearchFlags.WantsMore)));
        Check("context.openPicker", () => Context(SearchService.CreateContext(make(), string.Empty, SearchFlags.OpenPicker)));
        Check("context.nullText", () => Context(SearchService.CreateContext(make(), null, SearchFlags.None)));
        Check("context.duplicates", () =>
        {
            var providers = make();
            return Context(SearchService.CreateContext(new[] { providers[0], providers[1], providers[0], providers[2] }, ""));
        });
        Check("context.empty", () => Context(SearchService.CreateContext(new SearchProvider[0], "x")));
        Check("context.constructors", () =>
        {
            var providers = make();
            return "three:" + Context(new SearchContext(providers, "a", SearchFlags.None)) + "|two:" + Context(new SearchContext(providers, "a")) + "|one:" + Context(new SearchContext(providers))
                + "|copy:" + Context(new SearchContext(new SearchContext(providers, "copied", SearchFlags.WantsMore)));
        });
        foreach (var text in new[] { "", " ", "hello", "Hello  World ", "  lead", "probe:", "probe: hello", "probe:hello World", "PROBE: x", "explicit: x", "early:late: x", "unknown: x",
                     "t:Texture hello", "a:b c:d", "\"quoted words\" tail", "tab\tsep", "hello probe: x", "h=1 size>5 name" })
        {
            var t = text;
            Check("context.text:" + t.Replace("\t", "\\t"), () => Context(SearchService.CreateContext(make(), t)));
        }
        Check("context.setText", () =>
        {
            var c = SearchService.CreateContext(make(), "first");
            c.searchText = "probe: second Third";
            var filtered = Context(c);
            c.searchText = "plain";
            return "filtered:" + filtered + "|plain:" + Context(c);
        });
        Check("context.wantsMore", () =>
        {
            var c = SearchService.CreateContext(make(), "x");
            c.wantsMore = true;
            var on = c.options;
            c.wantsMore = false;
            c.options = SearchFlags.Packages;
            return "on=" + on + "|off=" + c.options + "|wantsMore=" + c.wantsMore;
        });
        Check("context.setFilter", () =>
        {
            var c = SearchService.CreateContext(make(), "x");
            var before = "probe=" + c.IsEnabled("probe") + "|unknown=" + c.IsEnabled("unknown");
            c.SetFilter("probe", false);
            var removed = string.Join(",", c.providers.Select(p => p.id)) + ":" + c.IsEnabled("probe");
            c.SetFilter("probe", true);
            c.SetFilter("unknown", true);
            c.SetFilter("unknown", false);
            return before + "|removed=" + removed + "|again=" + string.Join(",", c.providers.Select(p => p.id));
        });
        Check("context.setFilterEnabling", () =>
        {
            int enabled = 0, disabled = 0, others = 0;
            var p = new SearchProvider("probe") { onEnable = () => enabled++, onDisable = () => disabled++ };
            var q = new SearchProvider("other") { onEnable = () => others++, onDisable = () => others += 100 };
            var c = SearchService.CreateContext(new[] { p, q }, "x");
            var made = enabled + "/" + disabled + "/" + others;
            c.SetFilter("probe", false);
            var removed = enabled + "/" + disabled + "/" + others;
            c.Dispose();
            return "made=" + made + "|removed=" + removed + "|disposed=" + enabled + "/" + disabled + "/" + others;
        });
        Check("context.disposed", () =>
        {
            var c = SearchService.CreateContext(make(), "x");
            c.Dispose();
            c.Dispose();
            return "text=" + Try(() => Text(c.searchText)) + "|providers=" + Try(() => c.providers.Count().ToString()) + "|phrase=" + Try(() => Text(c.searchPhrase))
                + "|isEnabled=" + Try(() => c.IsEnabled("probe").ToString()) + "|setText=" + Try(() => { c.searchText = "y"; return "set"; }) + "|options=" + Try(() => c.options.ToString());
        });
        Check("context.enabling", () =>
        {
            int enabled = 0, disabled = 0;
            var p = new SearchProvider("probe") { onEnable = () => enabled++, onDisable = () => disabled++ };
            var first = SearchService.CreateContext(p, "a");
            var afterFirst = enabled + "/" + disabled;
            var second = SearchService.CreateContext(new[] { p }, "b");
            var afterSecond = enabled + "/" + disabled;
            second.Dispose();
            var afterDispose = enabled + "/" + disabled;
            first.Dispose();
            return "single=" + afterFirst + "|list=" + afterSecond + "|listDisposed=" + afterDispose + "|singleDisposed=" + enabled + "/" + disabled;
        });
    }

    static void Searching()
    {
        var texts = new List<string>();
        Func<string, int, bool, SearchProvider> make = (id, priority, yields) =>
        {
            var p = new SearchProvider(id, id.ToUpperInvariant()) { priority = priority };
            p.fetchItems = (context, items, provider) =>
            {
                texts.Add(id + ":" + context.searchText + ":" + context.searchQuery);
                if (yields) return new[] { "z", "m" }.Select((name, n) => provider.CreateItem(context, id + "." + name, 50 - n, name + " of " + id, null, null, null));
                items.Add(provider.CreateItem(context, id + ".listed", 10, "listed of " + id, null, null, null));
                items.Add(provider.CreateItem(context, id + ".also", -5, "also of " + id, null, null, null));
                return null;
            };
            return p;
        };
        Func<List<SearchItem>, string> list = items => items == null ? "<null>" : string.Join(",", items.Select(i => i.id + "(" + i.score + ")"));
        Step("search.synchronous", () =>
        {
            texts.Clear();
            using (var c = SearchService.CreateContext(new[] { make("beta", 200, true), make("alpha", 50, false) }, "find me", SearchFlags.Synchronous))
                return "items=" + list(SearchService.GetItems(c, SearchFlags.Synchronous)) + "|asked=" + string.Join(",", texts);
        });
        Step("search.sorted", () =>
        {
            texts.Clear();
            using (var c = SearchService.CreateContext(new[] { make("beta", 200, true), make("alpha", 50, false) }, "find me", SearchFlags.Synchronous))
                return "items=" + list(SearchService.GetItems(c, SearchFlags.Synchronous | SearchFlags.Sorted)) + "|asked=" + string.Join(",", texts);
        });
        Step("search.sortedAcross", () =>
        {
            // The earlier provider's items have the higher scores, and two share a score.
            var first = new SearchProvider("first") { priority = 10 };
            first.fetchItems = (context, items, provider) =>
            {
                items.Add(provider.CreateItem(context, "f.high", 100, "high", null, null, null));
                items.Add(provider.CreateItem(context, "f.b", 90, "b", null, null, null));
                items.Add(provider.CreateItem(context, "f.a", 90, "a", null, null, null));
                return null;
            };
            var second = new SearchProvider("second") { priority = 20 };
            second.fetchItems = (context, items, provider) => new[] { provider.CreateItem(context, "s.low", 1, "low", null, null, null), provider.CreateItem(context, "f.a", 0, "same id", null, null, null) };
            using (var c = SearchService.CreateContext(new[] { second, first }, "x", SearchFlags.Synchronous))
            {
                var unsorted = SearchService.GetItems(c, SearchFlags.Synchronous);
                var plain = list(unsorted);
                var options = c.options;
                return "unsorted=" + plain + "|optionsAfter=" + options + "|sorted=" + string.Join(",", SearchService.GetItems(c, SearchFlags.Sorted).Select(i => i.id + ":" + i.label + "(" + i.score + ")"))
                    + "|optionsAfterSorted=" + c.options;
            }
        });
        Step("search.defaultOptions", () =>
        {
            texts.Clear();
            using (var c = SearchService.CreateContext(new[] { make("beta", 200, true), make("alpha", 50, false) }, "x"))
                return "items=" + list(SearchService.GetItems(c)) + "|options=" + c.options;
        });
        Step("search.filtered", () =>
        {
            texts.Clear();
            using (var c = SearchService.CreateContext(new[] { make("beta", 200, true), make("alpha", 50, false) }, "alpha: find", SearchFlags.Synchronous))
                return "items=" + list(SearchService.GetItems(c, SearchFlags.Synchronous)) + "|asked=" + string.Join(",", texts);
        });
        Step("search.inactive", () =>
        {
            texts.Clear();
            var quiet = make("alpha", 50, false);
            quiet.active = false;
            using (var c = SearchService.CreateContext(new[] { make("beta", 200, true), quiet }, "x", SearchFlags.Synchronous))
                return "items=" + list(SearchService.GetItems(c, SearchFlags.Synchronous)) + "|asked=" + string.Join(",", texts);
        });
        Step("search.throws", () =>
        {
            texts.Clear();
            var broken = new SearchProvider("broken") { fetchItems = (context, items, provider) => throw new InvalidOperationException("probe") };
            using (var c = SearchService.CreateContext(new[] { broken, make("alpha", 50, false) }, "x", SearchFlags.Synchronous))
                return "items=" + list(SearchService.GetItems(c, SearchFlags.Synchronous)) + "|asked=" + string.Join(",", texts);
        });
        Step("search.duplicateIds", () =>
        {
            var twice = new SearchProvider("twice");
            twice.fetchItems = (context, items, provider) =>
            {
                items.Add(provider.CreateItem(context, "same", 1, "one", null, null, null));
                items.Add(provider.CreateItem(context, "same", 2, "two", null, null, null));
                return null;
            };
            using (var c = SearchService.CreateContext(new[] { twice }, "x", SearchFlags.Synchronous))
                return "items=" + string.Join(",", SearchService.GetItems(c, SearchFlags.Synchronous).Select(i => i.id + ":" + i.label));
        });
        Step("search.registered", () => "probe=" + (SearchService.GetProvider("probe") == null ? "<null>" : "found") + "|null=" + Try(() => SearchService.GetProvider(null) == null ? "<null>" : "found"));
    }

    static void Step(string key, Func<string> read)
    {
        Check(key, () =>
        {
            s_Logs.Clear();
            var value = Try(read);
            return value + "|logs=" + (s_Logs.Count == 0 ? "<none>" : string.Join("||", s_Logs));
        });
    }

    static string Try(Func<string> read)
    {
        try { return read(); }
        catch (Exception e) { return "throws " + e.GetType().Name; }
    }

    static string Set(Delegate d) => d == null ? "<null>" : "set";

    static string Name(Object o) => o == null ? "<null>" : o.GetType().Name + ":" + o.name;

    static string Text(string value) => value == null ? "<null>" : "\"" + value + "\"";

    static void Check(string key, Func<string> read)
    {
        string value;
        try { value = read(); }
        catch (Exception e) { value = "throws " + e.GetType().Name + ":" + e.Message; }
        s_Output.Append("{\"k\":").Append(Quote(key)).Append(",\"v\":").Append(Quote(value)).Append("}\n");
    }

    static string Quote(string s)
    {
        var sb = new StringBuilder(s.Length + 2).Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }
}
