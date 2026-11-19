#!/usr/bin/env python3
"""schema.py REFERENCE OUT: the TextureImporter type tree the Editor iterates (the reference's
`schema/Sheet/...` rows: every array emptied and given one new element) as the engine's table
(managed/UnityEngine/Editor/Data/importer-types-<family>.json), in the format of
tests/projects/settings/schema.py: per importer class its tree, each node's name, type name,
SerializedPropertyType, hidden, a new element's scalar value and children."""
import json
import sys

ref, out = sys.argv[1], sys.argv[2]
PREFIX = 'schema/Sheet/'
nodes, roots = {}, []
for line in open(ref):
    row = json.loads(line)
    if not row['key'].startswith(PREFIX):
        continue
    path = row['key'][len(PREFIX):]
    name, type_name, kind, _depth, _array, editable, visible, value = row['value'].split('|', 7)
    kind = int(kind)
    node = {'n': name, 't': type_name, 'p': kind}
    if visible != 'visible':
        node['h'] = True
    if editable != 'editable':
        node['r'] = True
    if kind in (0, 12):
        node['d'] = int(value)
    elif kind == 1:
        node['d'] = value == 'true'
    elif kind == 2:
        node['d'] = float(value)
    elif kind == 3:
        node['d'] = value
    nodes[path] = node
    parent = path.rsplit('.', 1)[0] if '.' in path else None
    if parent is None:
        roots.append(node)
    else:
        nodes[parent].setdefault('c', []).append(node)
with open(out, 'w') as o:
    json.dump({'TextureImporter': {'tree': roots}}, o, separators=(',', ':'))
    o.write('\n')
print(f'{len(nodes)} nodes')
