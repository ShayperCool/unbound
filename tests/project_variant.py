#!/usr/bin/env python3
"""Stage an isolated authored project with an explicit color-space setting."""
import argparse
from pathlib import Path
from value_probe import assemble, set_color_space


def prepare(source, work, color_space):
    project = assemble(source, work)
    set_color_space(project, color_space)
    return project


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path)
    parser.add_argument('work', type=Path)
    parser.add_argument('color_space', choices=('Gamma', 'Linear'))
    args = parser.parse_args()
    prepare(args.source, args.work, args.color_space)
