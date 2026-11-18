# FileUtil.DeleteFileOrDirectory

Unity 6000.0.71f1, recorded with the macOS Editor in batch mode in Play mode (two byte-identical
captures). The values depend on the file system only through what the probe makes; the gate
compares every value exactly.

`Assets/Scripts/FileUtilDeleteProbe.cs` makes every path it deletes itself, in its own project
copy (`Assets/FileUtilDeleteScratch`) and in a directory next to its output (`<outside>`, removed
at the end), and calls `FileUtil.DeleteFileOrDirectory` on: a file with a `.meta` file beside it, a
directory tree with one, a trailing slash, backslashes, a missing file and a file in a missing
directory, null and "", absolute paths outside and inside the project, a read-only file, a tree
holding a read-only file, an empty directory, a path through `..`, a name with a space, a wildcard,
symbolic links to a directory and to a file (made with `ln -s`), a file already deleted, and the
scratch directory itself. Each row: the return value or exception (`.returns`), what exists
afterwards (`.after`) and the logs the call wrote (`.logs`).

Measured: the call deletes exactly the file or tree it names (read-only files too) and returns true;
false for a path that does not exist, a wildcard being a name; null throws ArgumentNullException
(path), "" throws ArgumentException; a `.meta` file beside the path stays; backslashes separate
folders; absolute paths outside the project are deleted as well; a symbolic link is removed, not
what it points to; nothing is logged.

Unmeasured: paths the process may not delete (permissions; Unbound returns false), a case that
differs from the file's on a case-insensitive file system (the file system decides), the Asset
Database's view of deleted assets before a refresh, other releases.
