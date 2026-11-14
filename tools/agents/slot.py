#!/usr/bin/env python3
"""Persistent worker checkouts ("slots") and cleanup of finished worktrees.

A slot is a git worktree that outlives its tasks. Its checkout path never changes, so its private
build target (tools/build.py: one target per checkout path) and its ignored caches (probe
Library/, managed obj/) stay warm from one task to the next. No target is shared or copied.

    python3 tools/agents/slot.py create [--count 10]      make missing slots, detached at main
    python3 tools/agents/slot.py list                     state of every slot
    python3 tools/agents/slot.py prepare <slot> <branch>  start a task: fresh branch from main
    python3 tools/agents/slot.py reap [--idle-hours 6] [--dry-run]

prepare refuses a slot that still holds work: tracked changes, untracked files that are not
ignored, or commits that are not on main. --discard drops them; only use it for work that is
landed or abandoned. Ignored files (caches) are kept.

reap removes worktrees that are not slots and not the main checkout once they are clean and
idle for --idle-hours, together with their build targets. Branches and commits are retained. It skips a
worktree that a symlink under the run caches points into. It also deletes targets whose checkout
no longer exists.

Slots live in $UNBOUND_SLOT_ROOT, else next to the build root (tools/build.py), so checkouts and
targets share the cache volume.
"""
import argparse
import errno
import fcntl
import os
from pathlib import Path
import shutil
import stat
import subprocess
import sys
import time

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import build  # noqa: E402

DEFAULT_COUNT = 10
SLOT_PREFIX = 'slot-'


def git(*args, cwd, check=True):
    result = subprocess.run(['git', *args], cwd=cwd, capture_output=True, text=True)
    if check and result.returncode:
        raise SystemExit(f'git {" ".join(args)} in {cwd}: {result.stderr.strip()}')
    return result.stdout.strip()


def main_checkout(cwd):
    """The repository's main checkout: the worktree whose git dir is the common dir."""
    common = Path(git('rev-parse', '--path-format=absolute', '--git-common-dir', cwd=cwd))
    return common.parent.resolve()


def slot_root(env=os.environ):
    return Path(env.get('UNBOUND_SLOT_ROOT') or build.build_root(env).parent/'slots').resolve()


def slot_name(number):
    return f'{SLOT_PREFIX}{number:02d}'


def worktrees(repo):
    """(path, head, branch or None) for every worktree git knows, main checkout first."""
    entries, current = [], {}
    for line in git('worktree', 'list', '--porcelain', cwd=repo).splitlines() + ['']:
        if not line:
            if current:
                entries.append((Path(current['worktree']).resolve(), current.get('HEAD'),
                                current.get('branch', '').removeprefix('refs/heads/') or None))
            current = {}
            continue
        key, _, value = line.partition(' ')
        current[key] = value
    return entries


def unmerged_commits(path, repo):
    head = git('rev-parse', 'HEAD', cwd=path)
    return int(git('rev-list', '--count', f'main..{head}', cwd=repo))


def changed_paths(path):
    # --no-optional-locks: a check must not refresh the index, whose mtime measures idleness.
    return len(git('--no-optional-locks', 'status', '--porcelain', '--untracked-files=all',
                   '--ignore-submodules=none', cwd=path).splitlines())


def hidden_index_paths(path):
    # These flags can conceal edited tracked files even from worktree removal.
    # Keep the checkout until its owner clears them rather than changing its index.
    return any(row and (row[0].islower() or row[0] == 'S')
               for row in git('ls-files', '-v', cwd=path).splitlines())


def leftover_work(path, repo):
    """What a checkout holds that is not on main: a list of human-readable reasons."""
    reasons = []
    if changed := changed_paths(path):
        reasons.append(f'{changed} changed or untracked paths')
    if unmerged := unmerged_commits(path, repo):
        reasons.append(f'{unmerged} commits not on main')
    return reasons


def idle_seconds(path):
    """Seconds since the checkout's index or HEAD last changed."""
    git_dir = Path(git('rev-parse', '--absolute-git-dir', cwd=path))
    stamps = [p.stat().st_mtime for p in (git_dir/'index', git_dir/'HEAD') if p.exists()]
    return time.time() - max(stamps, default=0)


def cmd_create(args, repo):
    root = slot_root()
    root.mkdir(parents=True, exist_ok=True)
    known = {path for path, _, _ in worktrees(repo)}
    for number in range(1, args.count + 1):
        path = root/slot_name(number)
        if path in known:
            continue
        if path.exists():
            raise SystemExit(f'{path} exists but is not a worktree of {repo}')
        git('worktree', 'add', '--detach', str(path), 'main', cwd=repo)
        print(f'created {path}')
    return 0


def cmd_list(args, repo):
    root = slot_root()
    cache = build.build_root()
    rows = [(path, branch) for path, _, branch in worktrees(repo) if path.parent == root]
    if not rows:
        print(f'no slots in {root}; run `slot.py create`')
    for path, branch in sorted(rows):
        work = leftover_work(path, repo)
        state = 'busy: ' + ', '.join(work) if work else 'free'
        target = build.default_target(path, cache)
        warm = 'warm' if (target/'release').is_dir() else 'cold'
        print(f'{path.name}  {branch or "(detached)"}  {state}  idle {idle_seconds(path)/3600:.1f} h  '
              f'target {warm}  {path}')
    return 0


