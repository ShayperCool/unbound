#!/usr/bin/env python3
"""The repository's git hooks, kept here because .git/hooks is outside Git.

    python3 tools/agents/git_hooks.py install [--repo <checkout>]

post-commit pushes the committed branch to its upstream in the background, so GitHub follows
every commit of a branch that is meant to be published. It pushes only a branch whose upstream
is a branch of the same name on a remote: a work branch that tracks origin/main (or anything
else under another name) is never pushed, so a commit there cannot reach main. Work branches
are created without an upstream (slot.py prepare) and are left alone. main reaches origin
through land.py. Refusals are logged with the pushes in <git common dir>/auto-push.log.
slot.py prepare installs the current version, so a checkout keeps it up to date.
"""
import argparse
import os
from pathlib import Path
import subprocess
import sys

POST_COMMIT = r'''#!/bin/sh
# Installed by tools/agents/git_hooks.py; edit it there.
# Push the committed branch to its upstream in the background, so GitHub follows every commit.
# Only a branch whose upstream is the same-named branch on a remote is pushed: never a branch
# into another name (a work branch tracking origin/main must not push to main).
# Log: <git common dir>/auto-push.log
ref=$(git symbolic-ref --quiet HEAD) || exit 0
branch=${ref#refs/heads/}
[ "$branch" != "$ref" ] || exit 0
remote=$(git config --get "branch.$branch.remote") || exit 0
merge=$(git config --get "branch.$branch.merge") || exit 0
log="$(git rev-parse --git-common-dir)/auto-push.log"
remote_branch=${merge#refs/heads/}
if [ "$remote" = "." ] || [ "$merge" = "$remote_branch" ] || [ "$remote_branch" != "$branch" ] ||
   { [ "$remote_branch" = "main" ] && [ "$branch" != "main" ]; }; then
  echo "[$(date '+%F %T')] $branch: upstream $remote $merge is not its own branch; not pushed" >>"$log" 2>&1
  exit 0
fi
(
  echo "[$(date '+%F %T')] $branch -> $remote/$remote_branch"
  git push --quiet "$remote" "refs/heads/$branch:refs/heads/$branch" 2>&1 || echo "  push failed"
) >>"$log" 2>&1 </dev/null &
exit 0
'''

HOOKS = {'post-commit': POST_COMMIT}
# The first comment line of every hook written here; another hook found in its place is kept
# beside it (<name>.local) before it is replaced.
MARKER = '# Installed by tools/agents/git_hooks.py'


def hooks_dir(repo):
    """The common hooks directory of every worktree of repo. A core.hooksPath setting would make
    git run other hooks than these, so it is refused."""
    configured = subprocess.run(['git', '-C', str(repo), 'config', '--get', 'core.hooksPath'],
                                capture_output=True, text=True).stdout.strip()
    if configured:
        raise SystemExit(f'core.hooksPath is set to {configured} for {repo}: git would not run the hooks '
                         f'of tools/agents/git_hooks.py; unset it (git config --unset core.hooksPath)')
    common = subprocess.run(['git', '-C', str(repo), 'rev-parse', '--path-format=absolute', '--git-common-dir'],
                            check=True, capture_output=True, text=True).stdout.strip()
    return Path(common)/'hooks'


def install(repo):
    """Write each hook that differs, atomically; returns the names written."""
    directory = hooks_dir(repo)
    directory.mkdir(exist_ok=True)
    written = []
    for name, text in HOOKS.items():
        path = directory/name
        try:
            current = path.read_text()
        except FileNotFoundError:
            current = None
        if current == text and os.access(path, os.X_OK):
            continue
        if current is not None and MARKER not in current:
            backup = path.with_name(name+'.local')
            if not backup.exists():
                backup.write_text(current)
        temporary = path.with_name(f'.{name}.{os.getpid()}')
        temporary.write_text(text)
        temporary.chmod(0o755)
        os.replace(temporary, path)
        written.append(name)
    return written


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest='command', required=True)
    command = sub.add_parser('install', help='write the hooks into the repository')
    command.add_argument('--repo', default='.')
    args = parser.parse_args(argv)
    written = install(args.repo)
    print(f'hooks in {hooks_dir(args.repo)}: ' + (', '.join(written) + ' written' if written else 'up to date'))
    return 0


if __name__ == '__main__':
    sys.exit(main())
