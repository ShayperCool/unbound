"""Queued, ordered landings sharing a gate. Git operations stay behind the land lock."""
import contextlib
import datetime
import fcntl
import hashlib
import json
import os
import re
from pathlib import Path
import subprocess
import sys
import time
import uuid

POLL_SECONDS = 0.5
BUILD_ENVIRONMENT = ('CARGO_TARGET_DIR', 'CARGO_BUILD_BUILD_DIR', 'UNBOUND_BUILD_ROOT')


class Queue:
    def __init__(self, api):
        self.api = api
        self.root = api.STATE
        self.root.mkdir(parents=True, exist_ok=True)

    @contextlib.contextmanager
    def edit(self):
        with (self.root/'land-queue.lock').open('a') as lock:
            fcntl.flock(lock, fcntl.LOCK_EX)
            path = self.root/'land-queue.json'
            try:
                state = json.loads(path.read_text()) if path.exists() else {'version': 1, 'entries': []}
                if state['version'] != 1 or not isinstance(state['entries'], list):
                    raise ValueError('unsupported queue format')
                for entry in state['entries']:
                    if (not isinstance(entry, dict) or not isinstance(entry.get('id'), str)
                            or not re.fullmatch('[0-9a-f]{32}', entry['id'])):
                        raise ValueError('invalid request identity')
            except (OSError, ValueError, KeyError) as error:
                self.api.die(2, f'cannot read landing queue: {error}')
            before = json.dumps(state, sort_keys=True)
            yield state['entries']
            if json.dumps(state, sort_keys=True) != before:
                self.api.atomic_json(path, state)

    def entries(self):
        with self.edit() as entries:
            return [dict(entry) for entry in entries]

    def update(self, request_id, **values):
        with self.edit() as entries:
            for entry in entries:
                if entry['id'] == request_id:
                    entry.update(values)
                    break

    def remove(self, request_id):
        with self.edit() as entries:
            entries[:] = [entry for entry in entries if entry['id'] != request_id]
        (self.root/'requests'/f'{request_id}.lock').unlink(missing_ok=True)

    def live(self, entry):
        path = self.root/'requests'/f"{entry['id']}.lock"
        try:
            with path.open('r') as lock:
                try:
                    fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
                except BlockingIOError:
                    return True
        except FileNotFoundError:
            pass
        return False

    @contextlib.contextmanager
    def register(self, entry):
        directory = self.root/'requests'
        directory.mkdir(exist_ok=True)
        path = directory/f"{entry['id']}.lock"
        try:
            with path.open('x') as lock:
                fcntl.flock(lock, fcntl.LOCK_SH)
                with self.edit() as entries:
                    if any(item['worktree'] == entry['worktree'] and not item.get('result') and self.live(item)
                           for item in entries):
                        self.api.die(2, f"{entry['worktree']} already has a queued landing")
                    entries.append(entry)
                try:
                    yield
                finally:
                    # A killed leader leaves pending requests for the next live caller.
                    current = next((item for item in self.entries() if item['id'] == entry['id']), None)
                    if current and current.get('result'):
                        self.remove(entry['id'])
        finally:
            path.unlink(missing_ok=True)



