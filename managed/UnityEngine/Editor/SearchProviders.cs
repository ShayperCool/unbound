using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityEditor.Search
{
    /// <summary>How a search runs and what its window shows.</summary>
    [Flags]
    public enum SearchFlags
    {
        None = 0,
        Synchronous = 1,
        Sorted = 2,
        FirstBatchAsync = 4,
        WantsMore = 8,
        Debug = 16,
        NoIndexing = 32,
        Expression = 64,
        QueryString = 128,
        Packages = 256,
        Default = 2,
        AllProvidersAvailable = 4194304,
        UseSessionSettings = 8388608,
        ShowErrorsWithResults = 16777216,
        [Obsolete("Not used anymore", false)] SaveFilters = 33554432,
        ReuseExistingWindow = 67108864,
        Multiselect = 134217728,
        Dockable = 268435456,
        FocusContext = 536870912,
        HidePanels = 1073741824,
        GeneralSearchWindow = -2147483648,
        OpenDefault = -1744830464,
        OpenGlobal = -1677721600,
        OpenContextual = 939524096,
        OpenPicker = 536870912,
    }

    /// <summary>How an item is shown and when its description is fetched.</summary>
    [Flags]
    public enum SearchItemOptions
    {
        None = 0,
        Ellipsis = 1,
        RightToLeft = 2,
        Highlight = 4,
        FuzzyHighlight = 8,
        Compacted = 16,
        AlwaysRefresh = 32,
        FullDescription = 64,
        CustomAction = 128,
    }

    /// <summary>What a provider's details panel shows.</summary>
    [Flags]
    public enum ShowDetailsOptions
    {
        None = 0,
        Preview = 1,
        Inspector = 2,
        Actions = 4,
        Description = 8,
        ListView = 16,
        DefaultGroup = 32,
        InspectorWithoutHeader = 64,
        Default = 13,
    }

    /// <summary>The kind and size of preview a provider is asked for.</summary>
    [Flags]
    public enum FetchPreviewOptions
    {
        None = 0,
        Preview2D = 1,
        Preview3D = 2,
        Normal = 1024,
        Large = 2048,
    }

    /// <summary>The text rules the Search types share (tests/projects/searchprovider
    /// provider.names, fetch.labelHtml, context.text:*).</summary>
    static class SearchText
    {
        static readonly Regex s_WordBreaks = new Regex(@"-+|_+|\s+|(?<!^)(?=[A-Z0-9])");
        static readonly Regex s_Tags = new Regex("<.*?>");
        static readonly Regex s_Blanks = new Regex(@"(\s\s+)|(\r\n|\r|\n)+");

        /// <summary>An identifier as words: split at dashes, underscores, blanks and before
        /// every capital and digit, each word's first letter raised.</summary>
        internal static string Words(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return "";
            var words = new List<string>();
            foreach (var word in s_WordBreaks.Split(identifier))
                if (!string.IsNullOrWhiteSpace(word)) words.Add(char.ToUpperInvariant(word[0]) + word.Substring(1));
            return string.Join(" ", words);
        }

        /// <summary>The text without its markup tags (entities stay).</summary>
        internal static string WithoutTags(string text) => s_Tags.Replace(text, "");

        /// <summary>Runs of blanks and line breaks as one space.</summary>
        internal static string OneSpaced(string text) => s_Blanks.Replace(text, " ").Replace("\r\n", " ").Replace('\n', ' ');
    }

    /// <summary>Something a search item can be asked to do.</summary>
    public partial class SearchAction
    {
        public bool closeWindowAfterExecution = true;
        internal string providerId;
        public GUIContent content;
        public Func<IReadOnlyCollection<SearchItem>, bool> enabled;
        public Action<SearchItem[]> execute;
        public Action<SearchItem> handler;

        public SearchAction(string providerId, string id, GUIContent content)
        {
            this.providerId = providerId;
            this.id = id;
            this.content = content;
            enabled = items => true;
        }

        public SearchAction(string providerId, string id, GUIContent content, Action<SearchItem[]> handler) : this(providerId, id, content) => execute = handler;

        public SearchAction(string providerId, string id, GUIContent content, Action<SearchItem> handler) : this(providerId, id, content) => this.handler = handler;

        public SearchAction(string providerId, string name, Texture2D icon, string tooltip, Action<SearchItem[]> handler)
            : this(providerId, name, new GUIContent(SearchText.Words(name), icon, tooltip ?? name), handler) { }

        public SearchAction(string providerId, string name, Texture2D icon, string tooltip, Action<SearchItem> handler)
            : this(providerId, name, new GUIContent(SearchText.Words(name), icon, tooltip ?? name), handler) { }

        public SearchAction(string providerId, string name, Texture2D icon = null, string tooltip = null)
            : this(providerId, name, new GUIContent(SearchText.Words(name), icon, tooltip ?? name)) { }

        public string id { get; private set; }

        public string displayName => content.tooltip;
    }

    /// <summary>
    /// A source of search items: its identity, how it is listed and the handlers a search and its
    /// items call, as the Unity 6000.0.71f1 Editor makes one (tests/projects/searchprovider
    /// provider.*). Unbound runs the handlers of the providers a context is made with
    /// (<see cref="SearchService.GetItems"/>); it has no Search window and registers no providers
    /// of its own.
    /// </summary>
    [Serializable]
    public partial class SearchProvider : ISerializationCallbackReceiver
    {
        [NonSerialized] internal int m_EnableLockCounter;
        [NonSerialized] internal SearchContext defaultContext;
        public int priority;
        public bool active;
        public string filterId;
        public bool isExplicitProvider;
        public bool showDetails;
        public ShowDetailsOptions showDetailsOptions = ShowDetailsOptions.Default;
        public Func<SearchContext, List<SearchItem>, SearchProvider, object> fetchItems;
        public Func<SearchItem, SearchContext, string> fetchLabel;
        public Func<SearchItem, SearchContext, string> fetchDescription;
        public Func<SearchItem, SearchContext, Texture2D> fetchThumbnail;
        public Func<SearchItem, SearchContext, Vector2, FetchPreviewOptions, Texture2D> fetchPreview;
        public Action<SearchItem, SearchContext> startDrag;
        public Action<SearchItem, SearchContext> trackSelection;
        public Func<SearchItem, Type, Object> toObject;
        public Action onEnable;
        public Action onDisable;
        public Func<bool> isEnabledForContextualSearch;

        public SearchProvider(string id) : this(id, null, (Func<SearchContext, List<SearchItem>, SearchProvider, object>)null) { }

        public SearchProvider(string id, string displayName) : this(id, displayName, (Func<SearchContext, List<SearchItem>, SearchProvider, object>)null) { }

        public SearchProvider(string id, Func<SearchContext, List<SearchItem>, SearchProvider, object> fetchItemsHandler) : this(id, null, fetchItemsHandler) { }

        public SearchProvider(string id, Func<SearchContext, SearchProvider, object> fetchItemsHandler)
            : this(id, null, (context, items, provider) => fetchItemsHandler(context, provider)) { }

        public SearchProvider(string id, string displayName, Func<SearchContext, SearchProvider, object> fetchItemsHandler)
            : this(id, displayName, (context, items, provider) => fetchItemsHandler(context, provider)) { }

        public SearchProvider(string id, string displayName, Func<SearchContext, List<SearchItem>, SearchProvider, object> fetchItemsHandler)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("provider id must be non-empty", nameof(id));
            this.id = id;
            type = id;
            active = true;
            name = displayName ?? SearchText.Words(id);
            actions = new List<SearchAction>();
            fetchItems = fetchItemsHandler ?? ((context, items, provider) => null);
            fetchThumbnail = (item, context) => EditorIcons.Load(DefaultIcon, logError: false);
            fetchLabel = (item, context) => item.label ?? item.id ?? "";
            priority = 100;
            filterId = id + ":";
            defaultContext = new SearchContext(this);
        }

        /// <summary>What a provider without a thumbnail handler of its own shows (the Editor has
        /// its Search icon there; which image it is is not measured).</summary>
        const string DefaultIcon = "Search Icon";

        public string id { get; private set; }

        public string type { get; set; }

        public string name { get; private set; }

        public List<SearchAction> actions { get; internal set; }

        public SearchItem CreateItem(SearchContext context, string id, int score, string label, string description, Texture2D thumbnail, object data)
        {
            // A context searching with the Debug flag gets items that describe themselves.
            if (context != null && (context.options & SearchFlags.Debug) != 0)
            {
                description = $"DEBUG: id={id} - label={label} - description={description} - thumbnail={thumbnail} - data={data}";
                label = $"{label ?? id} ({score})";
            }
            return new SearchItem(id)
            {
                score = score,
                label = label,
                description = description,
                options = SearchItemOptions.Highlight | SearchItemOptions.Ellipsis,
                thumbnail = thumbnail,
                data = data,
                provider = this,
                context = context,
            };
        }

        [Obsolete("Use CreateItem which take a SearchContext", false)]
        public SearchItem CreateItem(string id, int score, string label, string description, Texture2D thumbnail, object data) =>
            CreateItem(defaultContext, id, score, label, description, thumbnail, data);

        public SearchItem CreateItem(SearchContext context, string id) => CreateItem(context, id, 0, null, null, null, null);

        [Obsolete("Use CreateItem which take a SearchContext", false)]
        public SearchItem CreateItem(string id) => CreateItem(defaultContext, id, 0, null, null, null, null);

        [Obsolete("Use CreateItem which take a SearchContext", false)]
        public SearchItem CreateItem(string id, string label) => CreateItem(defaultContext, id, 0, label, null, null, null);

        public SearchItem CreateItem(string id, string label, string description, Texture2D thumbnail, object data) =>
            CreateItem(defaultContext, id, 0, label, description, thumbnail, data);

        public SearchItem CreateItem(SearchContext context, string id, string label, string description, Texture2D thumbnail, object data) =>
            CreateItem(context, id, 0, label, description, thumbnail, data);

        /// <summary>A context began using the provider: the first one enables it.</summary>
        internal void Enabled()
        {
            if (m_EnableLockCounter++ == 0) onEnable?.Invoke();
        }

        /// <summary>A context of the provider ended: the last one disables it.</summary>
        internal void Disabled()
        {
            if (--m_EnableLockCounter == 0) onDisable?.Invoke();
        }

        public override string ToString() => $"[{id}] {name} active={active}";

        const string NotKept = "Unbound keeps no search provider over a script reload: there is nothing to prepare or restore";

        [Unbound.EditorFaithful(NotKept)]
        void ISerializationCallbackReceiver.OnBeforeSerialize() { }

        [Unbound.EditorFaithful(NotKept)]
        void ISerializationCallbackReceiver.OnAfterDeserialize() { }
    }

    /// <summary>
    /// One result of a search: its identity, what it shows and the provider that answers for
    /// it. What it is not given it asks of its provider, and keeps as the Unity 6000.0.71f1
    /// Editor keeps it (tests/projects/searchprovider item.*, fetch.*, compare.*).
    /// </summary>
    public partial class SearchItem : IEquatable<SearchItem>, IComparable<SearchItem>, IComparable
    {
        public readonly string id;
        public int score;
        public string label;
        public string description;
        public SearchItemOptions options;
        public Texture2D thumbnail;
        public Texture2D preview;
        public object data;
        public SearchProvider provider;
        public SearchContext context;

        object m_Value;
        Dictionary<Type, Object> m_Objects;

        /// <summary>The provider of an item made without one.</summary>
        static readonly SearchProvider s_DefaultProvider = new SearchProvider("default", "Default")
        {
            priority = int.MinValue,
            toObject = (item, type) => null,
            fetchLabel = (item, context) => item.label ?? item.id,
            fetchThumbnail = (item, context) => item.thumbnail ?? EditorIcons.Load("console.infoicon", logError: false),
            actions = new List<SearchAction> { new SearchAction("", "select", new GUIContent("select")) { handler = item => { } } },
        };

        [Obsolete("Use SearchItem.clear instead.", false)]
        public static readonly SearchItem none = new SearchItem(Guid.NewGuid().ToString())
        {
            label = "None",
            score = int.MinValue,
            provider = s_DefaultProvider,
            options = SearchItemOptions.CustomAction,
        };

        public SearchItem(string _id)
        {
            id = _id;
            provider = s_DefaultProvider;
        }

        /// <summary>The item's value: its ID until one is set.</summary>
        public object value
        {
            get => m_Value ?? id;
            set => m_Value = value;
        }

        /// <summary>The item that stands for "nothing" in a picker.</summary>
        public static SearchItem clear
        {
            get
            {
#pragma warning disable 618
                if (none.thumbnail == null) none.thumbnail = EditorIcons.Load("clear", logError: false);
                return none;
#pragma warning restore 618
            }
        }

        /// <summary>The label, asked of the provider once when the item has none.</summary>
        public string GetLabel(SearchContext context, bool stripHTML = false)
        {
            if (label == null) label = provider?.fetchLabel?.Invoke(this, context);
            return !stripHTML || string.IsNullOrEmpty(label) ? label : SearchText.WithoutTags(label);
        }

        /// <summary>The description: asked of the provider every time for a compacted or
        /// full-description item, else once when the item has none (and kept unless the item
        /// always refreshes); without any, the item's value when one was set, and a compacted
        /// item's label.</summary>
        public string GetDescription(SearchContext context, bool stripHTML = false)
        {
            var fetch = provider?.fetchDescription;
            if ((options & (SearchItemOptions.Compacted | SearchItemOptions.FullDescription)) != 0 && fetch != null) return fetch(this, context);
            var text = description;
            if (description == null && fetch != null)
            {
                text = fetch(this, context);
                if ((options & SearchItemOptions.AlwaysRefresh) == 0) description = text;
            }
            if (text == null)
            {
                if (m_Value != null) return value.ToString();
                if ((options & SearchItemOptions.Compacted) != 0) return GetLabel(context, stripHTML);
            }
            return !stripHTML || string.IsNullOrEmpty(text) ? text : SearchText.WithoutTags(text);
        }

        /// <summary>The provider's thumbnail, kept (and then answered without asking) only when
        /// the caller says so; an empty texture counts as none.</summary>
        public Texture2D GetThumbnail(SearchContext context, bool cacheThumbnail = false)
        {
            if (cacheThumbnail && thumbnail) return thumbnail;
            var fetched = Usable(provider?.fetchThumbnail?.Invoke(this, context));
            if (cacheThumbnail && fetched != null) thumbnail = fetched;
            return fetched;
        }

        public Texture2D GetPreview(SearchContext context, Vector2 size, FetchPreviewOptions options = FetchPreviewOptions.Normal, bool cacheThumbnail = false)
        {
            if (cacheThumbnail && preview) return preview;
            var fetched = Usable(provider?.fetchPreview?.Invoke(this, context, size, options));
            if (cacheThumbnail && fetched != null) preview = fetched;
            return fetched;
        }

        /// <summary>A destroyed or missing texture is none; so is one without a size, which the
        /// probe has no row for.</summary>
        static Texture2D Usable(Texture2D texture) => texture && texture.width > 0 && texture.height > 0 ? texture : null;

        public int CompareTo(SearchItem other) => string.CompareOrdinal(id, other.id);

        public int CompareTo(object other) => string.CompareOrdinal(id, ((SearchItem)other).id);

        public override bool Equals(object other) => other is SearchItem item && Equals(item);

        public override int GetHashCode() => id.GetHashCode();

        public bool Equals(SearchItem other) => string.Equals(id, other.id, StringComparison.Ordinal);

        public override string ToString()
        {
            if (string.IsNullOrEmpty(label)) return $"[{score}] {value}";
            if (string.IsNullOrEmpty(description)) return $"[{score}] {label} | {value}";
            return $"[{score}] {label} [{score}] | {value} ({description})";
        }

        public Object ToObject() => ToObject(typeof(Object));

        /// <summary>The object the provider gives for the item as the type, asked once per type;
        /// null when it gives none or one of another type.</summary>
        public Object ToObject(Type type)
        {
            m_Objects ??= new Dictionary<Type, Object>();
            if (!m_Objects.TryGetValue(type, out var found))
            {
                found = provider?.toObject?.Invoke(this, type);
                if (found != null && !type.IsInstanceOfType(found)) found = null;
                m_Objects[type] = found;
            }
            return found;
        }

        public T ToObject<T>() where T : Object => ToObject(typeof(T)) as T;

        // The item's named fields are not implemented yet: declared as the generated stub is,
        // since the indexer decides the class's default member.
        public object this[string name] { [Unbound.Stub] get { Unbound.Runtime.Stubs.NotImplemented("UnityEditor.Search.SearchItem.this[]"); return default; } }
    }

    /// <summary>
    /// A search: its providers by priority, its flags and its text, taken apart as the Unity
    /// 6000.0.71f1 Editor takes it apart (tests/projects/searchprovider context.*). A text that
    /// starts with a provider's filter ID searches that provider alone with the rest as the
    /// query; the query's lowercased tokens are its words, those with a colon its filters.
    /// </summary>
    [Serializable]
    public partial class SearchContext : IDisposable, ISerializationCallbackReceiver
    {
        static readonly string[] s_None = new string[0];

        [SerializeField] string m_SearchText = "";
        string m_CachedPhrase;
        [SerializeField] SearchFlags m_Flags;
        [NonSerialized] List<SearchProvider> m_Providers;
        bool m_Disposed;

        internal SearchContext(SearchProvider provider)
        {
            m_Providers = new List<SearchProvider> { provider };
            options = SearchFlags.Sorted;
        }

        public SearchContext(IEnumerable<SearchProvider> providers, string searchText, SearchFlags options)
        {
            m_Providers = providers.OrderBy(p => p.priority).Distinct().ToList();
            this.options = options;
            this.searchText = searchText ?? "";
            foreach (var provider in m_Providers) provider.Enabled();
        }

        public SearchContext(IEnumerable<SearchProvider> providers, string searchText) : this(providers, searchText, SearchFlags.Sorted) { }

        public SearchContext(IEnumerable<SearchProvider> providers) : this(providers, "", SearchFlags.Sorted) { }

        public SearchContext(SearchContext context) : this(context.providers, context.searchText, context.options) { }

        public string searchQuery { get; private set; } = "";

        public int searchQueryOffset { get; private set; }

        public string[] searchWords { get; private set; } = s_None;

        /// <summary>The words joined, as they were when first asked for: a later text does not
        /// change it (tests/projects/searchprovider context.setText).</summary>
        public string searchPhrase
        {
            get
            {
                if (m_CachedPhrase == null && searchWords.Length != 0) m_CachedPhrase = string.Join(" ", searchWords).Trim();
                return m_CachedPhrase ?? "";
            }
        }

        public string[] textFilters { get; private set; } = s_None;

        public SearchFlags options
        {
            get => m_Flags;
            set => m_Flags = value;
        }

        public bool wantsMore
        {
            get => (options & SearchFlags.WantsMore) == SearchFlags.WantsMore;
            set => options = value ? options | SearchFlags.WantsMore : options & ~SearchFlags.WantsMore;
        }

        public bool empty => string.IsNullOrEmpty(m_SearchText);

        public string searchText
        {
            get => m_SearchText;
            set
            {
                if (!m_SearchText.Equals(value)) SetSearchText(value);
            }
        }

        /// <summary>The providers the search asks: the filtered one alone, else all of them
        /// without the explicit ones (a context of one provider asks it whatever it is).</summary>
        public IEnumerable<SearchProvider> providers
        {
            get
            {
                if (filterId != null) return m_Providers.Where(p => p.filterId == filterId);
                if (m_Providers.Count == 1) return m_Providers;
                return m_Providers.Where(p => !p.isExplicitProvider);
            }
        }

        [Unbound.EditorFaithful("searches run inside SearchService.GetItems: none is under way between calls (tests/projects/searchprovider context.*)")]
        public bool searchInProgress => false;

        public string filterId { get; private set; }

        void SetSearchText(string value)
        {
            m_SearchText = value ?? "";
            filterId = null;
            searchWords = textFilters = s_None;
            searchQueryOffset = 0;
            var query = m_SearchText.TrimStart();
            if (query.Length > 0)
            {
                foreach (var provider in m_Providers)
                {
                    if (!query.StartsWith(provider.filterId, StringComparison.OrdinalIgnoreCase)) continue;
                    filterId = provider.filterId;
                    query = query.Remove(0, provider.filterId.Length).TrimStart();
                    break;
                }
            }
            searchQuery = query;
            if (string.IsNullOrEmpty(query)) return;
            searchQueryOffset = m_SearchText.Length - query.Length;
            searchQuery = query.TrimEnd();
            var tokens = SearchText.OneSpaced(searchQuery).ToLowerInvariant().Split(' ');
            searchWords = tokens.Where(t => t.IndexOf(':') == -1).ToArray();
            textFilters = tokens.Where(t => t.IndexOf(':') != -1).ToArray();
        }

        /// <summary>Takes a provider out of the search: the search's session ends and begins
        /// again with the providers left, so each of those is disabled and enabled
        /// (tests/projects/searchprovider context.setFilterEnabling). Putting one in that the
        /// context was not made with needs a provider registered with the Editor, which Unbound
        /// has none of.</summary>
        public void SetFilter(string providerId, bool isEnabled)
        {
            var provider = m_Providers.FirstOrDefault(p => p.id == providerId);
            if (isEnabled || provider == null) return;
            foreach (var current in m_Providers) current.Disabled();
            m_Providers = m_Providers.Where(p => p != provider).ToList();
            foreach (var current in m_Providers) current.Enabled();
        }

        public bool IsEnabled(string providerId) => m_Providers.Any(p => p.id == providerId);

        /// <summary>Ends the search: its providers are disabled once no other context uses them.
        /// The context still answers afterwards.</summary>
        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;
            foreach (var provider in m_Providers) provider.Disabled();
        }

        const string NotKept = "Unbound keeps no search context over a script reload: there is nothing to prepare or restore";

        [Unbound.EditorFaithful(NotKept)]
        void ISerializationCallbackReceiver.OnBeforeSerialize() { }

        [Unbound.EditorFaithful(NotKept)]
        void ISerializationCallbackReceiver.OnAfterDeserialize() { }
    }

    public static partial class SearchService
    {
        public static SearchContext CreateContext(IEnumerable<SearchProvider> providers, string searchText = "", SearchFlags flags = SearchFlags.Sorted) =>
            new SearchContext(providers, searchText, flags);

        public static SearchContext CreateContext(SearchProvider provider, string searchText = "") => CreateContext(new[] { provider }, searchText);

        /// <summary>
        /// The items the context's providers give for its text, now: each provider in turn, the
        /// items it added to the list and then those it returned; one that throws is logged and
        /// the next is asked. The options join the context's, and the joined flags decide the rest
        /// (search.sortedContextPlainCall, search.plainContextSortedCall); a sorted search orders the items
        /// by their provider's priority, their score and their ID and keeps the first of each ID
        /// (matches the Unity 6000.0.71f1 Editor, tests/projects/searchprovider search.*).
        /// </summary>
        public static List<SearchItem> GetItems(SearchContext context, SearchFlags options = SearchFlags.Sorted)
        {
            context.options |= options;
            var list = new List<SearchItem>();
            foreach (var provider in context.providers.ToArray())
            {
                try
                {
                    var fetch = provider.fetchItems ?? throw Unbound.Runtime.MonoExceptions.NullReference();
                    Collect(fetch(context, list, provider), list);
                }
                catch (Exception e)
                {
                    Debug.LogException(new Exception("Failed to get fetch " + provider.name + " provider items.", e));
                }
            }
            if ((context.options & SearchFlags.Sorted) == 0) return list;
            list.Sort((a, b) =>
            {
                int order = a.provider.priority.CompareTo(b.provider.priority);
                if (order == 0) order = a.score.CompareTo(b.score);
                return order != 0 ? order : string.CompareOrdinal(a.id, b.id);
            });
            var seen = new HashSet<string>(StringComparer.Ordinal);
            return list.Where(item => seen.Add(item.id)).ToList();
        }

        /// <summary>The items of what a provider returned: nothing for null; an enumerable or
        /// enumerator is walked to its end, nulls passed over and enumerables and enumerators
        /// inside it walked in turn; anything else is an error, as in the Editor (a text is an
        /// enumerable of characters, which are no items; tests/projects/searchprovider
        /// search.returns.*). The walk runs to its end inside the call: Unbound has no
        /// asynchronous search sessions to hand a long enumeration to.</summary>
        static void Collect(object returned, List<SearchItem> list)
        {
            if (returned == null) return;
            // An enumerable first: an iterator object is both, and only its GetEnumerator starts it.
            var enumerator = (returned as System.Collections.IEnumerable)?.GetEnumerator() ?? returned as System.Collections.IEnumerator
                ?? throw new ArgumentException("Parameter itemEnumerator is not an IEnumerable or IEnumerator.", "itemEnumerator");
            while (enumerator.MoveNext())
            {
                var current = enumerator.Current;
                if (current == null) continue;
                if (current is SearchItem item) list.Add(item);
                else if (current is System.Collections.IEnumerable || current is System.Collections.IEnumerator) Collect(current, list);
                else throw new InvalidCastException($"Cannot cast \"{current.GetType()}\" to type {typeof(SearchItem)}.");
            }
        }
    }
}
