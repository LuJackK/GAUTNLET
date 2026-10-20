from pathlib import Path
import socket
import struct
import sys
import unittest
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import live_console as console


def frame(kind=b'PRNT', body=None):
    if body is None:
        body = struct.pack('>i', 9) + bytes(24) + b'probe hello\n\0'
    return struct.pack('>4sIHH', kind, 123, len(body) + 12, 5) + body


class FakeSocket:
    def __init__(self, chunks=()):
        self.chunks = list(chunks)
        self.sent = []
        self.closed = False
        self.timeouts = []

    def settimeout(self, value):
        self.timeouts.append(value)

    def connect(self, address):
        self.address = address

    def sendall(self, payload):
        self.sent.append(payload)

    def recv(self, count):
        if not self.chunks:
            raise socket.timeout()
        value = self.chunks.pop(0)
        if isinstance(value, Exception):
            raise value
        if len(value) > count:
            self.chunks.insert(0, value[count:])
        return value[:count]

    def close(self):
        self.closed = True


class LiveConsoleTests(unittest.TestCase):
    def client(self, chunks=(), **kwargs):
        transport = FakeSocket(chunks)
        patch = mock.patch.object(console.socket, 'socket', return_value=transport)
        patch.start()
        self.addCleanup(patch.stop)
        client = console.ConsoleClient(**kwargs).connect()
        self.addCleanup(client.close)
        return client, transport

    def test_command_framing_matches_independent_wire_layout(self):
        packet = console.command_packet('echo probe')
        self.assertEqual(packet[:12], b'CMND\x00\xd4\x00\x00\x00\x17\x00\x00')
        self.assertEqual(packet[12:], b'echo probe\0')
        self.assertEqual(len(packet), 23)
        self.assertEqual(console.command_packet('echo \u010d')[12:], 'echo \u010d\0'.encode())
        for command in ('', '  ', 'echo\0x', 'echo\nx', 'echo\rx', 'x' * 4097, '\ud800', None):
            with self.subTest(command=repr(command)[:30]), self.assertRaises(console.ConsoleError):
                console.command_packet(command)

    def test_only_loopback_literals_and_bounded_types(self):
        for host in ('example.com', 'localhost', '192.168.1.2', '0.0.0.0', '::', True):
            with self.subTest(host=host), self.assertRaises(console.ConsoleError):
                console.ConsoleClient(host=host)
        self.assertEqual(console.ConsoleClient(host='::1').family, socket.AF_INET6)
        for key, value in (('timeout', True), ('timeout', float('nan')), ('timeout', 11),
                           ('port', True), ('port', 0), ('max_packet_bytes', 11),
                           ('max_packet_bytes', 65536)):
            with self.subTest(key=key, value=value), self.assertRaises(console.ConsoleError):
                console.ConsoleClient(**{key: value})

    def test_send_records_transport_without_execution_acknowledgement(self):
        client, transport = self.client()
        report = client.send_command('echo probe')
        self.assertEqual(transport.address, ('127.0.0.1', 29000))
        self.assertEqual(transport.sent, [console.command_packet('echo probe')])
        self.assertFalse(report['execution_acknowledged'])
        self.assertEqual(report['bytes_sent'], 23)
        self.assertIsInstance(report['sent_monotonic_ns'], int)
        client.close()
        client.close()
        self.assertTrue(transport.closed)
        with self.assertRaisesRegex(console.ConsoleError, 'not connected'):
            client.send_command('echo test')

    def test_fragmented_headers_and_payloads_preserve_print_and_unknown_raw_data(self):
        payload = frame() + frame(b'AINF', b'\x00\xfe\x01')
        client, _ = self.client([payload[index:index + 1] for index in range(len(payload))])
        packets = client.read_packets(max_packets=2)
        self.assertEqual(len(packets), 2)
        self.assertEqual(packets[0]['text'], 'probe hello\n')
        self.assertEqual(packets[0]['channel_id'], 9)
        self.assertEqual(packets[0]['handle'], 5)
        self.assertEqual(packets[0]['version'], 123)
        self.assertEqual(packets[1]['payload_hex'], '00fe01')
        self.assertIsNone(packets[1]['text'])
        self.assertEqual(client.last_read_status, 'packet_limit')

    def test_timeout_retains_partial_frame_and_never_claims_acknowledgement(self):
        payload = frame()
        client, transport = self.client([payload[:7], socket.timeout(), payload[7:]])
        self.assertEqual(client.read_packets(duration=0.25), [])
        self.assertEqual(client.last_read_status, 'timeout')
        self.assertTrue(client.connected)
        self.assertLessEqual(transport.timeouts[-1], 0.25)
        self.assertEqual(client.read_packets(max_packets=1)[0]['text'], 'probe hello\n')

    def test_clean_disconnect_and_truncated_disconnect_are_distinct(self):
        client, transport = self.client([frame(), b''])
        self.assertEqual(len(client.read_packets()), 1)
        self.assertFalse(client.connected)
        self.assertEqual(client.last_read_status, 'peer_closed')
        self.assertTrue(transport.closed)
        for truncated in (frame()[:3], frame()[:-1]):
            client, transport = self.client([truncated, b''])
            with self.assertRaisesRegex(console.ConsoleError, 'incomplete'):
                client.read_packets()
            self.assertFalse(client.connected)

    def test_bad_lengths_close_and_short_print_keeps_raw_bytes(self):
        for length, limit in ((0, 65535), (11, 65535), (65, 64)):
            malformed = struct.pack('>4sIHH', b'PRNT', 0, length, 0)
            client, transport = self.client([malformed], max_packet_bytes=limit)
            with self.assertRaisesRegex(console.ConsoleError, 'frame length'):
                client.read_packets()
            self.assertTrue(transport.closed)
        client, _ = self.client([frame(body=b'bad')])
        record = client.read_packets(max_packets=1)[0]
        self.assertEqual(record['payload_hex'], b'bad'.hex())
        self.assertIn('decode_error', record)

    def test_connect_and_send_failure_cleanup_and_read_limits(self):
        fake = FakeSocket()
        fake.connect = mock.Mock(side_effect=socket.timeout())
        with mock.patch.object(console.socket, 'socket', return_value=fake):
            with self.assertRaisesRegex(console.ConsoleError, 'connect failed'):
                console.ConsoleClient().connect()
        self.assertTrue(fake.closed)
        client, transport = self.client()
        transport.sendall = mock.Mock(side_effect=socket.timeout())
        with self.assertRaisesRegex(console.ConsoleError, 'partial delivery'):
            client.send_command('echo x')
        self.assertFalse(client.connected)
        client, _ = self.client()
        for options in ({'duration': 0}, {'max_packets': 0}, {'max_packets': True}, {'max_packets': 4097}):
            with self.subTest(options=options), self.assertRaises(console.ConsoleError):
                client.read_packets(**options)


if __name__ == '__main__':
    unittest.main()
