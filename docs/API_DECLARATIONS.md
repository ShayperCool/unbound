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
