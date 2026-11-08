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

reap removes worktrees that are not slots and not the main checkout once they are clean, fully
merged into main and idle for --idle-hours, together with their build targets. It skips a
worktree that a symlink under the run caches points into. It also deletes targets whose checkout
no longer exists.

Slots live in $UNBOUND_SLOT_ROOT, else next to the build root (tools/build.py), so checkouts and
targets share the cache volume.
"""
import argparse
import fcntl
import os
from pathlib import Path
import shutil
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
    return len(git('--no-optional-locks', 'status', '--porcelain', cwd=path).splitlines())


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
        git('switch', '-C', args.branch, args.base, cwd=path)
    print(f'{path.name} is on {args.branch} from {args.base}: cd {path}')
    print(f'build target: {build.default_target(path, build.build_root())}')
    return 0


def symlink_targets(base, depth=5):
    """Resolved destinations of the symlinks under the run caches (to depth), to protect live runs."""
    found = set()
    if not base.is_dir():
        return found
    for dirpath, dirnames, filenames in os.walk(base):
        level = len(Path(dirpath).relative_to(base).parts)
        for name in dirnames + filenames:
            entry = Path(dirpath)/name
            if entry.is_symlink():
                found.add(Path(os.path.realpath(entry)))
        if level >= depth:
            dirnames[:] = []
    return found


def run_cache(env=os.environ):
    return Path(env.get('XDG_CACHE_HOME') or Path.home()/'.cache')/'unbound'


def cmd_reap(args, repo):
    root = slot_root()
    main = main_checkout(repo)
    cache = build.build_root()
    linked = None
    removed = 0
    for path, head, branch in worktrees(repo):
        if path == main or path.parent == root or not path.exists():
            continue
        # Cheapest checks first: there are many worktrees and `git status` is the slow one.
        reason = None
        if idle_seconds(path) < args.idle_hours*3600:
            reason = 'recently active'
        elif unmerged_commits(path, repo):
            reason = 'has commits not on main'
        elif changed_paths(path):
            reason = 'has changed or untracked paths'
        else:
            if linked is None:
                linked = symlink_targets(run_cache())
            if any(link == path or path in link.parents for link in linked):
                reason = 'a symlink under the run caches points into it'
        if reason:
            if args.verbose:
                print(f'keep {path} ({branch or "detached"}): {reason}')
            continue
        target = build.default_target(path, cache)
        print(f'{"would remove" if args.dry_run else "remove"} {path} ({branch or "detached"})'
              f'{" and " + str(target) if target.exists() else ""}')
        if not args.dry_run:
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
    return 0


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest='command', required=True)
    create = sub.add_parser('create', help='make missing slots')
    create.add_argument('--count', type=int, default=DEFAULT_COUNT)
    sub.add_parser('list', help='show every slot')
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
    return {'create': cmd_create, 'list': cmd_list, 'prepare': cmd_prepare, 'reap': cmd_reap}[args.command](args, repo)


if __name__ == '__main__':
    raise SystemExit(main())
