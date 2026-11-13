# Checking Unity declarations

`unbound api-check -p <project>` compares the implementation built without generated stubs with
that project's release assemblies. The assemblies are read as metadata and are never loaded for
execution. `--json` includes every unaccepted difference, the raw category counts and every exact
exception with its reason.

The comparison covers type and member signatures, accessibility, bases, declared interfaces,
enum values, constants, static/abstract/virtual/override/sealed and readonly modifiers, extension-method status, generic
constraints and variance, property and event accessors, optional defaults and params/ref/in/out.
Custom attributes on types, members including shared private fields, generic parameters, parameters and return values include
constructor identity and arguments, typed arrays, type arguments and named arguments. Attribute order and named
argument order do not matter; duplicate attributes still count. Storage checks also read layout,
packing, size, field offsets, marshaling descriptors, in/out flags and NonSerialized, which use metadata flags instead of custom
attribute records. Inherited framework interfaces are excluded by comparing declared interfaces;
the base type is checked separately.

`managed/UnityEngine/ApiCheckAllowList.json` records declaration migrations that remain open.
Each entry names one release, category, type and complete mismatch, with a nonempty reason.
Changing that mismatch invalidates the exception. Duplicate entries and exceptions that no
longer match fail the check. Exceptions for another release do not apply. Raw counts remain
visible even when all current differences have exceptions. This file is reviewed source policy,
not a report to regenerate merely to make a failing check pass.

## Attribute exclusions

`DeclarationMetadata.IgnoreAttribute` is the single exclusion list. Unknown attributes are
checked. It excludes:

- `JetBrains.Annotations.*`, nullable context/nullable annotations and debugger annotations, which
  describe source analysis or debugging rather than Unity behavior.
- `UnityEngine.Bindings.*`, `RequiredByNativeCode`, `UsedByNativeCode`, and DllImport, which describe
  how the reference implementation calls its platform runtime.
- `UnityEngine.Scripting.APIUpdating.*` and Unity's internal documentation, default-value hint
  and type-inference annotations. Optional defaults themselves are checked from parameter metadata.
- Compiler-generated/state-machine attributes and compiler protocol attributes for readonly,
  ref-like types, extension methods and parameter collections. The corresponding declaration
  modifiers and parameter flags are checked separately.
- `Unbound.*`, which marks implementation-specific diagnostics and unsupported features.

Serializable and NonSerialized are checked despite being stored as flags. AttributeUsage,
Obsolete including its error flag, serialization attributes and user-visible Unity annotations
are not excluded.

## Initial declaration debt

On the initial 6000.0.71f1 audit, the previous checker reported zero errors for 2,587 types.
The extended checks found 1,357 differences: 973 attribute, 74 parameter, 70 accessor,
68 layout, 65 interface, 88 modifier, 18 accessibility and one generic-constraint difference.
They are exact migration entries, not claims of matching declarations. Owning module slices
must remove each entry when they fix the corresponding declaration. The counts remain in the
JSON report so annotations and signatures cannot disappear into an aggregate pass result.

## Reflection lookup policies

The `lookup` category checks explicit reflection inherit arguments throughout `managed/`, excluding
tests and generated sources. It resolves attribute usage from the reference assemblies rather
than trusting the implementation's declaration. Unknown direct/computed lookups and explicit
lookups in inactive conditional branches remain visible until they have a policy. FieldInfo and
attributes restricted to targets without an inheritance chain do not need an inheritance policy.
A general inherited query delegates each attribute's usage rules to the CLR.

The Editor sometimes discovers declarations directly despite an attribute allowing CLR
inheritance. TypeCache, callback discovery, editor registration, UXML member records and image
message selection have exact exceptions with their measured reasons. The
[attribute inheritance probe](../tests/projects/attributeinheritance/README.md) tests the cases
where direct and inherited discovery produce different results, and keeps editor/drawer child
flags independent. The attribute declaration repair removes 30 initial annotation differences,
leaving 1,327 declaration migrations and eleven measured lookup policies.

