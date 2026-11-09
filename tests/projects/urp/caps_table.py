#!/usr/bin/env python3
"""Writes managed/UnityEngine/Platform/GraphicsCaps.Generated.cs from the Editor's recorded caps
(CapsProbe.Record) on each graphics device: per GraphicsFormat the GraphicsFormatUsage mask the
device supports and its compatible formats for rendering, sampling, blending and random writes.

    python3 tests/projects/urp/caps_table.py \\
        OpenGLCore=tests/projects/urp/Reference/caps-6000.0.71f1.jsonl \\
        Vulkan=tests/projects/urp/Reference/unity-6000.0.71f1/caps-vulkan.jsonl
"""
import json
import sys


def read(path):
    values = {}
    for line in open(path):
        entry = json.loads(line)
        values[entry["key"]] = entry["value"]
    return values


def table(values, prefix, size):
    row = [0] * size
    for key, value in values.items():
        if key.startswith(prefix):
            row[int(key[len(prefix):])] = int(value)
    return row


def emit(name, kind, row):
    lines = []
    for i in range(0, len(row), 16):
        lines.append("                " + ", ".join(str(v) for v in row[i:i + 16]) + ",")
    return f"            {name}: new {kind}[]\n            {{\n" + "\n".join(lines) + "\n            }"


devices = [arg.split("=", 1) for arg in sys.argv[1:]]
recorded = [(name, read(path)) for name, path in devices]
size = 1 + max(int(k.split(".")[1]) for _, values in recorded for k in values if k.startswith("usages."))

out = [
    "// Generated from the Unity Editor's graphics device answers on Linux, recorded by",
    "// tests/projects/urp/Assets/Editor/CapsProbe.cs. Do not edit by hand; regenerate with",
    "//   python3 tests/projects/urp/caps_table.py " + " ".join(f"{n}={p}" for n, p in devices),
    "namespace Unbound.Runtime",
    "{",
    "    internal static class GraphicsCapsTable",
    "    {",
    "        /// <summary>One device's format table, by GraphicsFormat value: the GraphicsFormatUsage",
    "        /// flags supported, and SystemInfo.GetCompatibleFormat for each usage.</summary>",
    "        internal sealed class Device",
    "        {",
    "            internal readonly long[] Usages;",
    "            internal readonly int[] CompatibleRender, CompatibleSample, CompatibleBlend, CompatibleLoadStore;",
    "",
    "            internal Device(long[] usages, int[] compatibleRender, int[] compatibleSample, int[] compatibleBlend, int[] compatibleLoadStore)",
    "            {",
    "                Usages = usages;",
    "                CompatibleRender = compatibleRender;",
    "                CompatibleSample = compatibleSample;",
    "                CompatibleBlend = compatibleBlend;",
    "                CompatibleLoadStore = compatibleLoadStore;",
    "            }",
    "        }",
]
for name, values in recorded:
    out += [
        "",
        f"        /// <summary>The Editor's {values['graphicsDeviceType']} device.</summary>",
        f"        internal static readonly Device {name} = new Device(",
        ",\n".join([
            emit("usages", "long", table(values, "usages.", size)),
            emit("compatibleRender", "int", table(values, "compatible.render.", size)),
            emit("compatibleSample", "int", table(values, "compatible.sample.", size)),
            emit("compatibleBlend", "int", table(values, "compatible.blend.", size)),
            emit("compatibleLoadStore", "int", table(values, "compatible.loadstore.", size)),
        ]) + ");",
    ]
out += ["    }", "}", ""]
open("managed/UnityEngine/Platform/GraphicsCaps.Generated.cs", "w").write("\n".join(out))
