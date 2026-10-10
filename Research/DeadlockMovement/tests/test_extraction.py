"""Optional real-CLI integration test using only synthetic, original data."""
import argparse
import contextlib
import io
import json
from pathlib import Path
import sys
import struct
import tempfile
import unittest
import zlib
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import research


class ExtractionTests(unittest.TestCase):
    def test_archive_export_manifest_and_failures(self):
        try:
            research.cli()
        except ValueError:
            self.skipTest('Optional CLI integration test: run bootstrap.py first')
        research.LOCAL.mkdir(exist_ok=True)
        with tempfile.TemporaryDirectory(dir=research.LOCAL) as temp:
            root = Path(temp)
            source = root / 'source'
            source.mkdir()
            (source / 'measurement.txt').write_text('synthetic research fixture\n', encoding='utf-8')
            archive = root / 'fixture_dir.vpk'
            # Minimal VPK v1: one root-level text entry, entirely in preload data.
            # Format reference: https://developer.valvesoftware.com/wiki/VPK_(file_format)
            payload = (source / 'measurement.txt').read_bytes()
            entry = struct.pack('<IHHIIH', zlib.crc32(payload), len(payload), 0x7fff, 0, 0, 0xffff)
            tree = b'txt\x00 \x00measurement\x00' + entry + payload + b'\x00\x00\x00'
            archive.write_bytes(struct.pack('<III', 0x55aa1234, 1, len(tree)) + tree)
            args = argparse.Namespace(vpk=str(archive), filter='measurement.txt',
                                      run='success', build='synthetic')
            with patch.object(research, 'LOCAL', root), contextlib.redirect_stdout(io.StringIO()):
                research.extract(args)
                manifest = json.loads((root / 'runs/success/manifest.json').read_text())
                self.assertEqual(manifest['status'], 'complete')
                self.assertEqual(len(manifest['sources']), 1)
                self.assertEqual(manifest['exports'][0]['sha256'], research.sha256(source / 'measurement.txt'))
                with self.assertRaises(FileExistsError):
                    research.extract(args)
                args.run = 'empty'
                args.filter = 'nonexistent.txt'
                with self.assertRaises(ValueError):
                    research.extract(args)
                failure = json.loads((root / 'runs/empty/manifest.json').read_text())
                self.assertEqual(failure['status'], 'failed')
                args.run = '../escape'
                with self.assertRaises(ValueError):
                    research.extract(args)


if __name__ == '__main__':
    unittest.main()
