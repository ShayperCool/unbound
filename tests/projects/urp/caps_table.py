#!/usr/bin/env python3
"""Writes managed/UnityEngine/Platform/GraphicsCaps.Generated.cs from the Editor's recorded caps
(Reference/caps-<version>.jsonl, CapsProbe.Record): per GraphicsFormat the GraphicsFormatUsage mask
the device supports and its compatible formats for rendering, sampling, blending and random writes.

    python3 tests/projects/urp/caps_table.py tests/projects/urp/Reference/caps-6000.0.71f1.jsonl
"""
import json
import sys

values = {}
for line in open(sys.argv[1]):
    entry = json.loads(line)
    values[entry["key"]] = entry["value"]

formats = sorted(int(k.split(".")[1]) for k in values if k.startswith("usages."))
size = max(formats) + 1


def table(prefix):
    row = [0] * size
    for f in formats:
        row[f] = int(values[f"{prefix}{f}"])
    return row


def emit(name, kind, row):
    lines = []
    for i in range(0, len(row), 16):
        lines.append("            " + ", ".join(str(v) for v in row[i:i + 16]) + ",")
    return f"        internal static readonly {kind}[] {name} =\n        {{\n" + "\n".join(lines) + "\n        };\n"


out = [
    "// Generated from the Unity Editor's graphics device answers (OpenGLCore on Linux), recorded by",
    "// tests/projects/urp/Assets/Editor/CapsProbe.cs. Do not edit by hand; regenerate with",
    f"//   python3 tests/projects/urp/caps_table.py {sys.argv[1]}",
    "namespace Unbound.Runtime",
    "{",
    "    internal static class GraphicsCapsTable",
    "    {",
    "        /// <summary>By GraphicsFormat value: the GraphicsFormatUsage flags supported.</summary>",
    emit("Usages", "long", table("usages.")),
    "        /// <summary>By GraphicsFormat value: SystemInfo.GetCompatibleFormat for each usage.</summary>",
    emit("CompatibleRender", "int", table("compatible.render.")),
    emit("CompatibleSample", "int", table("compatible.sample.")),
    emit("CompatibleBlend", "int", table("compatible.blend.")),
    emit("CompatibleLoadStore", "int", table("compatible.loadstore.")),
    "    }",
    "}",
    "",
]
open("managed/UnityEngine/Platform/GraphicsCaps.Generated.cs", "w").write("\n".join(out))
