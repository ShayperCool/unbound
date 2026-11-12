"""Reject input captures whose keyboard driver delivered no key-down state."""
import json


def keyboard_input_problem(records):
    for record in records:
        if (record.get('k') == 'event' and record.get('type') == 'STAT'
                and record.get('device') == 'Keyboard' and record.get('keys')):
            return None
    return 'no keyboard key-down state events; the input driver did not deliver keyboard input'


def capture_problem(path):
    with path.open() as stream:
        return keyboard_input_problem(json.loads(line) for line in stream if line.strip())
