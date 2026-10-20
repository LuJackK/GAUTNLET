from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import static_analysis as sa


SIGNATURES = '''# proto-file: header
#
# File header comment.

signatures {
  id: "ability.think"
  modules: GAME_MODULE_SERVER
  pattern: "48 89 5C 24"
  shape: "void Think(CCitadelBaseAbility*)"
}

# The source line stored at +0x1b changes between builds.
signatures {
  id: "movement.process"
  modules: GAME_MODULE_SERVER
  modules: GAME_MODULE_CLIENT
  pattern: "48 89 5C 24 08"
  shape: "ProcessMovement(movement services, CMoveData*)"
}
'''

SIGCHECK = '''Build 6796
ok    server.dll   ability.think                                rva=0xb649b0
ok    server.dll   movement.process                             rva=0xb2f7b0
ok    client.dll   movement.process                             rva=0x887f40
FAIL  server.dll   pawn.respawn                                 no match
2 of 3 signatures resolved
'''


def schema(*classes):
    return {'build': '6796', 'classes': list(classes), 'enums': []}


BASE = {'name': 'CitadelAbilityVData', 'module': 'server', 'size': 16,
        'fields': [{'name': 'm_flCooldown', 'type': 'float32', 'offset': 8}]}
SLIDE = {'name': 'CAbilitySlideVData', 'module': 'server', 'size': 40,
         'bases': [{'name': 'CitadelAbilityVData', 'module': 'server'}],
         'fields': [{'name': 'm_flFriction', 'type': 'float32', 'offset': 16},
                    {'name': 'm_vDir', 'type': 'Vector', 'offset': 20},
                    {'name': 'm_sound', 'type': 'CSoundEventName', 'offset': 32}]}


class SignatureTests(unittest.TestCase):
    def test_parses_shapes_and_only_adjacent_comments(self):
        parsed = sa.parse_signatures(SIGNATURES)
        self.assertEqual(parsed['ability.think'], {'shape': 'void Think(CCitadelBaseAbility*)', 'note': ''})
        self.assertIn('+0x1b', parsed['movement.process']['note'])

    def test_sigcheck_separates_resolved_and_failed(self):
        resolved, failed = sa.parse_sigcheck(SIGCHECK)
        self.assertEqual(len(resolved), 3)
        self.assertEqual(resolved[1], {'module': 'server.dll', 'id': 'movement.process', 'rva': 0xb2f7b0})
        self.assertEqual([f['id'] for f in failed], ['pawn.respawn'])

    def test_labels_carry_shape_and_movement_params(self):
        labels = sa.build_labels(SIGCHECK, SIGNATURES)
        movement = [l for l in labels['labels'] if l['id'] == 'movement.process']
        self.assertEqual({l['module'] for l in movement}, {'server.dll', 'client.dll'})
        self.assertEqual(movement[0]['name'], 'ml_movement_process')
        self.assertEqual(movement[0]['params'][1], ['mv', 'CMoveData*'])
        self.assertEqual(len(labels['failed']), 1)

    def test_refuses_empty_sigcheck(self):
        with self.assertRaises(sa.AnalysisError):
            sa.build_labels('nothing here', SIGNATURES)


class TypeTests(unittest.TestCase):
    def test_field_layouts(self):
        self.assertEqual(sa.field_layout('float32'), ('float', 1, 4))
        self.assertEqual(sa.field_layout('GameTime_t[4]'), ('float', 4, 16))
        self.assertEqual(sa.field_layout('QAngle'), ('float', 3, 12))
        self.assertEqual(sa.field_layout('CHandle< CBaseEntity >'), ('uint32', 1, 4))
        self.assertEqual(sa.field_layout('CCitadelPlayerBot*'), ('pointer', 1, 8))
        self.assertIsNone(sa.field_layout('CSoundEventName'))

    def test_flattens_bases_and_spans_unknown_types(self):
        result = sa.build_types(schema(BASE, SLIDE), [r'CAbilitySlideVData'], r'CAbility\w+VData')
        slide = result['structs'][0]
        self.assertEqual([f['name'] for f in slide['fields']], ['m_flCooldown', 'm_flFriction', 'm_vDir', 'm_sound'])
        self.assertEqual(slide['fields'][0]['declared_in'], 'CitadelAbilityVData')
        self.assertEqual(slide['fields'][2]['size'], 12)
        self.assertEqual(slide['fields'][3], {'name': 'm_sound', 'offset': 32, 'kind': 'bytes', 'count': 8,
                                              'size': 8, 'schema_type': 'CSoundEventName',
                                              'declared_in': 'CAbilitySlideVData'})
        self.assertEqual(result['probes'], ['CAbilitySlideVData'])
        self.assertEqual(result['structs'][-1]['name'], 'CMoveData')

    def test_known_type_too_wide_for_gap_becomes_bytes(self):
        tight = {'name': 'Tight', 'module': 'server', 'size': 8,
                 'fields': [{'name': 'a', 'type': 'Vector', 'offset': 0},
                            {'name': 'b', 'type': 'float32', 'offset': 4}]}
        fields = sa.build_types(schema(tight), ['Tight'], 'none')['structs'][0]['fields']
        self.assertEqual((fields[0]['kind'], fields[0]['size']), ('bytes', 4))

    def test_missing_class_pattern_is_an_error(self):
        with self.assertRaises(sa.AnalysisError):
            sa.build_types(schema(BASE), [r'CAbilitySlideVData'], 'none')

    def test_client_classes_are_ignored(self):
        client = {**SLIDE, 'module': 'client'}
        with self.assertRaises(sa.AnalysisError):
            sa.build_types(schema(BASE, client), [r'CAbilitySlideVData'], 'none')


class BuildIdTests(unittest.TestCase):
    def test_reads_build_from_app_manifest(self):
        with tempfile.TemporaryDirectory() as folder:
            steamapps = Path(folder) / 'steamapps'
            game = steamapps / 'common' / 'Deadlock'
            game.mkdir(parents=True)
            (steamapps / 'appmanifest_1422450.acf').write_text('"AppState"\n{\n\t"buildid"\t\t"26059233"\n}\n')
            self.assertEqual(sa.steam_build_id(game), '26059233')

    def test_missing_manifest_is_an_error(self):
        with tempfile.TemporaryDirectory() as folder:
            game = Path(folder) / 'a' / 'b'
            game.mkdir(parents=True)
            with self.assertRaises(sa.AnalysisError):
                sa.steam_build_id(game)


if __name__ == '__main__':
    unittest.main()
