#!/usr/bin/env python3
"""A user's live processes on macOS, for the janitor and runtime snapshot cleanup.

Linux reads /proc in those tools. macOS has no /proc: `lsof` lists each process's working
directory, executable, mapped and open files, `sysctl(KERN_PROCARGS2)` its arguments and
environment, and `sysctl(KERN_PROC_PID)` its start time. Both readers fail closed: a live
process of the user that cannot be read is an error, while one that exits during the reading is
skipped.
"""
from dataclasses import dataclass, field
import ctypes
import ctypes.util
import errno
import os
from pathlib import Path
import struct
import subprocess
import sys

MACOS = sys.platform == 'darwin'
# sysctl(3) names: the argument area's size limit and a process's arguments with its environment.
CTL_KERN = 1
KERN_ARGMAX = 8
KERN_PROCARGS2 = 49
KERN_PROC = 14
KERN_PROC_PID = 1
# sizeof(struct kinfo_proc) on 64-bit macOS; its first member, extern_proc, starts with the
# process's start time (struct timeval: 64-bit seconds, 32-bit microseconds).
KINFO_PROC_SIZE = 648
# Seconds for one `lsof` or `ps` call (a full listing takes well under a second on the Mac).
COMMAND_SECONDS = 60


@dataclass
class Process:
    pid: int
    cwd: Path = None
    executable: Path = None
    # Open and mapped files (the executable's text and libraries among them).
    files: list = field(default_factory=list)
    arguments: list = field(default_factory=list)
    environment: list = field(default_factory=list)


def alive(pid):
    """Whether the process still exists and is not a zombie (which holds no files)."""
    try:
        os.kill(pid, 0)
    except ProcessLookupError:
        return False
    except PermissionError:
        return True
    state = subprocess.run(['ps', '-o', 'stat=', '-p', str(pid)], capture_output=True, text=True,
                           timeout=COMMAND_SECONDS).stdout.strip()
    return bool(state) and not state.startswith('Z')


def user_pids(uid):
    """The processes running as `uid` (their effective user, as /proc's owner on Linux: a setuid
    program the user started runs as another and is not the user's to read)."""
    result = subprocess.run(['ps', '-ax', '-o', 'pid=,uid='], capture_output=True, text=True,
                            timeout=COMMAND_SECONDS)
    if result.returncode:
        raise OSError(f'ps failed: {result.stderr.strip()}')
    pids = []
    for line in result.stdout.splitlines():
        fields = line.split()
        if len(fields) == 2 and int(fields[1]) == uid:
            pids.append(int(fields[0]))
    return pids


def parse_lsof(output):
    """`lsof -F pfn` records: a `p<pid>` line, then `f<descriptor>` and `n<name>` lines per file."""
    found, process, descriptor = {}, None, None
    for line in output.splitlines():
        tag, value = line[:1], line[1:]
        if tag == 'p':
            process = found.setdefault(int(value), Process(int(value)))
            descriptor = None
        elif tag == 'f':
            descriptor = value
        elif tag == 'n' and process is not None and value.startswith('/'):
            path = Path(value)
            if descriptor == 'cwd':
                process.cwd = path
            elif descriptor == 'txt' and process.executable is None:
                process.executable = path
            process.files.append(path)
    return found


def parse_procargs(raw):
    """KERN_PROCARGS2: argc, the executable path, padding, argc arguments, then the environment."""
    if len(raw) < 4:
        raise ValueError('short argument area')
    count = struct.unpack_from('i', raw)[0]
    _, _, rest = raw[4:].partition(b'\0')
    parts = rest.lstrip(b'\0').split(b'\0')
    return parts[:count], [part for part in parts[count:] if part]


_libc = None


def _sysctl(names, size):
    global _libc
    if _libc is None:
        _libc = ctypes.CDLL(ctypes.util.find_library('c'), use_errno=True)
    mib = (ctypes.c_int*len(names))(*names)
    buffer = ctypes.create_string_buffer(size)
    length = ctypes.c_size_t(size)
    if _libc.sysctl(mib, len(names), buffer, ctypes.byref(length), None, 0):
        code = ctypes.get_errno()
        raise OSError(code, os.strerror(code))
    return buffer.raw[:length.value]


def procargs(pid):
    limit = struct.unpack('i', _sysctl([CTL_KERN, KERN_ARGMAX], 4))[0]
    return parse_procargs(_sysctl([CTL_KERN, KERN_PROCARGS2, pid], limit))


def darwin_processes(uid, exclude=()):
    """Every live process of `uid` but `exclude`, with its files, arguments and environment."""
    pids = [pid for pid in user_pids(uid) if pid not in exclude]
    result = subprocess.run(['lsof', '-n', '-P', '-w', '-F', 'pfn', '-u', str(uid)], capture_output=True,
                            text=True, timeout=COMMAND_SECONDS)
    # lsof exits 1 when some listed process vanished meanwhile; its other records stand.
    if result.returncode not in (0, 1):
        raise OSError(f'lsof failed: {result.stderr.strip()}')
    listed = parse_lsof(result.stdout)
    found = []
    for pid in pids:
        process = listed.get(pid)
        try:
            if process is None:
                raise OSError(errno.ESRCH, 'not listed by lsof')
            process.arguments, process.environment = procargs(pid)
        except (OSError, ValueError) as error:
            if not alive(pid):
                continue
            raise OSError(f'cannot inspect live process {pid}: {error}') from error
        found.append(process)
    return found


def start_time(pid):
    """The process's start time in microseconds since the epoch (as text, kinfo_proc's
    p_starttime: independent of locale and time zone), or None once it is gone; with the pid, its
    identity."""
    if not isinstance(pid, int) or pid <= 0:
        return None
    # A pid no process has gives an empty answer; any other failure is an error (fail closed).
    info = _sysctl([CTL_KERN, KERN_PROC, KERN_PROC_PID, pid], KINFO_PROC_SIZE)
    if not info:
        return None
    if len(info) != KINFO_PROC_SIZE:
        raise OSError(f'kinfo_proc of {len(info)} bytes, expected {KINFO_PROC_SIZE}')
    seconds, microseconds = struct.unpack_from('qi', info)
    return str(seconds*1_000_000+microseconds)
