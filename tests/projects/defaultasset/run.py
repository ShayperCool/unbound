#!/usr/bin/env python3
"""Record and compare UnityEditor.DefaultAsset loading and query behavior in Unity 6000.0."""
import argparse
from pathlib import Path
import shutil
import sys
import time

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
import value_probe  # noqa: E402

BASE = [(PROBE.parent/'imguiskin', ('Assets/Scenes', 'Assets/Editor')),
        (PROBE.parent/'analyticssession', ('Packages', 'ProjectSettings')),
        (PROBE.parent/'spriteatlaseditor', ('Assets/Sprites',))]
VARIABLE = 'DEFAULT_ASSET_OUT'

STREAMING_IMG_META = (
    "fileFormatVersion: 2\n"
    "guid: da635000000000000000000000000031\n"
    "DefaultImporter:\n"
    "  externalObjects: {}\n"
    "  userData: \n"
    "  assetBundleName: \n"
    "  assetBundleVariant: \n"
)


def configure(project):
    streaming_img = project/'Assets/StreamingAssets/img.png'
    shutil.copy2(PROBE.parent/'spriteatlaseditor/Assets/Sprites/Single.png', streaming_img)
    (project/'Assets/StreamingAssets/img.png.meta').write_text(STREAMING_IMG_META)
    plugin_src = PROBE.parent/'compilesetup/Assets/Plugins/Same'
    shutil.copy2(plugin_src/'ProbeSame.dll', project/'Assets/Data/ProbeSame.dll')
    shutil.copy2(plugin_src/'ProbeSame.dll.meta', project/'Assets/Data/ProbeSame.dll.meta')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('record', 'publish', 'gate'))
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label', default=f'check-{time.time_ns()}')
    args = parser.parse_args()
    if any(Path(label).name != label for label in (args.label, *args.labels)):
        parser.error('labels must be single directory names')
    if args.mode == 'record':
        value_probe.record(PROBE, 'ImguiSkinRunner.Record', VARIABLE, args.label, batch=True,
                           base=BASE, macos_batch=True, configure=configure)
        return 0
    if args.mode == 'publish':
        return value_probe.publish(PROBE, args.labels)
    return value_probe.gate(PROBE, VARIABLE, args.label, 0, ['--headless', '120'],
                            base=BASE, configure=configure)


if __name__ == '__main__':
    sys.exit(main())
