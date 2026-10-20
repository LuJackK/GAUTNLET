"""Experimental, synchronous loopback VConsole transport for explicit probes.

Framing references (independent implementations, not Valve specifications):
https://github.com/oxijoined/vconsole-python/blob/master/vconsole_client.py
https://github.com/theokyr/CS2RemoteConsole/tree/master/libvconsole

The 12-byte big-endian header carries type, discriminator, total length and pipe
handle. CMND uses discriminator 0x00D40000 and a NUL-terminated UTF-8 body.
PRNT text is tentatively decoded after its 28-byte metadata prefix; every raw
body is retained. A successful send is no execution acknowledgement. Wall-clock
transport timestamps provide no engine-tick or command-timing guarantee.
"""

import ipaddress
import math
import socket
import struct
import time


HEADER = struct.Struct('>4sIHH')
CMND_VERSION = 0x00D40000
MAX_COMMAND_BYTES = 4096


class ConsoleError(ValueError):
    """Invalid transport request, broken connection, or malformed wire frame."""


def _duration(value, name):
    if type(value) not in (int, float) or not 0 < value <= 10 or not math.isfinite(value):
        raise ConsoleError(name + ' must be a finite duration in (0, 10] seconds')


def command_packet(command):
    if not isinstance(command, str) or not command.strip():
        raise ConsoleError('command must be a nonempty string')
    if any(character in command for character in ('\0', '\r', '\n')):
        raise ConsoleError('command must contain one console line without NUL')
    try:
        encoded = command.encode('utf-8')
    except UnicodeEncodeError as error:
        raise ConsoleError('command is not valid UTF-8') from error
    if len(encoded) > MAX_COMMAND_BYTES:
        raise ConsoleError('command exceeds byte limit')
    body = encoded + b'\0'
    return HEADER.pack(b'CMND', CMND_VERSION, HEADER.size + len(body), 0) + body


def _packet(header, body):
    kind, version, length, handle = HEADER.unpack(header)
    result = {'type': kind.decode('ascii', errors='replace'), 'version': version,
              'length': length, 'handle': handle, 'payload_hex': body.hex(),
              'text': None, 'received_monotonic_ns': time.monotonic_ns()}
    if kind == b'PRNT':
        if len(body) < 28:
            result['decode_error'] = 'PRNT metadata shorter than 28 bytes'
        else:
            result['channel_id'] = struct.unpack('>i', body[:4])[0]
            result['text'] = body[28:].split(b'\0', 1)[0].decode('utf-8', errors='replace')
            result['text_status'] = 'reference_layout_unverified'
    return result


class ConsoleClient:
    """A bounded, single-threaded client; the caller owns command selection.

    Only numeric loopback IPs are accepted; DNS is never consulted. Calls to
    read_packets stop at duration, the idle timeout, EOF, or the packet bound.
    Incomplete frames survive an ordinary read timeout for the next read call.
    """

    def __init__(self, host='127.0.0.1', port=29000, timeout=2.0, max_packet_bytes=65535):
        try:
            address = ipaddress.ip_address(host)
        except (ValueError, TypeError) as error:
            raise ConsoleError('host must be a numeric loopback IP address') from error
        if not address.is_loopback:
            raise ConsoleError('only loopback connections are supported')
        if type(port) is not int or not 1 <= port <= 65535:
            raise ConsoleError('port must be an integer in [1, 65535]')
        _duration(timeout, 'timeout')
        if type(max_packet_bytes) is not int or not HEADER.size <= max_packet_bytes <= 65535:
            raise ConsoleError('max_packet_bytes must be an integer in [12, 65535]')
        self.host = str(address)
        self.port = port
        self.timeout = timeout
        self.max_packet_bytes = max_packet_bytes
        self.family = socket.AF_INET6 if address.version == 6 else socket.AF_INET
        self._socket = None
        self._buffer = bytearray()
        self.last_read_status = 'not_started'

    @property
    def connected(self):
        return self._socket is not None

    def connect(self):
        if self.connected:
            raise ConsoleError('already connected')
        connection = socket.socket(self.family, socket.SOCK_STREAM)
        try:
            connection.settimeout(self.timeout)
            connection.connect((self.host, self.port))
        except OSError as error:
            connection.close()
            raise ConsoleError(f'loopback connect failed: {error}') from error
        self._socket = connection
        self._buffer.clear()
        return self

    def send_command(self, command):
        packet = command_packet(command)
        if len(packet) > self.max_packet_bytes:
            raise ConsoleError('command packet exceeds configured packet limit')
        if not self.connected:
            raise ConsoleError('not connected')
        try:
            self._socket.settimeout(self.timeout)
            self._socket.sendall(packet)
        except OSError as error:
            self.close()
            raise ConsoleError(f'command send failed; partial delivery is possible: {error}') from error
        return {'command': command, 'bytes_sent': len(packet),
                'sent_monotonic_ns': time.monotonic_ns(), 'execution_acknowledged': False}

    def read_packets(self, duration=1.0, max_packets=256):
        _duration(duration, 'duration')
        if type(max_packets) is not int or not 1 <= max_packets <= 4096:
            raise ConsoleError('max_packets must be an integer in [1, 4096]')
        if not self.connected:
            raise ConsoleError('not connected')
        deadline = time.monotonic() + duration
        packets = []
        self.last_read_status = 'duration_elapsed'
        while len(packets) < max_packets:
            if len(self._buffer) >= HEADER.size:
                length = HEADER.unpack(self._buffer[:HEADER.size])[2]
                if not HEADER.size <= length <= self.max_packet_bytes:
                    self.close()
                    raise ConsoleError('invalid or oversized VConsole frame length: ' + str(length))
                if len(self._buffer) >= length:
                    header = bytes(self._buffer[:HEADER.size])
                    body = bytes(self._buffer[HEADER.size:length])
                    del self._buffer[:length]
                    packets.append(_packet(header, body))
                    if time.monotonic() >= deadline:
                        break
                    continue
            remaining = deadline - time.monotonic()
            if remaining <= 0:
                break
            try:
                self._socket.settimeout(min(self.timeout, remaining))
                # Bound buffered data even if a peer streams an unfinished frame.
                block = self._socket.recv(min(8192, self.max_packet_bytes - len(self._buffer)))
            except socket.timeout:
                self.last_read_status = 'timeout'
                break
            except OSError as error:
                self.close()
                raise ConsoleError(f'console read failed: {error}') from error
            if not block:
                truncated = bool(self._buffer)
                self.close()
                self.last_read_status = 'peer_closed'
                if truncated:
                    raise ConsoleError('peer disconnected during an incomplete VConsole frame')
                break
            self._buffer.extend(block)
        if len(packets) == max_packets:
            self.last_read_status = 'packet_limit'
        return packets

    def close(self):
        connection, self._socket = self._socket, None
        self._buffer.clear()
        if connection is not None:
            try:
                connection.close()
            except OSError:
                pass

    def __enter__(self):
        return self.connect()

    def __exit__(self, *_):
        self.close()
