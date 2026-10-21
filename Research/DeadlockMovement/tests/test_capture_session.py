import contextlib
import io
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import capture_session as sessions


def synthetic_records(synthetic=True, dropped=0):
    state = {'position': [0, 0, 0], 'velocity': [0, 0, 0],
             'ground_entity': None, 'move_type': None}
    return [
        {'type': 'session', 'schema_version': 1, 'module': 'server', 'units': 'source',
         'timing_status': 'unverified', 'synthetic': synthetic},
        {'type': 'step', 'sequence': 0, 'world': 1, 'pawn_handle': None,
         'command_raw': 0, 'ticks': [0, 0], 'fractions': [0, 0], 'buttons': ['0', '0', '0'],
         'angles': [0, 0, 0], 'axes': [0, 0, 0], 'before': state, 'after': state,
         'wall_ns': 0, 'clock_tick': None, 'clock_time_s': None},
        {'type': 'end', 'attempted': 1 + dropped, 'written': 1, 'dropped': dropped, 'worlds': 1},
    ]


def synthetic_frames(synthetic=True, dropped=0):
    return [
        {'type': 'frame_session', 'schema_version': 1, 'synthetic': synthetic, 'phase': 'after_game_frame'},
        {'type': 'frame', 'sequence': 0, 'world': 1, 'clock_tick': None, 'clock_time_s': None,
         'movement_sequence': 0, 'slot': 0, 'pawn_handle': 1, 'hero_id': 1, 'session_generation': 1,
         'position': [0, 0, 0], 'velocity': None, 'ground_entity': None, 'grounded': None,
         'sliding': None, 'dashing': None, 'climbing': None, 'mantling': None,
         'stamina': None, 'abilities': None},
        {'type': 'frame_end', 'attempted': 1 + dropped, 'written': 1, 'dropped': dropped},
    ]


class SessionTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name).resolve()
        self.native = self.root / 'capture' / 'native'
        self.native.mkdir(parents=True)
        for name in ('observe_step.h', 'observer_plugin.cc', 'recorder_core.h', 'CMakeLists.txt'):
            (self.native / name).write_text('fixture ' + name, encoding='utf-8')
        (self.root / 'capture' / 'modlock-observer.patch').write_text('fixture patch', encoding='utf-8')
        self.sdk = self.root / 'sdk bin'
        self.sdk.mkdir()
        for name in (*sessions.BINARIES, sessions.SDK_DLL_NAMES[0], sessions.SIGNATURES):
            (self.sdk / name).write_bytes(('fixture ' + name).encode())
        self.stamp_build()
        self.prepared = mock.patch.object(sessions, '_inspect_checkout', return_value={
            'head': sessions.MODLOCK_REVISION, 'patch_applied': True, 'helper_present': True})
        self.prepared.start()
        self.addCleanup(self.prepared.stop)

    def stamp_build(self):
        build = {'revision': sessions.MODLOCK_REVISION, 'sources': sessions._source_hashes(self.root),
                 'files': {path.name: sessions._hash_file(path)['sha256'] for path in self.sdk.iterdir()
                           if path.is_file() and (path.suffix.lower() in ('.exe', '.dll')
                                                  or path.name == sessions.SIGNATURES)}}
        (self.sdk / sessions.BUILD_MANIFEST).write_text(json.dumps(build), encoding='utf-8')

    def host(self, *, synthetic=True, dropped=0, code=0, write_trace=True,
             write_frames=True, frame_dropped=0, frame_synthetic=None):
        def launch(argv, **kwargs):
            self.assertFalse(kwargs['shell'])
            self.assertEqual(kwargs['cwd'], str(self.sdk))
            output_index = argv.index('--capture-output')
            self.assertEqual(argv[output_index - 1], '--')
            output_path = Path(argv[output_index + 1])
            self.assertTrue(output_path.is_absolute())
            kwargs['stdout'].write(b'fixture stdout\n')
            kwargs['stderr'].write(b'fixture stderr\n')
            if write_trace:
                output_path.write_text(''.join(json.dumps(item) + '\n' for item in
                                                synthetic_records(synthetic, dropped)), encoding='utf-8')
            if write_frames:
                (output_path.parent / 'frames.jsonl').write_text(''.join(json.dumps(item) + '\n' for item in
                    synthetic_frames(synthetic if frame_synthetic is None else frame_synthetic,
                                     frame_dropped)), encoding='utf-8')
            return subprocess.CompletedProcess(argv, code)
        return mock.patch.object(sessions.subprocess, 'run', side_effect=launch)

    def run_check(self, slug='check', **kwargs):
        return sessions.run_capture(self.sdk, slug, check_only=True, root=self.root, **kwargs)

    def test_check_only_creates_hashed_manifest_logs_and_validation(self):
        with self.host() as launch:
            result = self.run_check()
        directory = Path(result['run_directory'])
        manifest = json.loads((directory / 'manifest.json').read_text(encoding='utf-8'))
        self.assertEqual(result['exit_code'], 0)
        self.assertEqual(result['transport_status'], 'validated')
        self.assertEqual(manifest['status'], 'unverified')
        self.assertEqual(manifest['native_compatibility'], 'unverified')
        self.assertTrue(manifest['synthetic'])
        self.assertEqual(manifest['evidence_status'], 'synthetic_only_not_real_game_evidence')
        self.assertIn('--check-plugin', manifest['argv'])
        self.assertNotIn('--game-dir', manifest['argv'])
        self.assertEqual(manifest['revision'], sessions.MODLOCK_REVISION)
        self.assertEqual(len(manifest['sdk_binaries']), 3)
        self.assertEqual(manifest['build_provenance_status'], 'hashes_verified')
        self.assertEqual(len(manifest['build_manifest']['sha256']), 64)
        self.assertEqual(len(manifest['signatures']['sha256']), 64)
        self.assertIn('capture/native/recorder_core.h', manifest['sources'])
        self.assertEqual((directory / 'stdout.log').read_bytes(), b'fixture stdout\n')
        self.assertEqual((directory / 'stderr.log').read_bytes(), b'fixture stderr\n')
        self.assertTrue(json.loads((directory / 'validation.json').read_text())['valid'])
        self.assertTrue(json.loads((directory / 'frame-validation.json').read_text())['valid'])
        self.assertTrue(manifest['frame_validation_valid'])
        self.assertTrue(manifest['movement_validation_valid'])
        self.assertEqual(manifest['frame_schema_version'], 1)
        launch.assert_called_once()

    def test_live_server_argv_loopback_and_game_hashes_and_build_id(self):
        steamapps = self.root / 'steamapps'
        game = steamapps / 'common' / 'Deadlock'
        for relative in ('game/citadel/bin/win64/server.dll', 'game/bin/win64/engine2.dll',
                         'game/bin/win64/schemasystem.dll'):
            path = game / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(b'fake game module')
        (steamapps / 'appmanifest_1422450.acf').write_text(
            '"AppState" { "buildid" "123456" "SensitiveExtra" "never store this" }', encoding='utf-8')
        with self.host(synthetic=False), contextlib.redirect_stderr(io.StringIO()) as launch_info:
            result = sessions.run_capture(self.sdk, 'live', game_dir=game, root=self.root,
                                          map_name='dl_midtown', port=27069, solo_probe=True, solo_drop=True)
        self.assertIn('Capture run directory:', launch_info.getvalue())
        self.assertIn('stop --run live', launch_info.getvalue())
        manifest_path = Path(result['manifest'])
        manifest = json.loads(manifest_path.read_text())
        self.assertFalse(manifest['synthetic'])
        self.assertTrue(manifest['solo_probe'])
        self.assertIn('--solo-probe', manifest['argv'])
        self.assertTrue(manifest['solo_drop'])
        self.assertIn('--solo-drop', manifest['argv'])
        self.assertEqual(manifest['steam_build_id'], '123456')
        self.assertNotIn('SensitiveExtra', manifest_path.read_text())
        self.assertNotIn('never store this', manifest_path.read_text())
        self.assertEqual(set(manifest['game_binaries']), {'server.dll', 'engine2.dll', 'schemasystem.dll'})
        arguments = manifest['argv']
        self.assertEqual(arguments[arguments.index('--engine-args') + 1], '+ip 127.0.0.1 +sv_hibernate_when_empty 0')
        self.assertEqual(arguments[arguments.index('--hostport') + 1], '27069')
        self.assertEqual(arguments[arguments.index('--map') + 1], 'dl_midtown')
        self.assertNotIn('--check-plugin', arguments)

    def test_solo_probe_rejects_synthetic_mode(self):
        with self.assertRaises(sessions.SessionError), mock.patch.object(sessions.subprocess, 'run') as launch:
            self.run_check('invalid_probe', solo_probe=True)
        launch.assert_not_called()
        with self.assertRaises(sessions.SessionError), mock.patch.object(sessions.subprocess, 'run') as launch:
            self.run_check('invalid_drop', solo_drop=True)
        launch.assert_not_called()

    def test_missing_inputs_refuse_before_creating_capture(self):
        for name in (*sessions.BINARIES, sessions.SDK_DLL_NAMES[0], sessions.SIGNATURES, sessions.BUILD_MANIFEST):
            path = self.sdk / name
            saved = path.read_bytes()
            path.unlink()
            with self.subTest(name=name), mock.patch.object(sessions.subprocess, 'run') as launch:
                with self.assertRaises(sessions.SessionError):
                    self.run_check(name.replace('.', '_'))
                launch.assert_not_called()
                self.assertFalse((self.root / 'local' / 'captures').exists())
            path.write_bytes(saved)

    def test_missing_source_refuses(self):
        (self.native / 'observe_step.h').unlink()
        with self.assertRaises(sessions.SessionError), mock.patch.object(sessions.subprocess, 'run') as launch:
            self.run_check()
        launch.assert_not_called()

    def test_wrong_prepared_state_refuses(self):
        for state in ({'head': sessions.MODLOCK_REVISION, 'patch_applied': False, 'helper_present': True},
                      {'head': sessions.MODLOCK_REVISION, 'patch_applied': True, 'helper_present': False}):
            with self.subTest(state=state), mock.patch.object(sessions, '_inspect_checkout', return_value=state):
                with self.assertRaises(sessions.SessionError):
                    self.run_check()

    def test_bad_run_map_port_and_mode_arguments_never_launch(self):
        cases = [{'run': '../escape'}, {'run': 'with space'}, {'run': 'x' * 81},
                 {'map_name': 'dl_midtown;quit'}, {'map_name': '../map'},
                 {'port': 0}, {'port': 65536}, {'port': True},
                 {'check_only': False}, {'game_dir': self.root}, {'check_only': 'true'}]
        for case in cases:
            arguments = {'run': 'test', 'check_only': True, 'root': self.root, **case}
            with self.subTest(case=case), mock.patch.object(sessions.subprocess, 'run') as launch:
                with self.assertRaises(sessions.SessionError):
                    sessions.run_capture(self.sdk, **arguments)
                launch.assert_not_called()

    def test_live_game_directory_must_be_absolute_and_have_modules(self):
        for game in (Path('relative'), self.root / 'missing-game'):
            with self.subTest(game=game), mock.patch.object(sessions.subprocess, 'run') as launch:
                with self.assertRaises(sessions.SessionError):
                    sessions.run_capture(self.sdk, 'live', game_dir=game, root=self.root)
                launch.assert_not_called()

    def test_existing_run_is_preserved(self):
        with self.host():
            result = self.run_check()
        manifest = Path(result['manifest']).read_bytes()
        with mock.patch.object(sessions.subprocess, 'run') as launch:
            with self.assertRaises(sessions.SessionError):
                self.run_check()
        launch.assert_not_called()
        self.assertEqual(Path(result['manifest']).read_bytes(), manifest)

    def test_host_failure_missing_capture_and_sample_loss_fail_transport(self):
        cases = ({'code': 7}, {'write_trace': False}, {'dropped': 1})
        for index, arguments in enumerate(cases):
            with self.subTest(arguments=arguments), self.host(**arguments):
                result = self.run_check('failure_' + str(index))
                self.assertEqual(result['exit_code'], 1)
                manifest = json.loads(Path(result['manifest']).read_text())
                self.assertEqual(manifest['transport_status'], 'failed')
                self.assertEqual(manifest['native_compatibility'], 'unverified')
                self.assertTrue(Path(manifest['validation_report']).is_file())

    def test_launch_os_error_and_interrupt_preserve_diagnostics(self):
        for index, failure in enumerate((OSError('not executable'), KeyboardInterrupt())):
            with self.subTest(failure=failure), mock.patch.object(sessions.subprocess, 'run', side_effect=failure):
                result = self.run_check('launch_failure_' + str(index))
            self.assertEqual(result['exit_code'], 1)
            manifest = json.loads(Path(result['manifest']).read_text())
            self.assertIn('launch_error', manifest)
            self.assertEqual(manifest['transport_status'], 'failed')

    def test_synthetic_flag_mismatch_fails_transport(self):
        with self.host(synthetic=False):
            result = self.run_check()
        self.assertEqual(result['exit_code'], 1)
        validation = json.loads((Path(result['run_directory']) / 'validation.json').read_text())
        self.assertIn('session_mode_mismatch', [item['code'] for item in validation['errors']])

    def test_new_runs_require_valid_complete_mode_matching_frame_file(self):
        for index, options in enumerate(({'write_frames': False}, {'frame_dropped': 1},
                                          {'frame_synthetic': False})):
            with self.subTest(options=options), self.host(**options):
                result = self.run_check('bad_frames_' + str(index))
            self.assertEqual(result['exit_code'], 1)
            manifest = json.loads(Path(result['manifest']).read_text())
            self.assertTrue(manifest['movement_validation_valid'])
            self.assertFalse(manifest['frame_validation_valid'])
            self.assertFalse(manifest['validation_valid'])
            self.assertEqual(manifest['transport_status'], 'failed')

    def test_frame_validator_source_is_hashed_when_present(self):
        (self.root / 'capture_frames.py').write_text('fixture frame validator')
        self.assertIn('capture_frames.py', sessions._source_hashes(self.root))
        with self.assertRaises(sessions.SessionError):
            self.run_check()

    def test_doctor_presence_is_not_a_compatibility_claim(self):
        with mock.patch.object(sessions.shutil, 'which', side_effect=lambda name: '/tools/' + name):
            report = sessions.doctor(self.sdk)
        self.assertTrue(report['ready_tools'])
        self.assertTrue(all(report['binaries'].values()))
        self.assertEqual(report['native_compatibility'], 'unverified')
        with mock.patch.object(sessions.shutil, 'which', return_value=None):
            self.assertFalse(sessions.doctor()['ready_tools'])
            self.assertTrue(sessions.doctor(self.sdk)['ready'])
            with contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(sessions.main(['doctor', '--sdk-bin', str(self.sdk)]), 0)

    def test_mingw_sdk_filename_and_all_dependencies_are_supported_and_hashed(self):
        (self.sdk / sessions.SDK_DLL_NAMES[0]).rename(self.sdk / sessions.SDK_DLL_NAMES[1])
        for name in ('libprotobuf.dll', 'libabseil.DLL', 'wasmtime.dll', 'modlock-sigcheck.exe'):
            (self.sdk / name).write_bytes(('fixture dependency ' + name).encode())
        self.stamp_build()
        with self.host():
            result = self.run_check()
        manifest = json.loads(Path(result['manifest']).read_text())
        self.assertEqual(set(manifest['sdk_binaries']), {*sessions.BINARIES, sessions.SDK_DLL_NAMES[1],
                                                        'libprotobuf.dll', 'libabseil.DLL',
                                                        'wasmtime.dll', 'modlock-sigcheck.exe'})
        self.assertEqual(sessions.doctor(self.sdk)['sdk_dll'], sessions.SDK_DLL_NAMES[1])

    def test_both_or_no_sdk_dll_names_are_rejected(self):
        first = self.sdk / sessions.SDK_DLL_NAMES[0]
        second = self.sdk / sessions.SDK_DLL_NAMES[1]
        second.write_bytes(b'conflicting alternate SDK')
        with mock.patch.object(sessions.subprocess, 'run') as launch:
            with self.assertRaises(sessions.SessionError):
                self.run_check()
            self.assertFalse(sessions.doctor(self.sdk)['ready'])
            first.unlink()
            second.unlink()
            with self.assertRaises(sessions.SessionError):
                self.run_check()
            launch.assert_not_called()

    def test_stale_or_invalid_build_manifest_is_rejected_before_launch(self):
        path = self.sdk / sessions.BUILD_MANIFEST
        original = path.read_bytes()
        cases = ['not JSON', json.dumps([])]
        build = json.loads(original)
        for name, value in (('revision', '0' * 40), ('sources', {}), ('files', {}),
                            ('files', {'../outside.dll': '0' * 64})):
            changed = {**build, name: value}
            cases.append(json.dumps(changed))
        for content in cases:
            path.write_text(content, encoding='utf-8')
            with self.subTest(content=content), mock.patch.object(sessions.subprocess, 'run') as launch:
                with self.assertRaises(sessions.SessionError):
                    self.run_check()
                launch.assert_not_called()
        path.write_bytes(original)
        (self.native / 'observer_plugin.cc').write_text('changed after build')
        with mock.patch.object(sessions.subprocess, 'run') as launch:
            with self.assertRaises(sessions.SessionError):
                self.run_check()
            launch.assert_not_called()

    def test_added_removed_or_modified_runtime_files_are_rejected(self):
        host = self.sdk / 'modlock-host.exe'
        original = host.read_bytes()
        for change in ('modified_host', 'new_dll', 'new_exe'):
            extra = None
            if change == 'modified_host':
                host.write_bytes(b'modified binary')
            else:
                extra = self.sdk / ('unexpected.dll' if change == 'new_dll' else 'unexpected.exe')
                extra.write_bytes(b'new loader input')
            with self.subTest(change=change), mock.patch.object(sessions.subprocess, 'run') as launch:
                with self.assertRaises(sessions.SessionError):
                    self.run_check()
                launch.assert_not_called()
            host.write_bytes(original)
            if extra is not None:
                extra.unlink()
        dependency = self.sdk / 'wasmtime.dll'
        dependency.write_bytes(b'fixture dependency')
        self.stamp_build()
        dependency.unlink()
        with self.assertRaises(sessions.SessionError):
            self.run_check()

    def test_build_driver_itself_is_in_source_inventory_when_present(self):
        driver = self.root / 'capture_build.py'
        driver.write_text('fixture driver')
        self.assertIn('capture_build.py', sessions._source_hashes(self.root))
        with self.assertRaises(sessions.SessionError):
            self.run_check()

    def running_manifest(self, *, synthetic=False, transport_status='running'):
        directory = self.root / 'local' / 'captures' / 'live'
        directory.mkdir(parents=True, exist_ok=True)
        (directory / 'manifest.json').write_text(json.dumps({
            'synthetic': synthetic, 'transport_status': transport_status}), encoding='utf-8')
        return directory

    def test_stop_creates_exclusive_request_and_is_idempotent_without_launching(self):
        directory = self.running_manifest()
        manifest = (directory / 'manifest.json').read_bytes()
        with mock.patch.object(sessions.subprocess, 'run') as launch:
            first = sessions.stop_capture('live', root=self.root)
            self.assertFalse(first['already_requested'])
            request = directory / 'stop.request'
            self.assertTrue(request.is_file())
            request.write_bytes(b'preserve existing request')
            second = sessions.stop_capture('live', root=self.root)
            self.assertTrue(second['already_requested'])
            self.assertEqual(request.read_bytes(), b'preserve existing request')
            launch.assert_not_called()
        self.assertEqual((directory / 'manifest.json').read_bytes(), manifest)

    def test_scripted_run_is_labelled_and_keeps_its_own_schedule_copy(self):
        source = self.root / 'schedule.grs'
        source.write_text('GRS1\ntrial a 0 1\nt 0 0.0 0.0 0.0 0.0\nend\n', encoding='ascii')
        with self.host():
            result = self.run_check('scripted', input_schedule=source, runner_mode='observe')
        directory = Path(result['run_directory'])
        manifest = json.loads((directory / 'manifest.json').read_text(encoding='utf-8'))
        self.assertTrue(manifest['scripted'])
        self.assertEqual(manifest['input_source'], 'scripted_usercmd')
        self.assertEqual(manifest['runner_mode'], 'observe')
        arguments = manifest['argv']
        copy = directory / 'input-schedule.grs'
        self.assertEqual(arguments[arguments.index('--input-schedule') + 1], str(copy))
        self.assertEqual(arguments[arguments.index('--runner-mode') + 1], 'observe')
        self.assertGreater(arguments.index('--input-schedule'), arguments.index('--capture-output'))
        self.assertEqual(copy.read_bytes(), source.read_bytes())
        self.assertEqual(manifest['input_schedule']['sha256'], sessions._hash_file(copy)['sha256'])

    def test_ordinary_run_is_labelled_human(self):
        with self.host():
            result = self.run_check('ordinary')
        manifest = json.loads((Path(result['run_directory']) / 'manifest.json').read_text(encoding='utf-8'))
        self.assertFalse(manifest['scripted'])
        self.assertEqual(manifest['input_source'], 'human')
        self.assertNotIn('--input-schedule', manifest['argv'])

    def test_scripted_run_rejects_foreign_schedule_and_bad_mode(self):
        source = self.root / 'schedule.json'
        source.write_text('{}', encoding='utf-8')
        with self.assertRaises(sessions.SessionError):
            self.run_check('foreign', input_schedule=source)
        source.write_text('GRS1\n', encoding='ascii')
        with self.assertRaises(sessions.SessionError):
            self.run_check('badmode', input_schedule=source, runner_mode='replace')
        with self.assertRaises(sessions.SessionError):
            self.run_check('solo', input_schedule=source, solo_probe=True)

    def test_runner_start_needs_a_running_scripted_capture(self):
        directory = self.running_manifest()
        with self.assertRaises(sessions.SessionError):
            sessions.runner_start('live', root=self.root)
        (directory / 'manifest.json').write_text(json.dumps(
            {'synthetic': False, 'transport_status': 'running', 'scripted': True}), encoding='utf-8')
        self.assertEqual(sessions.runner_start('live', root=self.root)['status'], 'runner_start_requested')
        self.assertTrue((directory / 'runner-start.request').is_file())
        with self.assertRaises(sessions.SessionError):
            sessions.runner_start('live', root=self.root)

    def test_stop_refuses_missing_finished_synthetic_and_invalid_runs(self):
        with self.assertRaises(sessions.SessionError):
            sessions.stop_capture('missing', root=self.root)
        for synthetic, status in ((True, 'running'), (False, 'validated'), (False, 'failed'),
                                  (None, 'running'), (0, 'running')):
            directory = self.running_manifest(synthetic=synthetic, transport_status=status)
            with self.subTest(synthetic=synthetic, status=status):
                with self.assertRaises(sessions.SessionError):
                    sessions.stop_capture('live', root=self.root)
                self.assertFalse((directory / 'stop.request').exists())
        for slug in ('../escape', 'with space', '', 'x' * 81):
            with self.subTest(slug=slug), self.assertRaises(sessions.SessionError):
                sessions.stop_capture(slug, root=self.root)
        (directory / 'manifest.json').write_text('invalid JSON', encoding='utf-8')
        with self.assertRaises(sessions.SessionError):
            sessions.stop_capture('live', root=self.root)

    def test_stop_refuses_nonregular_existing_request(self):
        directory = self.running_manifest()
        (directory / 'stop.request').mkdir()
        with self.assertRaises(sessions.SessionError):
            sessions.stop_capture('live', root=self.root)

    def test_stop_cli_uses_request_without_process_termination(self):
        with mock.patch.object(sessions, 'stop_capture', return_value={'status': 'stop_requested'}) as stop:
            with contextlib.redirect_stdout(io.StringIO()) as output:
                self.assertEqual(sessions.main(['stop', '--run', 'live']), 0)
        stop.assert_called_once_with('live')
        self.assertEqual(json.loads(output.getvalue())['status'], 'stop_requested')

    def test_marker_payload_is_published_complete_and_not_acknowledged(self):
        directory = self.running_manifest()
        original_rename = sessions.os.rename
        original_link = sessions.os.link
        def publication(source, target):
            self.assertTrue(str(source).endswith('.tmp'))
            self.assertTrue(str(target).endswith('.request'))
            self.assertFalse(Path(target).exists())
            self.assertEqual(Path(source).read_bytes(),
                             b'00000000000000000123_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\nfall_01\nstart\n')
            (original_rename if sessions.os.name == 'nt' else original_link)(source, target)
        with mock.patch.object(sessions.time, 'time_ns', return_value=123):
            with mock.patch.object(sessions.uuid, 'uuid4', return_value=mock.Mock(hex='a' * 32)):
                with mock.patch.object(sessions.os, 'rename' if sessions.os.name == 'nt' else 'link', side_effect=publication):
                    with mock.patch.object(sessions.subprocess, 'run') as launch:
                        result = sessions.mark_capture('live', 'fall_01', 'start', root=self.root)
                        launch.assert_not_called()
        request = Path(result['request_file'])
        self.assertEqual(request.parent, directory / 'marker-requests')
        self.assertEqual(request.name, result['request_id'] + '.request')
        self.assertEqual(request.read_bytes(), (result['request_id'] + '\nfall_01\nstart\n').encode('ascii'))
        self.assertFalse(list(request.parent.glob('*.tmp')))
        self.assertEqual(result['status'], 'queued')
        self.assertFalse(result['acknowledged'])
        self.assertIn('confirm the matching request_id', result['note'])
        self.assertIn('not action time', result['note'])

    def test_marker_repeated_events_create_distinct_preserved_requests(self):
        self.running_manifest()
        first = sessions.mark_capture('live', 'trial-A', 'arm', root=self.root)
        first_bytes = Path(first['request_file']).read_bytes()
        second = sessions.mark_capture('live', 'trial-A', 'arm', root=self.root)
        self.assertNotEqual(first['request_id'], second['request_id'])
        self.assertEqual(Path(first['request_file']).read_bytes(), first_bytes)
        self.assertTrue(Path(second['request_file']).is_file())

    def test_marker_collision_refuses_to_overwrite(self):
        self.running_manifest()
        with mock.patch.object(sessions.time, 'time_ns', return_value=123):
            with mock.patch.object(sessions.uuid, 'uuid4', return_value=mock.Mock(hex='a' * 32)):
                first = sessions.mark_capture('live', 'trial-A', 'arm', root=self.root)
                original = Path(first['request_file']).read_bytes()
                with self.assertRaises(sessions.SessionError):
                    sessions.mark_capture('live', 'trial-B', 'start', root=self.root)
        self.assertEqual(Path(first['request_file']).read_bytes(), original)

    def test_marker_requires_running_live_manifest_and_safe_arguments(self):
        for trial in ('', '../path', 'with space', 'x' * 65, 'caf\u00e9', 'trial\nstart', None):
            with self.subTest(trial=trial), self.assertRaises(sessions.SessionError):
                sessions.mark_capture('live', trial, 'arm', root=self.root)
        with self.assertRaises(sessions.SessionError):
            sessions.mark_capture('../escape', 'trial', 'arm', root=self.root)
        with self.assertRaises(sessions.SessionError):
            sessions.mark_capture('missing', 'trial', 'arm', root=self.root)
        with self.assertRaises(sessions.SessionError):
            sessions.mark_capture('live', 'trial', 'finish', root=self.root)
        for synthetic, state in ((True, 'running'), (False, 'failed'), (False, 'validated'), (None, 'running')):
            directory = self.running_manifest(synthetic=synthetic, transport_status=state)
            with self.subTest(synthetic=synthetic, state=state), self.assertRaises(sessions.SessionError):
                sessions.mark_capture('live', 'trial', 'arm', root=self.root)
            self.assertFalse((directory / 'marker-requests').exists())

    def test_marker_cli_reports_queued_only(self):
        with mock.patch.object(sessions, 'mark_capture', return_value={'status': 'queued', 'acknowledged': False}) as mark:
            with contextlib.redirect_stdout(io.StringIO()) as output:
                self.assertEqual(sessions.main(['mark', '--run', 'live', '--trial', 'fall_01', '--event', 'start']), 0)
        mark.assert_called_once_with('live', 'fall_01', 'start')
        self.assertFalse(json.loads(output.getvalue())['acknowledged'])

    def test_new_clone_pins_repository_and_revision_without_reset(self):
        with mock.patch.object(sessions.subprocess, 'run', return_value=subprocess.CompletedProcess([], 0, '', '')) as clone:
            with mock.patch.object(sessions, '_git', return_value=subprocess.CompletedProcess([], 0, '', '')) as git:
                self.assertEqual(sessions.prepare(self.root)['status'], 'prepared')
        self.assertEqual(clone.call_args.args[0], ['git', 'clone', '--no-checkout', sessions.MODLOCK_URL,
                                                 str(self.root / 'local' / 'modlock')])
        git.assert_called_once_with(self.root / 'local' / 'modlock', 'checkout', '--detach', sessions.MODLOCK_REVISION)

    def test_failed_clone_preserves_partial_directory(self):
        checkout = self.root / 'local' / 'modlock'
        def fail_clone(argv, **kwargs):
            checkout.mkdir(parents=True)
            (checkout / 'partial').write_bytes(b'preserve clone diagnostics')
            return subprocess.CompletedProcess(argv, 1, '', 'clone fixture failure')
        with mock.patch.object(sessions.subprocess, 'run', side_effect=fail_clone):
            with self.assertRaises(sessions.SessionError):
                sessions.prepare(self.root)
        self.assertEqual((checkout / 'partial').read_bytes(), b'preserve clone diagnostics')

    def test_cli_exit_codes_and_json(self):
        with mock.patch.object(sessions, 'prepare', return_value={'status': 'prepared'}), contextlib.redirect_stdout(io.StringIO()) as output:
            self.assertEqual(sessions.main(['prepare']), 0)
            self.assertEqual(json.loads(output.getvalue())['status'], 'prepared')
        with mock.patch.object(sessions, 'prepare', side_effect=sessions.SessionError('refused')), contextlib.redirect_stderr(io.StringIO()) as output:
            self.assertEqual(sessions.main(['prepare']), 2)
            self.assertEqual(json.loads(output.getvalue())['error'], 'refused')
        with mock.patch.object(sessions, 'doctor', return_value={'ready_tools': False}), contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(sessions.main(['doctor']), 1)
        with mock.patch.object(sessions, 'run_capture', return_value={'exit_code': 1}) as capture, contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(sessions.main(['run', '--sdk-bin', str(self.sdk), '--run', 'check', '--check-only']), 1)
            self.assertTrue(capture.call_args.kwargs['check_only'])


