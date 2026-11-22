# FileUtil copy probe

Records the Unity 6000.0.71f1 Editor's `FileUtil` copy, move and replace calls on paths the probe
makes in its own project copy and next to its output, and `FileUtil`'s path helpers. Each call has
three rows: its exception if any (`.result`), what the checked paths are afterwards (`.after`: a
file with its text and read-only flag, a directory with everything in it, a link, or none) and the
logs it wrote (`.logs`).

- `copy.*`: `CopyFileOrDirectory` of a file (its .meta file is not copied, a read-only file stays
  read-only, backslashes and `..` in paths, an absolute destination outside the project), of a
  directory tree (hidden files, .meta files and an empty directory go along; trailing slashes),
  of links to a file and to a directory and of a tree holding a link (what the links point to is
  copied); a destination that exists (also the source itself, also a directory), a destination
  whose folder is missing, a missing source, and null and empty arguments.
- `follow.*`: `CopyFileOrDirectoryFollowSymlinks` on the same links and failures.
- `move.*`: `MoveFileOrDirectory` of a file (its .meta file stays), a tree and a link (moved as a
  link), out of the project, and its failures.
- `replaceFile.*`, `replaceDirectory.*`: the destination is deleted first, also when the copy then
  fails; a destination of the other kind is not.
- `temp`: the form of `GetUniqueTempPathInProject` (nothing is made).
- `path.*`: the internal `GetLastPathNameComponent`, `DeleteLastPathNameComponent`,
  `GetPathExtension`, `GetPathWithoutExtension`, `IsSymlink`, `ResolveSymlinks` and
  `GetActualPathName`, and `GetProjectRelativePath`, over a list of paths each.

## Usage

```bash
python3 tests/projects/fileutilcopy/run.py record --label run-a   # Editor, batch mode
python3 tests/projects/fileutilcopy/run.py publish run-a run-b
python3 tests/projects/fileutilcopy/run.py gate
```

The project takes its scene, packages and settings from `tests/projects/fileutildelete`. Values
compare exactly (tolerance 0). The captures are batch-mode ones of the macOS Editor
(`macos_batch`); the Linux Editor is not measured. Paths whose answer depends on whether the
volume tells upper from lower case are left out.

## Reference

`Reference/unity-6000.0.71f1/values.jsonl`: two independent, byte-identical macOS batch captures.