class Workspace:
    def __init__(self, train, repo, base, original=None):
        self.train, self.repo, self.base = train, repo, base
        self.original = original
        self.path = Path(original) if original else None
        self.owned = set()
        self.refs = []

    def create(self):
        key = hashlib.sha256(self.repo.encode()).hexdigest()[:12]
        root = self.train.api.integration_root().resolve()
        root.mkdir(parents=True, exist_ok=True)
        path = root/f'train-worktree-{key}'
        if os.path.lexists(path):
            # An interrupted gate can still own children or uncommitted work here.
            # Leave it intact and give this train a fresh checkout.
            path = path.with_name(path.name+'-'+uuid.uuid4().hex[:12])
        self.train.git('worktree', 'add', '--quiet', '--detach', str(path), self.base)
        self.path = path
        self.train.git('worktree', 'lock', '--reason', 'active land train '+self.train.id, str(path))
        self.owned.add(self.base)

    def checkout(self, revision):
        if self.path is None:
            self.create()
        try:
            self.train.api.require_clean(self.path, 'integration worktree')
        except SystemExit:
            if self.original:
                raise
            print(f'land: retaining changed integration worktree {self.path}', file=sys.stderr)
            self.create()
        self.train.git('switch', '--quiet', '--detach', revision, cwd=self.path)
        self.owned.add(revision)

    def retain_tip(self, request_id, tip):
        self.owned.add(tip)
        if not self.original:
            ref = f'refs/unbound/land-trains/{self.train.id}/{request_id}'
            self.train.git('update-ref', ref, tip)
            self.refs.append(ref)

    def close(self):
        if not self.original and self.path is not None:
            from runtime_snapshot import finish_task
            finish_task(self.path, repo=self.repo)
        if not self.original and self.path is not None:
            try:
                self.train.api.require_clean(self.path, 'integration worktree')
                head = self.train.git('rev-parse', 'HEAD', cwd=self.path)
                if head not in self.owned:
                    raise ValueError('checkout acquired an independent commit')
                self.train.git('worktree', 'unlock', str(self.path))
                self.train.git('worktree', 'remove', str(self.path))
            except (SystemExit, ValueError):
                print(f'land: retaining integration worktree {self.path}', file=sys.stderr)
        for ref in self.refs:
            self.train.git('update-ref', '-d', ref)


