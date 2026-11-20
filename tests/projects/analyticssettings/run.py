#!/usr/bin/env python3
"""Record and compare editor Analytics settings with selected service flags."""
import argparse
from pathlib import Path
import re
import sys
import time

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
import value_probe  # noqa: E402

BASE = [(PROBE.parent/'imguiskin', ('Assets/Scenes', 'Assets/Editor')),
        (PROBE.parent/'analyticssession', ('Packages', 'ProjectSettings'))]
VARIABLE = 'ANALYTICS_SETTINGS_OUT'
PROFILES = {
    'disabled': (),
    'global-only': (('UnityConnectSettings', 'm_Enabled'),),
    'analytics-only': (('UnityAnalyticsSettings', 'm_Enabled'),),
    'package-only': (('UnityAnalyticsSettings', 'm_PackageRequiringCoreStatsPresent'),),
    'crash-only': (('CrashReportingSettings', 'm_EnableCloudDiagnosticsReporting'),),
    'ads-only': (('UnityAdsSettings', 'm_Enabled'),),
    'purchasing-only': (('UnityPurchasingSettings', 'm_Enabled'),),
    'performance-only': (('PerformanceReportingSettings', 'm_Enabled'),),
    'insights-only': (('InsightsSettings', 'm_Enabled'),),
}


def configure(project, profile):
    path = project/'ProjectSettings/UnityConnectSettings.asset'
    settings = re.sub(r'(m_(?:EventOldUrl|EventUrl|ConfigUrl|DashboardUrl):) https?://[^\n]+',
                      r'\1 http://127.0.0.1:9', path.read_text())
    for section, field in PROFILES[profile]:
        indent = '  ' if section == 'UnityConnectSettings' else '    '
        expression = rf'({indent[:-2]}{section}:\n(?:{indent}[^\n]*\n)*?{indent}{field}:) 0\n'
        settings, changed = re.subn(expression, r'\1 1\n', settings)
        if changed != 1:
            raise ValueError(f'cannot select {section}.{field} in UnityConnectSettings')
    path.write_text(settings)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('record', 'publish', 'gate'))
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label', default=f'check-{time.time_ns()}')
    parser.add_argument('--profile', choices=PROFILES)
    args = parser.parse_args()
    if any(Path(label).name != label for label in (args.label, *args.labels)):
        parser.error('labels must be single directory names')
    selected = [args.profile] if args.profile else PROFILES
    codes = []
    for profile in selected:
        def setup(project):
            configure(project, profile)

        label = f'{args.label}-{profile}'
        if args.mode == 'record':
            value_probe.record(PROBE, 'ImguiSkinRunner.Record', VARIABLE, label, batch=True,
                               base=BASE, macos_batch=True, configure=setup)
        elif args.mode == 'publish':
            codes.append(value_probe.publish(PROBE, [f'{name}-{profile}' for name in args.labels], profile=profile))
        else:
            codes.append(value_probe.gate(PROBE, VARIABLE, label, 0, ['--headless', '120'],
                                          base=BASE, configure=setup, profile=profile))
    return max(codes, default=0)


if __name__ == '__main__':
    sys.exit(main())