## Q410 declaration migrations

The conditional-call slice copies sixteen method annotations from the 6000.0.71f1
release through the stub generator's attribute renderer. The attribute inheritance
probe checks argument evaluation with profiler defines present and absent, plus the
unconditional CommandBuffer string overloads. Its metadata rows include internal
Editor and collection-validation methods.

| Category | Before Q410 | After user annotation slice |
| --- | ---: | ---: |
| Attributes | 938 | 246 |
| Modifiers | 87 | 13 |
| Parameters | 74 | 0 |
| Accessors | 70 | 4 |
| Layout | 68 | 68 |
| Interfaces | 65 | 65 |
| Accessibility | 18 | 0 |
| Constraints | 1 | 1 |
| Lookup policies | 11 | 11 |
| Total | 1,332 | 408 |

One unrelated annotation exception entered main between these slices. Each Q410
slice removes only the exact entries for declarations it fixes.

Q410's enum slice copies obsolete constructor arguments, messages, error flags and
accompanying annotations for 190 enum declarations. The attribute probe records
the corresponding metadata without referencing removed enum names in game source.

The UIElements metadata slice copies 128 annotations and fixes 26 accessors,
modifiers and optional defaults. JSON controls start with different field values
before overwrite; angle controls exercise unmarked private fields. Declaration
controls record private setters, final overrides and abstract methods. Interface
migrations remain separate because notification subscriptions change binding updates.
The exact TextElement event-interest list also remains queued until command and IME
event types have handwritten pooling and propagation controls, tracked as Q410-UI-command-events.

The controls cover Unity 6000.0's [private-field serialization rules](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/SerializeField.html)
and [JSON overwrite semantics](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/JsonUtility.FromJsonOverwrite.html):
marked fields change from nondefault values, unmarked private fields stay excluded,
and an omitted field retains its previous value. [Event-interest annotations](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/UIElements.EventInterestAttribute.html)
retain their argument arrays and flags. These metadata changes do not add event dispatch filtering.
The default-state inventory adds two public attribute types and three Editor rows;
all 11,565 prior raw default-state rows are retained byte-identically.

The member obsolete slice copies exact annotations for 299 type and member declarations.
The probe identifies overloads by their full signature and records constructor arguments.
TextEditor callers use the replacement state properties. Twenty-six controls
separate legacy storage from active state and record focus selection/cursor
behavior in both modes. GuiCompat selects the measured mechanism for 6000.0.71f1;
other releases keep their previous, unmeasured field linkage and require their
own reference before that fallback changes.
Animator.animatePhysics remains queued as Q410-Animator-animatePhysics: its current setter
changes updateMode, while the [Unity 6000.0 contract](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animator-animatePhysics.html)
covers animated kinematic Rigidbody transforms influencing other bodies. That migration
needs divergent-state and moving-platform controls with the runtime adaptation.

The parameter slice fixes 73 exact optional-default and array direction entries.
Metadata controls cover each changed parameter; invocation controls exercise omitted
arguments and the existing three-argument mesh overload.

The Editor shape slice resolves 33 entries: 12 modifier, 19 accessor and two field-annotation differences.
PackageInfo now initializes get-only values in its factory constructor. DependencyInfo
uses explicit getters and the selected serialized fields; compilation metadata retains its storage with the selected
private setters. Compiler controls distinguish sealed AssetDatabase from inheritable
platform settings. The probe checks every changed declaration and a private setter
with different before/after names and an unchanged output-path control.