@unittest.skipUnless(shutil.which('git'), 'Temporary repository preparation checks need git.')
class PrepareTests(unittest.TestCase):
    def git(self, *args):
        result = subprocess.run(['git', '-C', str(self.checkout), *args], shell=False,
                                check=True, capture_output=True, text=True)
        return result.stdout.strip()

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name).resolve()
        self.checkout = self.root / 'local' / 'modlock'
        self.checkout.mkdir(parents=True)
        native = self.root / 'capture' / 'native'
        native.mkdir(parents=True)
        (native / 'observe_step.h').write_bytes(b'fixture helper\n')
        self.patch = self.root / 'capture' / 'modlock-observer.patch'
        self.git('init')
        self.git('config', 'user.email', 'fixtures@example.invalid')
        self.git('config', 'user.name', 'Capture test fixture')
        self.git('config', 'core.autocrlf', 'false')
        self.git('remote', 'add', 'origin', sessions.MODLOCK_URL)
        (self.checkout / '.gitignore').write_text('vendor/\nbuild*/\n')
        self.originals = {'file_a.txt': 'first\nsecond\n', 'CMakeLists.txt': 'original\n'}
        for name, content in self.originals.items():
            (self.checkout / name).write_text(content)
        self.git('add', '.')
        self.git('commit', '-m', 'Synthetic preparation fixture')
        head = self.git('rev-parse', 'HEAD')
        self.revision = mock.patch.object(sessions, 'MODLOCK_REVISION', head)
        self.revision.start()
        self.addCleanup(self.revision.stop)
        for name, content in self.originals.items():
            (self.checkout / name).write_text(content + 'patched\n')
        diff = subprocess.run(['git', '-C', str(self.checkout), 'diff', '--binary'], shell=False,
                              check=True, capture_output=True, text=True).stdout
        self.patch.write_text(diff, encoding='utf-8')
        for name, content in self.originals.items():
            (self.checkout / name).write_text(content)

    def test_prepare_applies_generic_patch_and_is_idempotent(self):
        report = sessions.prepare(self.root)
        self.assertEqual(report['status'], 'prepared')
        for name, content in self.originals.items():
            self.assertEqual((self.checkout / name).read_text(), content + 'patched\n')
        helper = self.checkout / sessions.HELPER_PATH
        self.assertEqual(helper.read_bytes(), b'fixture helper\n')
        before = self.git('status', '--porcelain=v1', '--untracked-files=all')
        self.assertTrue(sessions.prepare(self.root)['patch_applied'])
        self.assertEqual(self.git('status', '--porcelain=v1', '--untracked-files=all'), before)
        self.assertEqual(self.git('diff', '--cached', '--name-only'), '')

    def test_unexpected_untracked_changes_are_preserved(self):
        extra = self.checkout / 'unexpected.txt'
        extra.write_bytes(b'do not discard')
        with self.assertRaises(sessions.SessionError):
            sessions.prepare(self.root)
        self.assertEqual(extra.read_bytes(), b'do not discard')
        self.assertEqual((self.checkout / 'file_a.txt').read_text(), self.originals['file_a.txt'])

    def test_partial_patch_or_extra_same_file_edits_are_rejected(self):
        path = self.checkout / 'file_a.txt'
        path.write_text(self.originals['file_a.txt'] + 'patched\n')
        with self.assertRaises(sessions.SessionError):
            sessions.prepare(self.root)
        self.assertEqual(path.read_text(), self.originals['file_a.txt'] + 'patched\n')
        for name, content in self.originals.items():
            (self.checkout / name).write_text(content + 'patched\n')
        path.write_text(path.read_text() + 'extra\n')
        with self.assertRaises(sessions.SessionError):
            sessions.prepare(self.root)
        self.assertIn('extra', path.read_text())

    def test_wrong_head_staged_changes_origin_and_helper_are_rejected(self):
        with mock.patch.object(sessions, 'MODLOCK_REVISION', '0' * 40):
            with self.assertRaises(sessions.SessionError):
                sessions.prepare(self.root)
        (self.checkout / 'file_a.txt').write_text('staged change\n')
        self.git('add', 'file_a.txt')
        with self.assertRaises(sessions.SessionError):
            sessions.prepare(self.root)
        self.assertIn('staged change', (self.checkout / 'file_a.txt').read_text())

    def test_wrong_origin_refuses(self):
        self.git('remote', 'set-url', 'origin', 'https://example.invalid/unexpected')
        with self.assertRaises(sessions.SessionError):
            sessions.prepare(self.root)

    def test_differing_existing_helper_refuses(self):
        helper = self.checkout / sessions.HELPER_PATH
        helper.parent.mkdir(parents=True)
        helper.write_bytes(b'personal helper edits')
        with self.assertRaises(sessions.SessionError):
            sessions.prepare(self.root)
        self.assertEqual(helper.read_bytes(), b'personal helper edits')

    def test_ignored_dependency_and_build_artifacts_are_allowed(self):
        for directory in ('vendor', 'build-test'):
            path = self.checkout / directory
            path.mkdir()
            (path / 'artifact').write_bytes(b'ignored build fixture')
        self.assertEqual(sessions.prepare(self.root)['status'], 'prepared')


if __name__ == '__main__':
    unittest.main()