def cmd_prepare(args, repo):
    root = slot_root()
    path = (root/args.slot if '/' not in args.slot else Path(args.slot)).resolve()
    if path.parent != root or path not in {p for p, _, _ in worktrees(repo)}:
        raise SystemExit(f'{path} is not a slot in {root}; run `slot.py create` or pick from `slot.py list`')
    lock_dir = root/'.locks'
    lock_dir.mkdir(exist_ok=True)
    with (lock_dir/path.name).open('w') as lock:
        try:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            raise SystemExit(f'{path.name} is being prepared by another process')
        work = leftover_work(path, repo)
        if work and not args.discard:
            raise SystemExit(f'{path.name} still holds work ({", ".join(work)}); land it, '
                             f'or pass --discard if it is landed or abandoned')
        if work:
            git('reset', '--hard', cwd=path)
            git('clean', '-fd', cwd=path)  # untracked, not ignored: caches stay
        from runtime_snapshot import finish_task
        finish_task(path, repo=repo)
        git('switch', '-C', args.branch, args.base, cwd=path)
    print(f'{path.name} is on {args.branch} from {args.base}: cd {path}')
    print(f'build target: {build.default_target(path, build.build_root())}')
    return 0


def _symlink_targets(base, checkouts, index):
    """Run-cache links at any depth, including caches stored behind directory aliases."""
    found = set()
    base = base.absolute()
    checkouts = set(checkouts)
    # Compiler/Editor installations hold no live probe projects. Following the build
    # cache would scan every private target on each reap.
    excluded = {'builds', 'editors', 'tools'}

    prefixes = {}

    def inside(path, roots):
        key = frozenset(roots)
        if key not in prefixes:
            prefixes[key] = tuple((str(root), str(root).rstrip(os.sep)+os.sep) for root in roots)
        value = str(path)
        return any(value == root or value.startswith(prefix) for root, prefix in prefixes[key])

    def unreadable(error):
        if error.errno in (errno.ENOENT, errno.ENOTDIR, errno.ELOOP):
            return  # a run was cleaned up, or its link no longer resolves
        raise SystemExit(f'cannot inspect run-cache links: {error}; refusing to reap worktrees')

    resolved, resolving = {}, set()

    def resolve(path):
        # Cache physical prefixes too: run snapshots repeat the same runtime links.
        if path in resolved:
            return resolved[path]
        if path in resolving:
            return path  # a cyclic link cannot be traversed, but its targets are retained
        if path.parent == path:
            return Path(os.path.realpath(path))
        resolving.add(path)
        parent = resolve(path.parent)
        candidate = parent.parent if path.name == '..' else parent/path.name
        try:
            raw = Path(os.readlink(candidate))
        except OSError as error:
            if error.errno != errno.EINVAL:
                unreadable(error)
            result = candidate
        else:
            found.add(candidate)  # every source in a chain, including directory links
            target = raw if raw.is_absolute() else candidate.parent/raw
            found.add(target)
            result = resolve(target)
        resolving.remove(path)
        resolved[path] = result
        return result

    base = resolve(base)
    excluded_roots = {resolve(base/name) for name in excluded}
    pending, visited = [base], set()
    while pending:
        batch = pending[-index.batch_size:]
        del pending[-index.batch_size:]
        for directory, info in zip(batch, index.stat_many(batch)):
            # Descents into a checkout meet its root; links into a subdirectory were
            # already recorded by resolve(). Avoid rebuilding every ancestor per node.
            if directory in checkouts or (directory == base and inside(base, checkouts)):
                found.add(directory)
            try:
                if isinstance(info, OSError):
                    raise info
                identity = (info.st_dev, info.st_ino)
                if identity in visited:
                    continue
                visited.add(identity)
                for entry in index.entries(directory, info):
                    if directory == base and entry.name in excluded:
                        continue
                    try:
                        if entry.is_symlink():
                            target = resolve(Path(entry.path))
                            found.add(target)
                            if inside(target, excluded_roots):
                                continue
                            if stat.S_ISDIR(entry.stat().st_mode):
                                if target.parent == target:
                                    raise SystemExit(f'cannot scan filesystem root through run-cache alias {entry.path}')
                                pending.append(target)
                        elif entry.is_dir(follow_symlinks=False):
                            pending.append(Path(entry.path))
                    except OSError as error:
                        unreadable(error)
            except OSError as error:
                unreadable(error)
    return found


def symlink_targets(base, checkouts=(), *, seconds=None):
    from run_cache_index import DirectoryIndex
    import sqlite3
    index = None
    try:
        index = DirectoryIndex(base.absolute(), seconds=seconds)
        result = _symlink_targets(base, checkouts, index)
        index.finish()
        return result
    except (OSError, sqlite3.Error, ValueError) as error:
        raise SystemExit(f'cannot inspect run-cache links: {error}; refusing to reap worktrees')
    finally:
        if index is not None:
            index.close()


