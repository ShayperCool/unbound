# Serialized field coverage

`unbound doctor --all --json -p <project>` reports class support, unread serialized fields and
unsupported script messages. It reads project files and writes compilation artifacts only under
`Library/Unbound` or the global cache. It does not instantiate game objects, run lifecycle or
serialization callbacks, or start a renderer.

The serialized-object scan includes Unity YAML, binary serialized files with type trees and
UnityFS AssetBundles. Imported source files such as images, audio and models are not serialized
object documents. Importer `.meta` files and ProjectSettings are separate follow-ups. Stripped
prefab placeholders carry links rather than object data and are not counted as object documents.
A recognized binary file that cannot be decoded, including a file without required type trees,
remains an explicit incomplete scan.

The `serializedFields` JSON object contains:

- `unreadMayChangeBehaviour`: fields present in input with no matching reader declaration or
  decoder access. Zero, empty and null values are included.
- `knownNoRuntimeEffect`: unread authoring metadata with an explicit allow-list reason per path.
  `m_ObjectHideFlags`, serialization layout revisions and script class identifiers remain potential
  behaviour differences unless a reader consumes them.
- `incomplete`: every file or object whose field analysis could not finish, including missing
  scripts and assemblies that did not compile. Partial results remain in the report.
- `objects`: analyzed non-stripped documents of supported classes.

Each field includes `classId`, `typeName`, `path`, `count` and `exampleAsset`. Counts are object
counts, not element counts. Array elements share `[]` paths. Literal key characters `.`, `[`, `]`
and `\` are escaped with `\`, so dotted keys and nested fields cannot collide. Classes and paths
are sorted; the example is the first asset in path order. The human report shows a small sample;
JSON retains every field, reason and failure.

## Where reader declarations come from

`Unbound.ReaderFields` generates assembly metadata from the built-in readers in the same C#
compilation. It follows node aliases, nested getters, reader helper calls, field-name arrays and
bounded field-name loops. All branches contribute, including branches that need referenced asset
state. The field names are not repeated in a doctor-specific table. Removing a getter removes its
declaration on the next build. Unknown field expressions are reported as incomplete rather than
assumed consumed. Doctor matches these declarations to private input trees without invoking the
runtime readers. Script fields use `SerializationRules.Members`, the table the script reader uses,
including inherited fields, former names and managed references.

Rust map access capture follows cloned nodes through the binary conversions. Prefab declarations
use the same override input decoder as flattening, with an empty destination; referenced prefabs
and model imports are unnecessary to discover which fields the decoder accepts.
Ordinary maps have no capture collector. The map's serialized representation and decoded loading
output are unchanged. The metadata exception list is intentionally small; an exception requires a
nonempty reason and review. A field being declared read does not establish support for every value
or representation of that field; class-level layout and feature diagnostics still apply.

`tests/projects/classids/run.py check` exercises class support, obsolete components and the doctor
JSON protocol without starting the Editor. Managed tests cover reader-derived declarations,
helpers, nested values, dynamic keys and script tables. Rust tests cover removed consumption,
counts, stable examples, metadata reasons and binary scan failures.
`tests/projects/classids/field_coverage.py` checks the committed binary bundle's script fields,
serialized-file identity and independent unknown-field and metadata controls.
