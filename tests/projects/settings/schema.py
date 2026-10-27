#!/usr/bin/env python3
"""schema.py REFERENCE OUT: the settings classes' type trees (Reference/schema.jsonl, written by
ProbeRunner.Schema) and what AssetDatabase loads at each path (settings.jsonl's load records) as the
engine's table (managed/UnityEngine/Editor/Data/settings-types-<family>.json): per file its GUID,
the object's class and name, and the tree — name, type name, SerializedPropertyType, visible,
editable, a new element's value, children (an array's: Array, then size and its element)."""
import json, os, sys

ref, out = sys.argv[1], sys.argv[2]
loads = {}
for line in open(os.path.join(ref, "settings.jsonl")):
    r = json.loads(line)
    if r["case"] == "load":
        loads[r["file"]] = r
files = {}
for line in open(os.path.join(ref, "schema.jsonl")):
    r = json.loads(line)
    f = files.setdefault(r["file"], {"nodes": {}, "roots": []})
    node = {"n": r["name"], "t": r["typeName"], "p": r["type"]}
    if not r["visible"]:
        node["h"] = True
    if not r["editable"]:
        node["r"] = True
    if r["value"] is not None and r["type"] in (0, 1, 2, 3, 7, 12):
        node["d"] = r["value"]
    path = r["path"]
    f["nodes"][path] = node
    parent = path.rsplit(".", 1)[0] if "." in path else None
    if parent is None:
        f["roots"].append(node)
    else:
        f["nodes"][parent].setdefault("c", []).append(node)
table = {}
for name, f in sorted(files.items()):
    load = loads[name]
    table[name] = {"guid": load["guid"], "class": load["types"][0], "name": load["names"][0], "tree": f["roots"]}
with open(out, "w") as o:
    json.dump(table, o, separators=(",", ":"), sort_keys=False)
    o.write("\n")
print(f"{len(table)} files, {sum(len(f['nodes']) for f in files.values())} nodes")
