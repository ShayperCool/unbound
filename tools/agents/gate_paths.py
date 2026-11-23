#!/usr/bin/env python3
"""The parts of other probes' directories each probe's Python code uses (the parts its BASE tuples
list, the files it names, else the whole directory), which its gate.json engine_paths must name so
that land.py selects the probe when they change.

    gate_paths.py            list the probes that use undeclared parts (exit 1 when any)
    gate_paths.py --write    add them to the probes' gate.json engine_paths
"""
import ast
import json
import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
INLINE = re.compile(r"tests/projects/([A-Za-z0-9_.\-]+(?:/[^\s'\"]*)?)")
SKIP_DIRS = {'Library', 'LibraryUnbound', 'Reference', 'Temp', 'Logs', 'obj', '__pycache__'}


def docstrings(tree):
    """The ids of the string constants that are docstrings (they name paths in prose)."""
    found = set()
    for node in ast.walk(tree):
        if isinstance(node, (ast.Module, ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef)) and node.body:
            first = node.body[0]
            if isinstance(first, ast.Expr) and isinstance(first.value, ast.Constant) and isinstance(first.value.value, str):
                found.add(id(first.value))
    return found


def probe_div(node):
    """'X/sub/file' of PROBE.parent / 'X' / 'sub/file' (any chain of constant parts), from
    Path(__file__).parents[1] (a probe's top-level script) or PROJECTS, else None."""
    parts = []
    while isinstance(node, ast.BinOp) and isinstance(node.op, ast.Div) and isinstance(node.right, ast.Constant) \
            and isinstance(node.right.value, str):
        parts.insert(0, node.right.value.strip('/'))
        node = node.left
    if not parts:
        return None
    if (isinstance(node, ast.Attribute) and node.attr == 'parent') \
            or (isinstance(node, ast.Name) and node.id in ('PROJECTS', 'projects')) \
            or (isinstance(node, ast.Subscript) and isinstance(node.value, ast.Attribute) and node.value.attr == 'parents'
                and isinstance(node.slice, ast.Constant) and node.slice.value == 1):
        return '/'.join(parts)
    return None


def uses(source, names=None):
    """The paths under tests/projects a Python source uses; with the probes' <paramref>names</paramref>,
    also string constants that are a path inside one of them ('X/Assets/...', as copied lists hold)."""
    paths = set()
    try:
        tree = ast.parse(source)
    except SyntaxError:
        return paths
    docs, handled = docstrings(tree), set()
    for node in ast.walk(tree):
        # (PROBE.parent/'X', ('part', ...)): those parts of X.
        if isinstance(node, ast.Tuple) and len(node.elts) == 2 and probe_div(node.elts[0]) \
                and isinstance(node.elts[1], (ast.Tuple, ast.List)) and node.elts[1].elts \
                and all(isinstance(e, ast.Constant) and isinstance(e.value, str) for e in node.elts[1].elts):
            base = probe_div(node.elts[0]).strip('/')
            inner = node.elts[0]
            while isinstance(inner, ast.BinOp):
                handled.add(id(inner))
                inner = inner.left
            paths.update(f'{base}/{e.value.strip("/")}' for e in node.elts[1].elts)
    for node in ast.walk(tree):
        if id(node) in handled:
            continue
        rel = probe_div(node)
        if rel:
            paths.add(rel.strip('/'))
            # The chain's shorter prefixes (walked after it) are the same use.
            inner = node.left
            while isinstance(inner, ast.BinOp):
                handled.add(id(inner))
                inner = inner.left
        if isinstance(node, ast.Constant) and isinstance(node.value, str) and id(node) not in docs:
            paths.update(m.strip('/') for m in INLINE.findall(node.value))
            value = node.value.strip('/')
            if names and '/' in value and value.split('/')[0] in names and not any(c.isspace() for c in value):
                paths.add(value)
    return paths


def undeclared(repo=REPO, only=None):
    """{probe: [engine_paths entries its gate.json lacks]} for the probes with a Reference (those
    named in <paramref>only</paramref>, when given)."""
    projects = repo/'tests/projects'
    names = {p.name for p in projects.iterdir() if p.is_dir()}
    result = {}
    for p in sorted(projects.iterdir()):
        if not p.is_dir() or not (p/'Reference').is_dir() or (only is not None and p.name not in only):
            continue
        used = set()
        for f in list(p.rglob('*.py')) + list(p.rglob('*.sh')):
            # Unit tests of the probe's scripts are not run by its gate.
            if any(part in SKIP_DIRS for part in f.relative_to(p).parts) or f.name.startswith('test_'):
                continue
            text = f.read_text(errors='replace')
            if f.suffix == '.sh':
                used.update(m.strip('/') for m in INLINE.findall(text))
            else:
                used |= uses(text, names - {p.name})
        gate = p/'gate.json'
        spec = json.loads(gate.read_text()) if gate.exists() else {}
        for command in spec.get('commands', []):
            used.update(m.strip('/') for m in INLINE.findall(command))
        wanted = []
        for rel in sorted(used):
            top = rel.split('/')[0]
            path = projects/rel
            if top not in names or top == p.name or not path.exists():
                continue
            entries = [f'tests/projects/{rel}/'] if path.is_dir() else [f'tests/projects/{rel}']
            # A named asset's import settings are its .meta beside it.
            if path.is_file() and path.with_name(path.name + '.meta').is_file():
                entries.append(f'tests/projects/{rel}.meta')
            for entry in entries:
                declared = spec.get('engine_paths', []) + wanted
                if any(entry == d or entry.startswith(d if d.endswith('/') else d + '/') for d in declared):
                    continue
                if entry.endswith('/'):
                    wanted = [w for w in wanted if not w.startswith(entry)]
                wanted.append(entry)
        if wanted:
            result[p.name] = wanted
    return result


def main():
    missing = undeclared()
    for name, entries in missing.items():
        print(f'{name}: {", ".join(entries)}')
        if '--write' in sys.argv:
            gate = REPO/'tests/projects'/name/'gate.json'
            spec = json.loads(gate.read_text()) if gate.exists() else {}
            spec['engine_paths'] = spec.get('engine_paths', []) + entries
            gate.write_text(json.dumps(spec, indent=2, ensure_ascii=False) + '\n')
    return int(bool(missing) and '--write' not in sys.argv)


if __name__ == '__main__':
    sys.exit(main())
