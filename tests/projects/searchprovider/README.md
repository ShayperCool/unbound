# searchprovider

The data side of the Search API in Unity 6000.0.71f1 Play mode
(`Assets/Search/SearchProbe.cs`): no Search window is opened and no provider is registered with
the Editor; the probe makes its own providers and contexts.

- `provider.*`: what a `SearchProvider` made by each constructor holds (ID, type, name, filter
  ID, priority, flags, which handlers are set), the names made from IDs, and the default
  handlers' answers.
- `action.*`: what a `SearchAction` made by each public constructor holds.
- `item.*`: `new SearchItem`, every `CreateItem` overload (with a context, without one, with a
  context searching with `SearchFlags.Debug`), `value`, `SearchItem.clear`.
- `fetch.*`: how `GetLabel`, `GetDescription` (for every `SearchItemOptions` flag),
  `GetThumbnail`, `GetPreview` and `ToObject` ask the provider's handlers and what they keep.
- `compare.*`: `Equals`, `GetHashCode`, `CompareTo` and `ToString`.
- `context.*`: `SearchService.CreateContext` and the `SearchContext` constructors: the order and
  choice of providers, flags, and for a list of search texts the query, its offset, the words,
  the filters and the filter ID; setting the text again, `wantsMore`, `SetFilter`, a disposed
  context, and when providers' `onEnable` and `onDisable` run.
- `search.*`: `SearchService.GetItems` with `SearchFlags.Synchronous`: which providers are
  asked with what, the order of the items with and without `SearchFlags.Sorted` on the context
  and on the call, a filtered text, an inactive provider, a provider that throws or has no
  handler, items of one ID, and (`search.returns.*`) every shape a handler may return.

Not recorded: anything drawn (`ObjectField.DoObjectField`, the Search window and picker), the
providers the Editor registers itself and the calls that look providers up by ID, asynchronous
searches and their events, search expressions, an item's named fields, a thumbnail without a
size, and the session messages a context with `SearchFlags.Debug` logs (they carry a running
session number).

Unity's Scripting API for 6000.0 (`Search.SearchProvider`, `Search.SearchItem`,
`Search.SearchContext`, `Search.SearchService`,
docs.unity.com/en-us/engine/6000.0/script-reference/unityeditor/search/searchprovider) describes
the members: a `fetchItems` handler's "return value is an object that can be of type IEnumerable
or IEnumerator" whose enumeration "should return SearchItems" (`search.returns.*`); it gives no
order for unsorted results, which the probe records as observed.

```sh
python3 tests/projects/searchprovider/run.py record --label a
python3 tests/projects/searchprovider/run.py record --label b
python3 tests/projects/searchprovider/run.py publish a b
python3 tests/projects/searchprovider/run.py gate
```
