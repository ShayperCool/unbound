# Packages from an authenticated registry, a local tarball and git

Which packages the Package Manager installs into a project whose `Library/` is empty, and which
`Authorization` header it sends to scoped registries, recorded in Play mode. `run.py` builds
everything per run under the run cache: a local test registry (a Python HTTP server on
`127.0.0.1`), the user configuration file the Package Manager reads (`UPM_USER_CONFIG_FILE`, with
`[npmAuth."<registry url>"]` sections and this probe's own test tokens), a local tarball
(`file:../LocalPackages/com.probe.tarball-1.0.0.tgz`) and a bare git repository
(`git+file://.../repo.git?path=/pkgs/gitpkg#main`) whose tag `v1` ships the package at 1.0.0 with
marker `git v1` and whose `main` has moved on to 1.0.1 (`git v2`). The project's
`packages-lock.json` pins the git package to the `v1` commit, as a project checked out without
its `Library/` has it. The Editor gets its own empty package cache (`UPM_CACHE_ROOT`), the engine
an empty package store (a private `UNBOUND_CACHE` whose other entries link to the user's cache).

`Assets/Editor/PackageSourcesProbe.cs` records each `com.probe.*` package's version, source and the
text of its `Marker.txt`; `run.py` adds, for each request the registry received, the
`Authorization` values sent (`<none>` for none). The registry answers every request whatever its
header, so each one shows what was sent. The gate also fails when a test token appears in the
engine's log.

| Package | Scoped registry | npmAuth sections | Tarball |
|---|---|---|---|
| one | `/r1` | `/r1` token | on the registry |
| two | `/r2/` | `/r2` `_auth` | on the registry |
| three | `/r3/deep` | `/r3` and `/r3/deep` tokens | on the registry |
| four | `/r3/other` | only `/r3` | on the registry |
| five | `/r5` | `/r5` token, `alwaysAuth = false` | `localhost` (another host name) |
| six | `/r6` | `/r6` token, `alwaysAuth = true` | `localhost` |
| seven | `/r7` | `/r7` token | the registry's host, another path |
| eight | `/r8` | none | on the registry |
| nine | `/r9` | `/r9/` token | on the registry |
| ten | `/r10` | `/r10` token | the registry's host, another port |

Measured with the Unity 6000.0.71f1 Linux GPU Editor in batch mode (two identical captures; an
earlier capture without the lock file gave the same headers): a section applies to the scoped
registry whose URL is its own, a trailing slash aside (two, nine); a section for a parent path
does not apply (four: no header; three: its own section); the package document always carries the
header; the tarball carries it on the registry's own scheme, host and port, whatever the path
(one, seven), and elsewhere only with `alwaysAuth` (five and ten: none, six: sent). `token` is
sent as `Bearer`, `_auth` as `Basic`. The locked commit is installed although `main` has moved
(`git v1`; without the lock the Editor installs `main`'s head and locks it); the tarball is a
`LocalTarball` package. The Manual
([Scoped registry authentication](https://docs.unity3d.com/6000.0/Documentation/Manual/upm-config-scoped.html),
[Package Manager configuration](https://docs.unity3d.com/6000.0/Documentation/Manual/upm-config.html),
[Git dependencies](https://docs.unity3d.com/6000.0/Documentation/Manual/upm-git.html),
[Local folder or tarball paths](https://docs.unity3d.com/6000.0/Documentation/Manual/upm-localpath.html))
documents the sections, `alwaysAuth` ("if the package metadata and tarballs aren't located on the
same server"), the URL forms, `?path=` before `#revision` and the lock's commit, but not which URL
a section matches.

Not measured: registries answering 401 (the Editor's retries), `https` and default ports, a
section with both `token` and `_auth` (Unbound sends the token), the global configuration file,
git LFS, other releases. Without a lock file Unbound asks the repository for the revision's commit
on every start (it writes no lock), so such a package needs the network even when fetched before.

```sh
export UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1
python3 tests/projects/packagesources/run.py record --label native-a
python3 tests/projects/packagesources/run.py record --label native-b
python3 tests/projects/packagesources/run.py publish native-a native-b
UNBOUND=/path/to/unbound python3 tests/projects/packagesources/run.py gate
```

Values compare exactly. Unbound on Linux, 2026-11-18: 45/45 (before: 26/45; no header was sent,
the git package was missing and the tarball was reported missing).
