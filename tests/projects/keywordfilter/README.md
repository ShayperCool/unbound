# keywordfilter

What the `UnityEditor.ShaderKeywordFilter` attributes hold and answer in Unity 6000.0.71f1 Play
mode (`Assets/Scripts/KeywordFilterProbe.cs`), read through their internal members: the rules a
settings field declares (the file name and line the compiler fills in), each rule attribute made in
code (action, precedence, negation, keywords, condition, file, line, the action it answers for a
matching and another value, a null condition, its resolution messages), the tag and graphics API
constraints' rule checks, and the attribute usages.

```sh
python3 tests/projects/keywordfilter/run.py record --label a
python3 tests/projects/keywordfilter/run.py record --label b
python3 tests/projects/keywordfilter/run.py publish a b
python3 tests/projects/keywordfilter/run.py gate
```
