#!/usr/bin/env python3
from pathlib import Path
import json
import re
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from playmode_api_probe import compare, main


def global_services_enabled(project):
    path = project/'ProjectSettings/UnityConnectSettings.asset'
    settings = path.read_text().replace('  m_Enabled: 0\n', '  m_Enabled: 1\n', 1)
    settings = re.sub(r'(m_(?:EventOldUrl|EventUrl|ConfigUrl|DashboardUrl):) https?://[^\n]+',
                      r'\1 http://127.0.0.1:9', settings)
    path.write_text(settings)


def compare_disabled_analytics(reference, actual, report):
    verdict = compare(reference, actual, report)
    diagnostics = [line for line in actual.with_name('unbound.log').read_text().splitlines()
                   if 'Analytics with Unity services enabled in project settings' in line]
    result = json.loads(report.read_text())
    result['unexpected_analytics_diagnostics'] = diagnostics
    report.write_text(json.dumps(result, indent=2)+'\n')
    print(f'{len(diagnostics)} unsupported analytics diagnostics')
    return int(bool(verdict or diagnostics))


if __name__ == '__main__':
    enabled = '--global-enabled' in sys.argv
    if enabled:
        sys.argv.remove('--global-enabled')
    sys.exit(main(Path(__file__).resolve().parent, 'EDITORSERVICES_PROBE_OUT',
                  compare_rows=compare_disabled_analytics,
                  configure=global_services_enabled if enabled else None))
