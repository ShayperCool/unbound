#!/usr/bin/env python3
"""Compares two NUnit result files of the Test Framework (Unity's `-runTests -testResults`, Unbound's
`unbound test --results`): the test tree, each test's result, label, failure message and output,
and the counts. Times, ids, seeds, process ids, paths and stack traces differ between runs and are
not compared.

    python3 tests/projects/testrunner/compare.py tests/projects/testrunner/Reference/editmode-6000.0.71f1.xml /tmp/editmode.xml
"""
import re
import sys
import xml.etree.ElementTree as ET

COUNTS = ("total", "passed", "failed", "inconclusive", "skipped", "testcasecount")
ATTRIBUTES = ("type", "name", "fullname", "classname", "methodname", "runstate", "result", "label", "site") + COUNTS


def text(node, path):
    found = node.find(path)
    if found is None or found.text is None:
        return None
    # The results file is wherever the run was told to write it.
    return re.sub(r"Saving results to: .*", "Saving results to: <file>", found.text.strip())


def describe(node, depth=0):
    """(path, attributes, message, output) for the node and its descendant suites and cases."""
    rows = []
    attrs = {k: node.get(k) for k in ATTRIBUTES if node.get(k) is not None}
    if attrs.get("type") == "Assembly":
        attrs.pop("fullname", None)  # the assembly's file path
    name = attrs.get("fullname") or attrs.get("name") or node.tag
    rows.append((depth, node.tag, name, attrs, text(node, "failure/message"), text(node, "reason/message"), text(node, "output")))
    for child in node:
        if child.tag in ("test-suite", "test-case"):
            rows.extend(describe(child, depth + 1))
    return rows


def main():
    want = describe(ET.parse(sys.argv[1]).getroot())
    got = describe(ET.parse(sys.argv[2]).getroot())
    differences = 0
    got_by_key = {(r[1], r[2]): r for r in got}
    for row in want:
        key = (row[1], row[2])
        other = got_by_key.pop(key, None)
        if other is None:
            print(f"MISSING {row[1]} {row[2]}")
            differences += 1
            continue
        for label, a, b in (("attributes", row[3], other[3]), ("failure", row[4], other[4]), ("reason", row[5], other[5]), ("output", row[6], other[6])):
            if a != b:
                differences += 1
                print(f"DIFF {row[1]} {row[2]} {label}\n  unity:  {a!r}\n  unbound: {b!r}")
    for key in got_by_key:
        print(f"EXTRA {key[0]} {key[1]}")
        differences += 1
    print(f"{len(want)} nodes compared, {differences} differences")
    sys.exit(1 if differences else 0)


if __name__ == "__main__":
    main()