class LandTrain:
    def __init__(self, api):
        self.api = api
        self.queue = Queue(api)
        self.id = uuid.uuid4().hex
        self.repo = None
        self.worktree = None

    def git(self, *args, cwd=None, **kwargs):
        return self.api.git(*args, cwd=cwd or self.worktree, **kwargs)

    def ancestor(self, older, newer):
        return subprocess.run(['git', 'merge-base', '--is-ancestor', older, newer],
                              cwd=self.worktree, capture_output=True).returncode == 0

    def log_path(self, label):
        logs = self.api.STATE/'logs'
        logs.mkdir(parents=True, exist_ok=True)
        return logs/f'{datetime.datetime.now():%Y%m%d-%H%M%S}-{self.id[:12]}-{label}.log'

    def reject(self, entry, code, message, log=None):
        self.queue.update(entry['id'], result={'code': code, 'message': message,
                                             'log': str(log) if log else None, 'train_id': self.id})

    def valid(self, entry):
        if not self.queue.live(entry):
            self.queue.remove(entry['id'])
            return False
        path = Path(entry['worktree'])
        try:
            if self.git('rev-parse', '--path-format=absolute', '--git-common-dir', cwd=path) != entry['repo']:
                raise ValueError('repository changed')
            if self.git('symbolic-ref', '--quiet', '--short', 'HEAD', cwd=path) != entry['branch']:
                raise ValueError('checked-out branch changed')
            if self.git('rev-parse', 'HEAD', cwd=path) != entry['head']:
                raise ValueError('branch advanced while queued')
            self.api.require_clean(path, f"queued worktree {path}")
        except (SystemExit, ValueError) as error:
            self.reject(entry, 2, f'queued worktree changed; retry the landing: {error}')
            return False
        return True

    def record(self, entry, result):
        path = self.api.STATE/'landed.jsonl'
        # Prepared records make recovery idempotent if a leader exits after advancing main.
        from land_push import journal
        with journal(self.api.STATE):
            known = {}
            if path.exists():
                for line in path.read_text().splitlines():
                    row = json.loads(line)
                    known[row.get('request_id')] = row
            if entry['id'] not in known:
                row = {'time': f'{datetime.datetime.now():%F %T}', 'branch': entry['branch'],
                       'old': result['old'], 'new': result['new'], 'commits': result['commits'],
                       'worktree': entry['worktree'], 'request_id': entry['id'],
                       'train_id': result['train_id'], 'log': result.get('log')}
                with path.open('a') as stream:
                    stream.write(json.dumps(row)+'\n')
                    stream.flush()
                    os.fsync(stream.fileno())
            else:
                row = known[entry['id']]
                result = dict(result, **{key: row[key] for key in ('old', 'new', 'commits', 'train_id', 'log')})
        self.queue.update(entry['id'], result=result)

    def publications(self):
        path = self.api.STATE/'land-publications.json'
        if not path.exists():
            return {}
        value = json.loads(path.read_text())
        if value['version'] != 1:
            self.api.die(2, 'unsupported landing publication format')
        return value['trains']

    def save_publications(self, trains):
        self.api.atomic_json(self.api.STATE/'land-publications.json', {'version': 1, 'trains': trains})

    def prepare_publication(self, prepared):
        trains = self.publications()
        for entry, result in prepared:
            train = trains.setdefault(result['train_id'], {'repo': entry['repo'], 'entries': {}})
            snapshot = {key: entry[key] for key in ('id', 'branch', 'worktree', 'repo')}
            train['entries'][entry['id']] = {'entry': snapshot, 'result': result}
            # Keep decided commits reachable independently of caller worktrees.
            ref = f"refs/unbound/land-publications/{result['train_id']}/{entry['id']}"
            self.git('update-ref', ref, result['new'])
        self.save_publications(trains)

    def publish_pending(self, main_checkout):
        if not main_checkout:
            return
        trains, delivered = self.publications(), []
        for train_id, train in trains.items():
            if train['repo'] != self.repo:
                continue
            for request_id, item in train['entries'].items():
                if self.ancestor(item['result']['new'], 'main'):
                    self.record(item['entry'], item['result'])
                    delivered.append((train_id, request_id))
        if not delivered:
            return
        # The intent survives both completed caller results and an interrupted push.
        # Replaying a push after a crash is safe; a healthy train launches it once.
        if self.api.push_main(main_checkout) is False:
            return
        from runtime_snapshot import release_task, cleanup
        try:
            for train_id, request_id in delivered:
                item = trains[train_id]['entries'][request_id]['entry']
                release_task(item['worktree'], item['branch'])
            cleanup(days=1/24, repo=self.repo)
        except (OSError, ValueError) as error:
            print(f'land: runtime cleanup deferred: {error}', file=sys.stderr)
        for train_id, request_id in delivered:
            del trains[train_id]['entries'][request_id]
            if not trains[train_id]['entries']:
                del trains[train_id]
        self.save_publications(trains)
        for train_id, request_id in delivered:
            self.git('update-ref', '-d', f'refs/unbound/land-publications/{train_id}/{request_id}')

    def recover(self, entries):
        for entry in entries:
            prepared = entry.get('prepared')
            if prepared and self.ancestor(prepared['new'], 'main'):
                self.prepare_publication([(entry, prepared)])
                self.record(entry, prepared)
                if not self.queue.live(entry):
                    self.queue.remove(entry['id'])
            elif not self.queue.live(entry):
                self.queue.remove(entry['id'])

    def recover_ready(self, request):
        self.worktree = Path(request['worktree'])
        self.repo = request['repo']
        main_checkout = self.api.main_worktree(cwd=self.worktree)
        if main_checkout:
            self.worktree = Path(main_checkout)
        try:
            self.recover([item for item in self.queue.entries()
                          if item['repo'] == self.repo and not item.get('result')])
        finally:
            self.publish_pending(main_checkout)

    def oracle(self, entry, before, after, workspace):
        log = self.log_path('oracle-'+entry['id'][:10])
        command = [sys.executable, str(self.api.HERE/'oracle_guard.py'), before, after]
        result = subprocess.run(command, cwd=workspace.path, capture_output=True, text=True)
        log.write_text(result.stdout+result.stderr)
        if result.returncode:
            self.reject(entry, 4, 'oracle guard refused the change; full log: '+str(log), log)
            return False
        return True

    def assemble(self, entries, base, workspace):
        accepted, tip = [], base
        for entry in entries:
            if entry.get('automatic_error'):
                self.reject(entry, 2, 'automatic probe coverage could not be recovered: '+entry['automatic_error'])
                continue
            if not self.valid(entry):
                continue
            path = Path(entry['worktree']) if workspace.original else workspace.path
            if not workspace.original:
                workspace.checkout(entry['head'])
                path = workspace.path
            elif not self.ancestor(base, entry['head']):
                print('land: rebasing onto main', flush=True)
            command = ['git', 'rebase', '--quiet', '--empty=drop', '--onto', tip, base]
            result = subprocess.run(command, cwd=path, capture_output=True, text=True)
            if result.returncode:
                conflicts = self.git('diff', '--name-only', '--diff-filter=U', cwd=path, check=False)
                log = self.log_path('conflict-'+entry['id'][:10])
                log.write_text(result.stdout+result.stderr)
                subprocess.run(['git', 'rebase', '--abort'], cwd=path, capture_output=True)
                self.reject(entry, 3, 'conflicts with main in:\n  '+
                            '\n  '.join(conflicts.splitlines() or ['(see git output)'])+
                            '\nRun `git rebase main` yourself and resolve, re-test, then land again. '
                            f'Full log: {log}', log)
                continue
            after = self.git('rev-parse', 'HEAD', cwd=path)
            if workspace.original:
                entry['head'] = after
                self.queue.update(entry['id'], head=after)
            workspace.retain_tip(entry['id'], after)
            if after == tip:
                self.reject(entry, 2, 'nothing to land: the branch has no commits beyond main')
                continue
            if not self.oracle(entry, tip, after, workspace):
                continue
            changed = self.git('diff', '--name-only', '--no-renames', tip, after, cwd=path).splitlines()
            selector = self.api.Gate(path, tip, [], False, self.log_path('selection'))
            try:
                automatic = self.api.automatic_checks(path, self.commit_paths(entry['head']),
                                                       entry['probes'], entry.get('probe_environment', {}),
                                                       full=entry['full'])
                # Candidate metadata includes earlier branches. Keep new checks
                # attached to their owner, while retaining this branch's original
                # and inherited requirements across failure isolation.
                own = {self.api.automatic_key(row[1], row[3]) for row in entry.get('automatic', [])}
                earlier = {self.api.automatic_key(row[1], row[3]) for item in accepted
                           for row in item.get('automatic', [])}
                automatic = [row for row in automatic
                             if self.api.automatic_key(row[1], row[3]) not in earlier
                             or self.api.automatic_key(row[1], row[3]) in own]
                automatic = self.api.automatic_union([entry.get('automatic', []), automatic])
            except (SystemExit, ValueError) as error:
                self.reject(entry, 2, f'automatic probe selection failed: {error}')
                continue
            entry.update(before=tip, tip=after, changed=changed, crates=selector.touched_crates(changed),
                         automatic=automatic)
            self.save_automatic(entry, entries)
            accepted.append(entry)
            tip = after
        return accepted

    @staticmethod
    def context(entry):
        return {key: entry[key] for key in ('id', 'branch', 'tip', 'probes', 'releases', 'changed', 'crates', 'full', 'automatic')}

    def gate(self, entries, base, workspace, context):
        selected = [*context, *entries]
        probes = list(dict.fromkeys((command, release) for item in selected
                                   for command, release in zip(item['probes'], item['releases'])))
        automatic = self.api.automatic_union([item.get('automatic', []) for item in selected], probes)
        changed = sorted({path for item in selected for path in item['changed']})
        crates = sorted({crate for item in selected for crate in item['crates']})
        log = self.log_path('gate-'+entries[-1]['id'][:10])
        target = entries[-1]['tip']
        if not workspace.original:
            workspace.checkout(target)
        environment = dict(os.environ)
        for key, value in entries[0]['build_environment'].items():
            if value is None:
                environment.pop(key, None)
            else:
                environment[key] = value
        if not workspace.original:
            for key in ('CARGO_TARGET_DIR', 'CARGO_BUILD_BUILD_DIR', 'UNBOUND_MANAGED_DIR',
                        'UNBOUND_RUNTIME', 'UNBOUND', 'UNBOUND_CLI'):
                environment.pop(key, None)
        try:
            runtime = self.api.run_gate(workspace.path, base, [item[0] for item in probes],
                                        any(item['full'] for item in selected), 'train-'+self.id[:12],
                                        priority=True, log=log, changed=changed, crates=crates,
                                        releases=[item[1] for item in probes], environment=environment, automatic=automatic,
                                        probe_owners=[[item.get('branch', item['id']) for item in selected
                                                       if pair in zip(item['probes'], item['releases'])]
                                                      for pair in probes])
            self.api.require_clean(workspace.path, 'gated worktree')
            if self.git('rev-parse', 'HEAD', cwd=workspace.path) != target:
                raise ValueError('gated checkout acquired a different commit')
        except (SystemExit, ValueError) as error:
            code = error.code if isinstance(error, SystemExit) and isinstance(error.code, int) else 2
            with log.open('a') as stream:
                stream.write(f'\nland: gate failed (exit {code}): {error}\n')
            return {'code': code, 'log': str(log), 'runtime': None}
        return {'code': 0, 'log': str(log), 'runtime': str(runtime) if runtime else None}

    def advance(self, entries, outcome, context, remaining, main_checkout, workspace):
        old_tree = self.git('rev-parse', 'main^{tree}')
        extra = [self.context(entry) for entry in entries]
        # Preserve earlier checks across failure isolation and leader restarts.
        for entry in remaining:
            inherited = {item['id']: item for item in [*context, *extra]}
            self.queue.update(entry['id'], context=list(inherited.values()))
        prepared = []
        preceding = list(context)
        for entry in entries:
            self.queue.update(entry['id'], context=list(preceding))
            preceding.append(self.context(entry))
            record = {'code': 0, 'old': entry['before'], 'new': entry['tip'],
                      'commits': int(self.git('rev-list', '--count', entry['before']+'..'+entry['tip'])),
                      'train_id': self.id, 'log': outcome['log']}
            self.queue.update(entry['id'], prepared=record)
            prepared.append((entry, record))
        self.prepare_publication(prepared)
        for entry, record in prepared:
            if main_checkout:
                self.api.require_clean(main_checkout, f'the main checkout {main_checkout}', allow_untracked=True)
                self.git('merge', '--ff-only', '--quiet', entry['tip'], cwd=main_checkout)
            else:
                self.git('update-ref', 'refs/heads/main', entry['tip'], entry['before'])
            self.record(entry, record)
        try:
            runtime = Path(outcome['runtime']) if outcome['runtime'] else None
            self.api.pin_main(runtime, self.worktree, self.git('rev-parse', 'main^{tree}'), previous_tree=old_tree)
        except (OSError, ValueError, subprocess.CalledProcessError) as error:
            print(f'land: main advanced, but its runtime pin could not be saved: {error}', file=sys.stderr)
        return [*context, *extra]

    def commit_paths(self, head):
        # Preserve paths changed and later restored within a stacked branch.
        paths = self.git('log', '--format=', '--name-only', '--no-renames', '-m', '-z',
                         'main..'+head).split('\0')
        return sorted(set(filter(None, paths)))

    def save_automatic(self, entry, entries):
        self.queue.update(entry['id'], automatic=entry['automatic'])
        for child in entries:
            if child['id'] != entry['id'] and entry['head'] in self.lineage.get(child['id'], ()):
                child['automatic'] = self.api.automatic_union(
                    [child.get('automatic', []), entry['automatic']])
                self.queue.update(child['id'], automatic=child['automatic'])

    def inherit_probes(self, entries, workspace):
        # A stacked branch can carry another queued branch's commits even if that
        # earlier request is rejected. Keep the checks attached to those commits.
        self.lineage = {}
        for entry in entries:
            if 'automatic' in entry:
                continue
            # Legacy requests have no captured selection. Read their committed
            # snapshot in our checkout, leaving even a dirty caller untouched.
            workspace.checkout(entry['head'])
            try:
                entry['automatic'] = self.api.automatic_checks(
                    workspace.path, self.commit_paths(entry['head']), entry['probes'],
                    entry.get('probe_environment', {}), full=entry['full'])
            except (SystemExit, ValueError) as error:
                entry['automatic'] = []
                entry['automatic_error'] = f"{entry['branch']}: {error}"
        for entry in entries:
            ancestors = set(self.git('rev-list', 'main..'+entry['head']).splitlines())
            self.lineage[entry['id']] = ancestors
            sources = [entry, *(item for item in entries
                                if item['id'] != entry['id'] and item['head'] in ancestors)]
            specs = list(dict.fromkeys((command, release) for item in sources
                                      for command, release in zip(item['probes'], item['releases'])))
            entry['automatic'] = self.api.automatic_union([item.get('automatic', []) for item in sources])
            errors = [item['automatic_error'] for item in sources if item.get('automatic_error')]
            if errors:
                entry['automatic_error'] = errors[0]
            entry.update(probes=[item[0] for item in specs], releases=[item[1] for item in specs],
                         full=any(item['full'] for item in sources))
            self.queue.update(entry['id'], probes=entry['probes'], releases=entry['releases'],
                              full=entry['full'], automatic=entry['automatic'],
                              automatic_error=entry.get('automatic_error'))

    def lead(self):
        self.id = uuid.uuid4().hex
        records = self.queue.entries()
        for item in records:
            if not self.queue.live(item) and (item.get('result') or not item.get('prepared')):
                self.queue.remove(item['id'])
        live = [item for item in records if not item.get('result') and self.queue.live(item)]
        if not live:
            return
        self.worktree = Path(live[0]['worktree'])
        self.repo = live[0]['repo']
        main_checkout = self.api.main_worktree(cwd=self.worktree)
        if main_checkout:
            self.worktree = Path(main_checkout)
        pending = [item for item in records if item['repo'] == self.repo and not item.get('result')]
        try:
            self.integrate(pending, main_checkout)
        finally:
            self.publish_pending(main_checkout)

    def integrate(self, pending, main_checkout):
        self.recover(pending)
        # Earlier leaders cannot still hold the land lock. Their candidate tips are
        # either on main, retained by a checkout, or rebuilt from the original heads.
        for ref in self.git('for-each-ref', '--format=%(refname)', 'refs/unbound/land-trains/').splitlines():
            self.git('update-ref', '-d', ref)
        pending = [item for item in self.queue.entries()
                   if item['repo'] == self.repo and not item.get('result') and self.queue.live(item)]
        if not pending:
            return
        base = self.git('rev-parse', 'main')
        context = {item['id']: item for entry in pending for item in entry.get('context', [])
                   if self.ancestor(item['tip'], base)}
        context = list(context.values())
        original = (pending[0]['worktree'] if len(pending) == 1 and 'automatic' in pending[0] else None)
        workspace = Workspace(self, self.repo, base, original)
        if not original:
            workspace.create()
        print(f'land: train {self.id[:12]}: {len(pending)} queued branches', flush=True)
        try:
            self.inherit_probes(pending, workspace)
            while pending:
                base = self.git('rev-parse', 'main')
                entries = self.assemble(pending, base, workspace)
                if not entries:
                    break
                owner = next((item for item in self.queue.entries()
                              if item['id'] == getattr(self, 'request_id', None)), None)
                if owner and (owner.get('result') or {}).get('code') not in (None, 0):
                    # Let a live peer take over before starting its gate, so this
                    # caller can report its rejection promptly.
                    return
                outcome = self.gate(entries, base, workspace, context)
                current = [entry for entry in entries if self.valid(entry)]
                if len(current) != len(entries):
                    pending = current
                    continue
                if outcome['code'] == 0:
                    self.advance(entries, outcome, context, [], main_checkout, workspace)
                    break
                low, high, passing = 0, len(entries), None
                failing = outcome
                while high-low > 1:
                    middle = (low+high)//2
                    candidate = self.gate(entries[:middle], base, workspace, context)
                    if candidate['code'] == 0:
                        low, passing = middle, candidate
                    else:
                        high, failing = middle, candidate
                # Recheck original worktrees after all the gates, before moving main.
                current = [entry for entry in entries if self.valid(entry)]
                if len(current) != len(entries):
                    pending = current
                    continue
                if low:
                    context = self.advance(entries[:low], passing, context, entries[low:],
                                           main_checkout, workspace)
                bad = entries[low]
                self.reject(bad, failing['code'], 'gate failed; full log: '+failing['log'], failing['log'])
                pending = entries[low+1:]
        finally:
            workspace.close()

    def finish(self, request, result):
        if result['code']:
            if result.get('log') and Path(result['log']).is_file():
                print(self.api.tail(Path(result['log'])), file=sys.stderr)
            self.api.die(result['code'], result['message'])
        path = Path(request['worktree'])
        try:
            branch = self.git('symbolic-ref', '--quiet', '--short', 'HEAD', cwd=path)
            head = self.git('rev-parse', 'HEAD', cwd=path)
            if branch != request['branch'] or head not in (request['head'], result['new']):
                raise ValueError('worktree branch advanced while queued')
            self.api.require_clean(path, 'this worktree')
            if head != result['new']:
                self.git('reset', '--keep', result['new'], cwd=path)
        except (SystemExit, ValueError):
            print('land: captured commits landed; worktree changed and was retained. '
                  f"Landed tip: {result['new']}", file=sys.stderr)
        print(f"land: landed {request['branch']}: main {result['old'][:10]} -> "
              f"{result['new'][:10]} ({result['commits']} commits)", flush=True)
        return 0

    def run(self, worktree, branch, probes, full):
        self.worktree = Path(worktree)
        head = self.git('rev-parse', 'HEAD')
        if self.ancestor(head, 'main'):
            self.api.die(2, 'nothing to land: the branch has no commits beyond main')
        try:
            releases = [self.api.probe_release(probe, worktree) for probe in probes]
        except ValueError as error:
            self.api.die(2, str(error))
        probe_environment = {self.api.RELEASE_VARIABLE: os.environ.get(self.api.RELEASE_VARIABLE, '')}
        changed = self.commit_paths(head)
        automatic = self.api.automatic_checks(self.worktree, changed, probes, probe_environment, full=full)
        self.api.require_clean(self.worktree, 'this worktree')
        if self.git('rev-parse', 'HEAD') != head:
            self.api.die(2, 'branch changed during probe selection; retry the landing')
        request = {'id': uuid.uuid4().hex, 'branch': branch, 'worktree': str(worktree),
                   'repo': self.git('rev-parse', '--path-format=absolute', '--git-common-dir'),
                   'head': head, 'probes': probes, 'releases': releases, 'full': full,
                   'probe_environment': probe_environment, 'automatic': automatic,
                   'build_environment': {key: os.environ.get(key) for key in BUILD_ENVIRONMENT}}
        self.request_id = request['id']
        with self.queue.register(request):
            while True:
                records = self.queue.entries()
                current = next((item for item in records if item['id'] == request['id']), None)
                pending_recovery = (current and current.get('result') and
                                    current['result'].get('train_id') in self.publications())
                if current and current.get('result') and not pending_recovery:
                    return self.finish(current, current['result'])
                # Join the same blocking lock queue as older callers. Polling a
                # nonblocking lock could starve behind a continuous legacy queue.
                with self.api.land_lock(branch):
                    current = next((item for item in self.queue.entries() if item['id'] == request['id']), None)
                    if current and current.get('result'):
                        self.recover_ready(current)
                    else:
                        self.lead()
                current = next((item for item in self.queue.entries() if item['id'] == request['id']), None)
                if current and current.get('result'):
                    return self.finish(current, current['result'])
                time.sleep(POLL_SECONDS)