def run_cache(env=os.environ):
    return Path(env.get('XDG_CACHE_HOME') or Path.home()/'.cache')/'unbound'


def referenced_checkouts(links, checkouts):
    """Build membership once instead of walking every link's parents per checkout."""
    roots = {str(path): path for path in checkouts}
    found = set()
    for link in links:
        value = str(link)
        while value:
            if value in roots:
                found.add(roots[value])
            parent = os.path.dirname(value)
            if parent == value:
                break
            value = parent
    return found


def cmd_index(args, repo):
    started = time.monotonic()
    print(f'warm run-cache listings: budget {args.seconds}s; reap validates the complete tree', flush=True)
    found = symlink_targets(run_cache(), [entry[0] for entry in worktrees(repo)], seconds=args.seconds)
    print(f'{len(found)} reference paths indexed in {time.monotonic()-started:.1f}s; no worktrees removed')
    return 0


def cmd_reap(args, repo):
    root = slot_root()
    main = main_checkout(repo)
    cache = build.build_root()
    linked = None
    removed = kept_unmerged = 0
    entries = worktrees(repo)
    for path, head, branch in entries:
        if not path.exists():
            continue
        if path == main or path.parent == root:
            kept_unmerged += bool(unmerged_commits(path, repo))
            continue
        # Cheapest checks first: there are many worktrees and `git status` is the slow one.
        reason = None
        if idle_seconds(path) < args.idle_hours*3600:
            reason = 'recently active'
        elif changed_paths(path):
            reason = 'has changed or untracked paths'
        elif hidden_index_paths(path):
            reason = 'has tracked paths hidden by index flags'
        else:
            if linked is None:
                print('inspect run-cache references: full validation, no time limit', flush=True)
                roots = [entry[0] for entry in entries]
                linked = referenced_checkouts(symlink_targets(run_cache(), checkouts=roots), roots)
            if path in linked:
                reason = 'a symlink under the run caches points into it'
        if reason:
            kept_unmerged += bool(unmerged_commits(path, repo))
            if args.verbose:
                print(f'keep {path} ({branch or "detached"}): {reason}')
            continue
        # Branch worktrees retain their commits on removal. Give an otherwise unreferenced
        # detached commit a branch as well so pruning the worktree cannot lose committed work.
        if branch is None and unmerged_commits(path, repo) and not git(
                'for-each-ref', '--contains', head, '--format=%(refname)', 'refs/heads', cwd=repo):
            saved = f'reaped-worktree/{head}'
            print(f'{"would preserve" if args.dry_run else "preserve"} detached commits as {saved}')
            if not args.dry_run:
                git('branch', saved, head, cwd=repo)
        target = build.default_target(path, cache)
        print(f'{"would remove" if args.dry_run else "remove"} {path} ({branch or "detached"})'
              f'{" and " + str(target) if target.exists() else ""}')
        if not args.dry_run:
            from runtime_snapshot import finish_task
            finish_task(path, repo=repo)
            git('worktree', 'remove', str(path), cwd=repo)
            if target.exists():
                shutil.rmtree(target)
        removed += 1
    # Targets whose checkout is gone can never be used again.
    targets = cache/'targets'
    for target in sorted(targets.iterdir()) if targets.is_dir() else []:
        marker = target/'.unbound-source-root'
        owner = marker.read_text().strip() if marker.is_file() else ''
        if owner and not Path(owner).exists():
            print(f'{"would remove" if args.dry_run else "remove"} orphaned target {target} ({owner})')
            if not args.dry_run:
                shutil.rmtree(target)
            removed += 1
    if not args.dry_run:
        git('worktree', 'prune', cwd=repo)
    print(f'{removed} removed' if not args.dry_run else f'{removed} would be removed')
    print(f'{kept_unmerged} kept with unmerged commits')
    return 0


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest='command', required=True)
    create = sub.add_parser('create', help='make missing slots')
    create.add_argument('--count', type=int, default=DEFAULT_COUNT)
    sub.add_parser('list', help='show every slot')
    index = sub.add_parser('index', help='warm the run-reference index without reaping')
    index.add_argument('--seconds', type=int, default=300, choices=range(1, 301), metavar='1..300')
    prepare = sub.add_parser('prepare', help='put a slot on a fresh task branch')
    prepare.add_argument('slot', help='slot name (slot-03) or path')
    prepare.add_argument('branch')
    prepare.add_argument('--base', default='main')
    prepare.add_argument('--discard', action='store_true', help='drop leftover work that is landed or abandoned')
    reap = sub.add_parser('reap', help='remove finished non-slot worktrees and orphaned targets')
    reap.add_argument('--idle-hours', type=float, default=6)
    reap.add_argument('--dry-run', action='store_true')
    reap.add_argument('--verbose', action='store_true')
    args = parser.parse_args(argv)
    repo = main_checkout(Path.cwd())
    return {'create': cmd_create, 'list': cmd_list, 'prepare': cmd_prepare, 'reap': cmd_reap,
            'index': cmd_index}[args.command](args, repo)


if __name__ == '__main__':
    raise SystemExit(main())
