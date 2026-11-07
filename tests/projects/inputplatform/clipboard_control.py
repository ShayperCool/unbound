#!/usr/bin/env python3
"""External X11 clipboard owner/reader for the Linux Editor probe."""
import subprocess
import sys
mode = sys.argv[1]
if mode == 'focus':
    pid = int(sys.stdin.read())
    windows = subprocess.check_output(['xdotool', 'search', '--pid', str(pid)], text=True).split()
    subprocess.run(['xdotool', 'windowfocus', '--sync', windows[-1]], check=True)
elif mode == 'display':
    sys.stdout.write(subprocess.check_output(['xrandr', '--verbose'], text=True))
elif mode == 'get':
    result = subprocess.run(['xclip', '-selection', 'clipboard', '-o'], capture_output=True, timeout=8)
    if result.returncode == 0:
        sys.stdout.buffer.write(result.stdout)
elif mode == 'clear':
    subprocess.run(['xclip', '-selection', 'clipboard', '-i'], input=b'', stdout=subprocess.DEVNULL, check=True)
else:
    target = 'image/png' if mode == 'nontext' else 'UTF8_STRING'
    value = b'not text' if mode == 'nontext' else sys.stdin.buffer.read()
    subprocess.run(['xclip', '-selection', 'clipboard', '-target', target, '-i'], input=value,
                   stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=True)
