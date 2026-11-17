#!/usr/bin/env python3
"""AnalyticsSessionInfo and RemoteSettings in an Editor with Unity services disabled; see README.md."""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import value_probe  # noqa: E402

if __name__ == '__main__':
    sys.exit(value_probe.main(Path(__file__).resolve().parent, 'AnalyticsSessionRunner.Record', 'ANALYTICS_SESSION_OUT',
                              tolerance=0, engine_args=['--headless', '120'], macos_batch=True))