Q410-Editor-computed-setters retains EditorApplication.isFocused and
EditorAssemblies.loadedAssemblies. Their current getters derive live state; matching
private setters requires adapting focus updates and assembly-cache invalidation,
with diverged-state controls. The accessor slice resolves the AssetPostprocessor
setter. Q410-Editor-coordination retains the ObjectIdentifier serialization flag
until its source ownership and serialized payload controls are ready.
The five NamedBuildTarget readonly differences also require a generated header
currently owned by Q411. Q410-Editor-serialized-payloads retains five Serializable
flags on EditorBuildSettingsScene, PackageInfo, PackageCollection, ListRequest and
SearchRequest. Their current property/cache/request storage needs the selected
serialized payloads and nested/overwrite controls before the runtime classifies them
as serialized composites. The thirteen remaining exact differences stay visible in the allow-list.

The same slice excludes MultiColumnHeaderState.m_MaxNumberOfSortedColumns from
serialization. A control starts it at 9, supplies 17 in JSON, and confirms 9 survives
while the serialized visible-column list changes from [0,1] to [1]. DependencyInfo
JSON controls change one name, retain the omitted version and preserve an independent
struct copy. GUID and GlobalObjectId JSON controls use nondefault identifiers.

The runtime modifier slice resolves 47 exact differences. Readonly getter metadata
uses explicit storage where implicit auto-property getters differ. Setter values,
struct copies and nonfinite values retain their selected-release behavior. Texture's
graphicsFormat getter is virtual; compiler controls exercise an override, a sealed
annotation class and an inheritable preference exception.

Q410-Core-built-in-serialization retains three exact differences: the Serializable
markers on SphericalHarmonicsL2 and RenderingLayerMask, and the mask field's
SerializeField marker. The 6000.0.71f1 JSON control exposes special nested payloads
for harmonic coefficients and a versioned rendering mask, while root values write
empty objects. Removing the markers alone drops existing nested values. The runtime
needs shared built-in classification and JSON/YAML read/write/clone controls before
these declarations can migrate. Raw Native and engine differences remain recorded;
the accepted modifier controls cover the declarations migrated in this slice.

The visibility slice resolves all 18 accessibility entries and ten coupled accessor
entries. It narrows internal types, helpers and properties, exposes the selected
protected GUI getter, and gives FontEngine its internal constructor. Bodies and
field values remain unchanged. The generator's existing union of selected module
assembly friendships remains in force. Compiler controls reject an internal system
limit, accept the public texture limit and access the protected getter from a derived
class. Each changed visibility has an Editor metadata control.

The same visibility migration resolves seven default-state entries: five previous
implementation-only public rows disappear and two nested FaceInfo values now match.
Six remaining exact observations retain their unchanged Native expectations with
updated implementation payloads. The default-state queue is 561 rows; no variable
policy, comparator or Native reference row changes.

The accessor slice resolves 27 further entries. Get-only public properties use
private storage and existing internal mutation paths for focus, deep links, the
current camera, quality names and texture update counts. Snapshot properties retain
their copy behavior; newly private setters retain their existing storage. Controls
change NavMesh costs and resource paths through reflection, with independent struct
copy, agent and attribute-field controls. Startup focus keeps its existing callback
phase and texture increments retain their public counter.

Q410-Accessor-state retains four exact entries. PropertyCollection<T>.Enumerator.Current
needs a writable cached value integrated with MoveNext, Reset and end states;
LinkedPool<T>.CountInactive needs a counter integrated with its shared ObjectPool.
EditorApplication.isFocused and EditorAssemblies.loadedAssemblies retain the live
focus and game-domain state migration already tracked above. No no-op setters are added. The strict texture-pixel run retains three VolumeRGFloat
row differences per Gamma and Linear profile. The exact landed parent and this
slice produce byte-identical 315-row outputs in both profiles; this numerical
follow-up remains Q410-texturepixels-baseline.

The user annotation slice updates 74 declarations and resolves 58 exact annotation
entries. It copies Flags, RequireComponent, preset exclusions, help URLs, required
members, preservation markers, asset extensions and edit-mode markers through the
selected declaration renderer. Three Object lookup overloads now carry their
selected obsolete warnings. Sixteen compound entries retain their exact remaining
metadata differences for the later policy/type migration. Texture2D annotations
remain queued while another slice owns a partial declaration.

