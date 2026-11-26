# Which shader property names have ids first

`Assets/Scripts/PropertyIdsProbe.cs` records in Play mode, at `SubsystemRegistration` (before any
other script of the probe), into values.jsonl in `$PROPERTYIDS_OUT` (tests/value_probe.py) which of
the names `Assets/PropertyIds/Candidates.txt` lists (546 property names the engine gives ids before
Play mode, and three controls no shader has) already have shader property ids, and in what order: a name no shader has
(`__propertyids_marker`) is given an id first, every candidate after it, and those whose ids are
below the marker's are listed by id (values never contain ids). The project shader `Probe/Ids`
declares `_ProbeOwnFirst`, `_MainTex` and `_ProbeOwnSecond`. The values depend on neither the OS nor
the graphics device (macOS batch captures are the reference).

Rows: `builtin/count`, `builtin/order` (the candidates with ids, by id), `builtin/negative` (those
whose ids are negative), `builtin/shared` (names sharing an id), `builtin/before_project` and
`builtin/after_project` (by id, before and after the project shader's first own name),
`builtin/unlisted_before_project` (how many ids below the project's first name no candidate holds),
`project/shader`, `project/after_builtin` (its own names after every candidate), `project/before_marker`
(its names have ids before Play mode), `project/declared_order`, `project/main_tex_first`.

What it shows (Unity 6000.0.71f1): the engine gives 494 of the candidates ids before any project
content (`builtin/before_project`; the same list, in the same order, in a project of every built-in
module), some negative and legacy aliases sharing one (`builtin/shared`); 8 ids below the project's
first name belong to names the list misses; a project shader's names get ids when it is imported, in
declaration order, after those; 52 more built-in names get ids later in the Editor's session, after
the project's (the shaders it loads itself). Unbound registers
`builtin/before_project` first, in that order (`Compat.EnginePropertyNames`, which embeds this
reference), for the 6000.0 family.

Not covered: the 8 names the list misses, other release families. The candidates were found by
asking the Editor about a much larger list of identifiers (outside this probe).

Record and gate:

    python3 tests/projects/propertyids/run.py record --label A   # also B; macOS batch is fine
    python3 tests/projects/propertyids/run.py publish A B
    python3 tests/projects/propertyids/run.py gate

`residuals.json` names the open differences: the ids' values, the names given ids later in the
Editor's session, the project shader's import, and the names of its own Unbound gives ids at its start.
