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

| Category | Before Q410 | After enum slice |
| --- | ---: | ---: |
| Attributes | 938 | 732 |
| Modifiers | 87 | 87 |
| Parameters | 74 | 74 |
| Accessors | 70 | 70 |
| Layout | 68 | 68 |
| Interfaces | 65 | 65 |
| Accessibility | 18 | 18 |
| Constraints | 1 | 1 |
| Lookup policies | 11 | 11 |
| Total | 1,332 | 1,126 |

Q410's enum slice copies obsolete constructor arguments, messages, error flags and
accompanying annotations for 190 enum declarations. The attribute probe records
the corresponding metadata without referencing removed enum names in game source.

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
and Physics resolves 89. The default-state probe now has 10,551 exact rows and 1,223
queued rows, including the unchanged 50 variable rows. Its 11,565 canonical rows
preserve every original line. Added controls
cover exact removed-getter messages, valid and rejected factories, requirements,
explicit constructor arguments, aliases and refresh-rate arithmetic.

Fixed removed-getter exceptions come from the selected release assembly during stub
generation. Conditional and computed getters retain their existing fallback. Core
constructor, factory and refresh-rate mechanisms select exactly 6000.0.71f1; other
profiles keep their previous unmeasured paths, recorded in the compatibility gaps.
The generator format is version 5. API declarations and lookup checks still retain
1,332 exact release exceptions with zero errors; this slice adds no exceptions.

The probe README lists remaining module counts and unresolved contexts. In particular,
CoreModule keeps 116 rows requiring operation/payload validity, rendering initialization
or imported/serialized state controls. Two TextCore instance IDs and one Canvas early
layout value retain updated exact implementation payloads, without changing reference
values or adding variable policies.

Physics2D retains five default-state rows after factory, collision and capability fixes.
Its 17 controls include live collision entry/exit and observable contact-array mutation,
so changing an invalid manual constructor does not change valid callback payloads. The
existing exact-release Physics2D mechanism carries these observations. Polygon initialization
and initial substep accounting remain explicit follow-ups; no variable policy was widened.
