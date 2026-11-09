"""Read-only binary probe; exercises CDirWatcher only in this child process.
Linux x86-64 probe for the inspected shipped tier0 ABI, not a production binding.
The 256-byte aligned opaque storage exceeds its inspected 0x58-byte footprint.
"""
import ctypes as C
import json
import os
from pathlib import Path
import select
import sys
import tempfile
import time

lib = C.CDLL(str(Path(sys.argv[1]).resolve()))
def function(symbol, result, *arguments):
    f = getattr(lib, symbol)
    f.restype, f.argtypes = result, list(arguments)
    return f
ctor = function('_ZN11CDirWatcherC1Ev', None, C.c_void_p)
start = function('_ZN11CDirWatcher13SetDirToWatchEPKcjb', C.c_bool, C.c_void_p, C.c_char_p, C.c_uint, C.c_bool)
changed = function('_ZN11CDirWatcher14GetChangedFileEP10CUtlStringb', C.c_bool, C.c_void_p, C.c_void_p, C.c_bool)
feed = function('_ZN11CDirWatcher19AddFileToChangeListEPKc', None, C.c_void_p, C.c_char_p)
stop = function('_ZN11CDirWatcher4StopEv', None, C.c_void_p)
purge = function('_ZN10CUtlString5PurgeEv', None, C.c_void_p)
libc = C.CDLL(None, use_errno=True)
libc.inotify_init1.argtypes, libc.inotify_init1.restype = [C.c_int], C.c_int
libc.inotify_add_watch.argtypes, libc.inotify_add_watch.restype = [C.c_int, C.c_char_p, C.c_uint], C.c_int
watcher = (C.c_uint64 * 32)()
output = (C.c_uint64 * 4)()
ctor(watcher)
fd = libc.inotify_init1(os.O_NONBLOCK | os.O_CLOEXEC)
assert fd >= 0
result = {'library': str(Path(sys.argv[1]).resolve()), 'events': []}
try:
    with tempfile.TemporaryDirectory(prefix='sbox-cdirwatcher-') as folder:
        result['set_dir_returned'] = start(watcher, os.fsencode(folder), 0x1F, True)
        assert libc.inotify_add_watch(fd, os.fsencode(folder), 0xFFF) >= 0
        path = Path(folder) / 'minimal.scene_c'
        for operation in ['create', 'modify', 'rename', 'delete']:
            if operation == 'create': path.write_bytes(b'first')
            elif operation == 'modify': path.write_bytes(b'second')
            elif operation == 'rename':
                destination = path.with_name('renamed.scene_c')
                path.rename(destination)
                path = destination
            else: path.unlink()
            deadline = time.monotonic() + .3
            seen = False
            while time.monotonic() < deadline:
                seen = changed(watcher, output, False) or seen
                time.sleep(.01)
            ready = bool(select.select([fd], [], [], 0)[0])
            raw = os.read(fd, 65536) if ready else b''
            result['events'].append({'operation': operation, 'native_reported': seen, 'kernel_event_bytes': len(raw)})
        feed(watcher, b'manually-fed.scene_c')
        result['manual_feed_reported'] = changed(watcher, output, False)
        result['manual_feed_path'] = C.cast(output, C.POINTER(C.c_char_p))[0].decode() if result['manual_feed_reported'] else None
finally:
    os.close(fd)
    stop(watcher)
    purge(output)
    purge(watcher)
print(json.dumps(result, indent=2))
assert result['set_dir_returned'] and result['manual_feed_reported']
assert all(not row['native_reported'] and row['kernel_event_bytes'] > 0 for row in result['events'])
