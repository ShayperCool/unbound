#!/usr/bin/env python3
"""Resolve candidate suite recipes without sharing the leader's imported modules."""
import importlib.util
import contextlib
import json
from pathlib import Path
import sys


def main():
    source, projects = map(Path, sys.argv[1:])
    requested = json.load(sys.stdin)
    sys.path.insert(0, str(source.resolve().parent))
    spec = importlib.util.spec_from_file_location('suite', source)
    suite = importlib.util.module_from_spec(spec)
    sys.modules['suite'] = suite
    tables, results = {}, {}
    with contextlib.redirect_stdout(sys.stderr):
        spec.loader.exec_module(suite)
        for name, release in requested:
            if release not in tables:
                tables[release] = suite.recipes(release)
            table = tables[release]
            recipe = suite.recipe_for(name, table, release, projects=projects)
            error = (str(recipe.reason) if isinstance(recipe, suite.NoRecipe) else
                     'candidate recipe has no steps' if not recipe else None)
            results[name] = {'builtin': name in table, 'error': error}
    print(json.dumps(results))


if __name__ == '__main__':
    main()
