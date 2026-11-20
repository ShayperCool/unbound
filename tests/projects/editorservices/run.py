#!/usr/bin/env python3
from pathlib import Path
import json
import re
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from playmode_api_probe import compare, main


def configure_services(project, global_enabled, legacy_enabled, deferred_startup=False):
    path = project/'ProjectSettings/UnityConnectSettings.asset'
    settings = path.read_text()
    if global_enabled:
        settings = settings.replace('  m_Enabled: 0\n', '  m_Enabled: 1\n', 1)
    if legacy_enabled:
        settings = settings.replace('  UnityAnalyticsSettings:\n    m_Enabled: 0\n',
                                    '  UnityAnalyticsSettings:\n    m_Enabled: 1\n', 1)
    if deferred_startup:
        settings, changed = re.subn(r'(  UnityAnalyticsSettings:\n(?:    [^\n]*\n)*?    m_InitializeOnStartup:) 1',
                                    r'\1 0', settings)
        if changed != 1:
            raise ValueError('cannot defer legacy Analytics startup in UnityConnectSettings')
    settings = re.sub(r'(m_(?:EventOldUrl|EventUrl|ConfigUrl|DashboardUrl):) https?://[^\n]+',
                      r'\1 http://127.0.0.1:9', settings)
    path.write_text(settings)


def compare_disconnected_analytics(reference, actual, report):
    verdict = compare(reference, actual, report)
    diagnostics = [line for line in actual.with_name('unbound.log').read_text().splitlines()
                   if 'Analytics with Unity services enabled in project settings' in line]
    result = json.loads(report.read_text())
    result['unexpected_analytics_diagnostics'] = diagnostics
    report.write_text(json.dumps(result, indent=2)+'\n')
    print(f'{len(diagnostics)} unsupported analytics diagnostics')
    return int(bool(verdict or diagnostics))


if __name__ == '__main__':
    global_enabled = '--global-enabled' in sys.argv
    legacy_enabled = '--legacy-enabled' in sys.argv
    deferred_startup = '--deferred-startup' in sys.argv
    if deferred_startup and not legacy_enabled:
        sys.exit('--deferred-startup requires --legacy-enabled')
    for option in ('--global-enabled', '--legacy-enabled', '--deferred-startup'):
        if option in sys.argv:
            sys.argv.remove(option)

    def configure(project):
        configure_services(project, global_enabled, legacy_enabled, deferred_startup)

    sys.exit(main(Path(__file__).resolve().parent, 'EDITORSERVICES_PROBE_OUT',
                  compare_rows=compare_disconnected_analytics,
                  configure=configure if global_enabled or legacy_enabled else None,
                  profile='legacy-enabled' if legacy_enabled else None))
