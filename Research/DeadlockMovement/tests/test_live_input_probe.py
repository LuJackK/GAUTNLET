import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import live_input_probe as probe


class ProbeTests(unittest.TestCase):
    def exercise(self, stop_on_walk=False, fail_on_walk=False):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            (root / 'manifest.json').write_text(json.dumps({'transport_status': 'running'}))
            sent = []
            ticks = [0]
            now = [0.0]
            def clock():
                now[0] += .1
                return now[0]
            def frame(_):
                ticks[0] += 1
                return dict(world=1, slot=0, pawn_handle=7, session_generation=1,
                            hero_id=63, grounded=True, velocity=[0,0,0],
                            stamina={'current':2}, clock_tick=ticks[0])
            class FakeConsole:
                def __init__(self, **kwargs):
                    self._socket = self
                def __enter__(self): return self
                def __exit__(self, *args): pass
                def recv(self, count): return b''
                def send_command(self, command):
                    sent.append(command)
                    if 'PROBE_PHASE_walk;' in command:
                        if stop_on_walk:
                            (root / 'input-probe.stop').touch()
                        if fail_on_walk:
                            raise RuntimeError('send failed')
                    return {'command':command, 'execution_acknowledged':False}
            with patch.object(probe, 'latest_frame', frame), patch.object(probe, 'ConsoleClient', FakeConsole), \
                    patch.object(probe.time, 'monotonic', clock), patch.object(probe.time, 'sleep', lambda _: None):
                if fail_on_walk:
                    with self.assertRaisesRegex(RuntimeError, 'send failed'):
                        probe.run_probe(root, root/'out.jsonl', 7, 63)
                else:
                    probe.run_probe(root, root/'out.jsonl', 7, 63)
            records = [json.loads(line) for line in (root/'out.jsonl').read_text().splitlines()]
            return sent, records

    def test_normal_sequence_always_releases_after_deliberate_stop(self):
        sent, records = self.exercise()
        self.assertTrue(sent[-1].endswith(probe.RELEASE))
        self.assertTrue(any('deliberate stop' in r.get('reason','') for r in records))

    def test_external_stop_prevents_later_jump_and_releases(self):
        sent, records = self.exercise(stop_on_walk=True)
        self.assertFalse(any('PROBE_PHASE_jump;' in c for c in sent))
        self.assertTrue(sent[-1].endswith(probe.RELEASE))
        self.assertTrue(any(r.get('reason') == 'stop file requested' for r in records))

    def test_exception_also_attempts_release(self):
        sent, _ = self.exercise(fail_on_walk=True)
        self.assertTrue(sent[-1].endswith(probe.RELEASE))


if __name__ == '__main__': unittest.main()
