# Test filter probe

Which tests `-runTests` runs for the Test Framework's command-line filters (com.unity.test-framework
1.6.0, unmodified, in the Unity 6000.0.71f1 Editor and in `unbound test`), over two Edit Mode test
assemblies: `ProbeTests` (namespace `Probe`: `Alpha`, `AlphaExtra`, `Beta` with a `Slow` category,
two parameterized cases and an `[Explicit]` test) and `OtherTests` (namespace `Other`: `Alpha` and
`Gamma` with a `Slow` category).

The Test Framework's [command-line reference](https://docs.unity3d.com/Packages/com.unity.test-framework@1.4/manual/reference-command-line.html)
documents `-testFilter` as "a semicolon-separated list of test names to run, or a regular
expression pattern to match tests by their full name", with `!` excluding a name and
`ClassName\.MethodName\(Param1,Param2\)` selecting one case of a parameterized test;
`-testCategory` as a semicolon-separated list of categories or a regular expression (with `!`
exclusions), combined with `-testFilter` so that only tests satisfying both run; and
`-assemblyNames` as a semicolon-separated list of test assemblies. Measured, and what the
package's filter code does: a filter selects a test when it matches the test's full name or the
name of a suite above it (a fixture, a namespace, the assembly as `ProbeTests.dll`), anywhere in
it (`class-name`: `Alpha` takes both `Alpha` fixtures and `AlphaExtra`), so a filter that matches
the test assembly's name runs the whole assembly (`assembly-name`: `ProbeTests` runs all of
`ProbeTests.dll`, not only a class of that name; name the class `^Probe\.Alpha$` or a test by its
full name to run less). Unescaped `(` and `)` are a regular expression's groups:
`Probe.Beta.Sum(2)` selects nothing. An `[Explicit]` test runs only when a filter names it
(`explicit`) and is otherwise reported as skipped. A filter with no match runs nothing and exits
with 0. Unbound runs the package's own filter code, so its results
are the Editor's in every case.

    run.py editor  --release 6000.0.71f1 --label L   # two GPU batch Editors, one launch per case
    run.py publish --release 6000.0.71f1 --label L   # the identical repeats -> Reference/unity-<release>.jsonl
    UNBOUND_CLI=<runtime>/unbound run.py check --release 6000.0.71f1

Each reference line holds a case's exit code and its executed test cases (full name and result)
in the order of the results file; `check` compares them exactly.