The 127 added probe rows include every changed declaration, six formatting values
for each of eight enums and five component-requirement controls. A joint begins
without a Rigidbody, adding it creates one, and adding a second joint retains one
shared Rigidbody. A Light retains one Transform and a Camera remains an independent
control without a Rigidbody. Existing internal class IDs and all non-target
annotations are preserved, including across partial declarations.

## Landing checks and default state

The managed landing step runs `api-check` after stub generation and fails on any new or stale
exception. It also runs the API tooling tests. Managed sources, compatibility profiles and changes
to the gate itself select this step. The incremental check reuses the no-stubs build; a cold check
can exceed the budget for an always-run check.

At Q403 gate integration, module follow-ups had reduced the declaration queue to 1,321 entries:
938 attribute, 74 parameter, 70 accessor, 68 layout, 65 interface, 87 modifier, 18 accessibility and
one generic-constraint difference. The eleven measured lookup policies remain separate. The JSON
report is the authoritative count for later changes.

[`defaultstate`](../tests/projects/defaultstate/README.md) records the public creation-time state
of matching implemented types and compares decidable constructor literal stores. Its suite recipe
keeps every queued discrepancy visible, requires reasons and rejects new or stale entries.
Factory/context failures, variable observations and truncated nested values remain explicit
coverage gaps. New declarations come from the selected release through the stub generator; new
defaults need release evidence and a control that can distinguish the behavior being implemented.

Q411's shared removed-getter migration resolves 1,196 rows. The Core slice resolves
another 156 rows, including one Canvas viewport observation; Physics2D resolves 157
and Physics resolves 89. Audio resolves 185 more; an independently integrated lighting
fix resolves one additional row. The ParticleSystem slice resolves another 337 rows and the Editor slice resolves 132.
The default-state probe now has 11,644 exact rows and 568 queued rows, including
the unchanged 50 variable rows. Its 12,180 canonical rows
preserve every original line. Added controls
cover exact removed-getter messages, valid and rejected factories, requirements,
explicit constructor arguments, aliases, refresh-rate arithmetic, drive input ranges,
material ownership, existing audio-provider requirements, module ownership after
destruction, removed particle accessors, default curve key metadata, sprite slots,
importer backing and constructor failures, window initialization, removed texture
settings, collection validity and nullable paths.

Fixed removed-getter exceptions come from the selected release assembly during stub
generation. Conditional and computed getters retain their existing fallback. Core
constructor, factory and refresh-rate mechanisms select exactly 6000.0.71f1; other
profiles keep their previous unmeasured paths, recorded in the compatibility gaps.
The generator format is version 8 after adding backing checks to importer property
stubs. API declarations and lookup checks retain the exact release exceptions listed above
with zero errors; the Editor default-state changes add no exceptions.

The probe README lists remaining module counts and unresolved contexts. In particular,
CoreModule keeps 115 rows requiring operation/payload validity, rendering initialization
or imported/serialized state controls. Two TextCore instance IDs and one Canvas early
layout value retain updated exact implementation payloads, without changing reference
values or adding variable policies.

Physics2D retains five default-state rows after factory, collision and capability fixes.
Its 17 controls include live collision entry/exit and observable contact-array mutation,
so changing an invalid manual constructor does not change valid callback payloads. The
existing exact-release Physics2D mechanism carries these observations. Polygon initialization
and initial substep accounting remain explicit follow-ups; no variable policy was widened.

ParticleSystem retains ten rows after its module-handle and initialization changes:
four aggregates with inactive gradient color fields, the variable random seed and five
inherited Renderer rows. The canonical raw values and all 50 variable policies remain
strict. Missing-owner fallbacks and removed accessors have exact-release Compat rules;
constructor values reuse the existing initialization rule. The README records the
362 independent controls and the remaining renderer and inactive-storage contexts.
