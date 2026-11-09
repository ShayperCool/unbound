#!/usr/bin/env python3
"""Stage an isolated authored project with an explicit color-space setting."""
import argparse
from pathlib import Path
import re
from value_probe import assemble


def prepare(source, work, color_space):
    project = assemble(source, work)
    settings = project/'ProjectSettings/ProjectSettings.asset'
    text, count = re.subn(r'(?m)^(\s*m_ActiveColorSpace:) [01]$',
                          r'\g<1> '+str({'Gamma': 0, 'Linear': 1}[color_space]), settings.read_text())
    if count != 1:
        raise ValueError(f'expected one color-space setting, got {count}')
    settings.write_text(text)
    return project


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path)
    parser.add_argument('work', type=Path)
    parser.add_argument('color_space', choices=('Gamma', 'Linear'))
    args = parser.parse_args()
    prepare(args.source, args.work, args.color_space)
