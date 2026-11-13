"""The spriteatlas comparator: every value of a capture against the reference, exactly (images
within 2 per channel). Page textures pair up by their names without the Editor's hash, which
Unbound does not reproduce; names equal up to it are listed residuals."""
import base64
import json
import math
import re

# A page texture's name ends with a hash of the Editor's import state (`sactx-...-<8 hex>`).
PAGE_NAME = re.compile(r'^(sactx-.*)-[0-9a-f]{8}((-var-.*)?)$')


def compare(reference, candidate, out, label='spriteatlas'):
    problems = []
    residuals = []
    count = 0

    def walk(a, b, key):
        nonlocal count
        if isinstance(a, dict) and isinstance(b, dict):
            for name in sorted(a.keys() | b.keys()):
                child = key+'.'+name
                if name not in a or name not in b:
                    problems.append(f'{child}: reference={a.get(name, "<missing>")} candidate={b.get(name, "<missing>")}')
                else:
                    walk(a[name], b[name], child)
        elif isinstance(a, list) and isinstance(b, list):
            if len(a) != len(b):
                problems.append(f'{key}: lengths {len(a)} vs {len(b)}')
            for i in range(max(len(a), len(b))):
                if i >= min(len(a), len(b)):
                    problems.append(f'{key}[{i}]: reference={a[i] if i < len(a) else "<missing>"} candidate={b[i] if i < len(b) else "<missing>"}')
                else:
                    walk(a[i], b[i], f'{key}[{i}]')
        elif key == '.image' and isinstance(a, str) and isinstance(b, str):
            pa, pb = bytes.fromhex(a), bytes.fromhex(b)
            if len(pa) != len(pb):
                problems.append(f'image: byte counts {len(pa)} vs {len(pb)}')
            for i in range(min(len(pa), len(pb))):
                count += 1
                if abs(pa[i] - pb[i]) > 2:
                    problems.append(f'image pixel {i//3} channel {i%3}: {pa[i]} vs {pb[i]}')
        elif key.endswith('.texels') and isinstance(a, str) and isinstance(b, str):
            pa, pb = base64.b64decode(a), base64.b64decode(b)
            if len(pa) != len(pb):
                problems.append(f'{key[1:]}: byte counts {len(pa)} vs {len(pb)}')
            for i in range(min(len(pa), len(pb))):
                count += 1
                if pa[i] != pb[i]:
                    problems.append(f'{key[1:]} texel {i//4} channel {i%4}: {pa[i]} vs {pb[i]}')
        else:
            count += 1
            equal = a == b
            if isinstance(a, (int, float)) and not isinstance(a, bool) and isinstance(b, (int, float)):
                equal = math.isfinite(b) and abs(a-b) <= 1e-5 * max(1, abs(a))
            if not equal and isinstance(a, str) and isinstance(b, str):
                ma, mb = PAGE_NAME.match(a), PAGE_NAME.match(b)
                if ma and mb and ma.groups() == mb.groups():
                    residuals.append(f'{key}: {a!r} vs {b!r}')
                    return
                # Clone and atlas names that carry a page name.
                ra, rb = re.sub(r'-[0-9a-f]{8}', '', a), re.sub(r'-[0-9a-f]{8}', '', b)
                if 'sactx-' in a and ra == rb:
                    residuals.append(f'{key}: {a!r} vs {b!r}')
                    return
            if not equal:
                problems.append(f'{key}: {a!r} vs {b!r}')
    def by_page_name(capture):
        # Pages pair up by their names without the hash; a missing or extra page is reported.
        capture = dict(capture)
        capture['pages'] = {re.sub(r'-[0-9a-f]{8}', '', p['name']): p for p in capture.get('pages') or []}
        return capture
    walk(by_page_name(reference), by_page_name(candidate), '')
    (out/'differences.json').write_text(json.dumps({'differences': problems, 'listed_residuals': residuals}, indent=2)+'\n')
    print(f'{label}: {count} values, {len(problems)} differences, {len(residuals)} listed residual values (page name hashes); {out}')
    for problem in problems[:20]:
        print(problem)
    return bool(problems)
