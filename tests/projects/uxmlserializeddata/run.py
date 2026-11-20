#!/usr/bin/env python3
"""Record and compare Unity 6 UxmlSerializedData, ShouldWriteAttributeValue and [UxmlElement]/[UxmlAttribute] serialized selection."""
from pathlib import Path
import sys

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
import value_probe  # noqa: E402

BASE = [(PROBE.parent/'uxmlattributes', ('Assets/Scenes', 'Packages', 'ProjectSettings'))]

if __name__ == '__main__':
    sys.exit(value_probe.main(PROBE, 'UxmlSerializedDataRunner.Record', 'UXML_SERIALIZED_DATA_OUT', 0, ['--headless', '120'],
                              batch=True, base=BASE, macos_batch=True))
