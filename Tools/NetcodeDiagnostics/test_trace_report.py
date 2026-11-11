import copy
import json
import pathlib
import struct
import tempfile
import unittest

import trace_report as trace


def bits(number):
    return struct.unpack('<i', struct.pack('<f', number))[0]


def state(x=0, buttons=0):
    return {'origin': {'x': bits(x), 'y': 0, 'z': 0}, 'velocity': {'x': 0, 'y': 0, 'z': 0},
            'lastInput': {'frame': 25, 'characterObjectId': 3, 'buttons': buttons}}


def event(kind, sequence, **kwargs):
    return dict(type=kind, sequence=sequence, entity=3, epoch=1, tick=25, sourceTick=40,
                role='owner', renderFrame=1, hasState=True, state=state(), **kwargs)


class TraceReportTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = pathlib.Path(self.temp.name)

    def bundle(self, process, events, *, manifest_changes=None, suffix='', footer=True):
        path = self.root / process
        path.mkdir(exist_ok=True)
        manifest = dict(schema=1, session='run', process=process, peer='B' if process == 'A' else 'A',
                        build='build', unity='6000', scene='test', tickRate=60, fixedDelta=.02,
                        configuration='config', revision='commit', artifactHash='sha', contentHash='scenehash',
                        backend='Mono', physics='physics', frequency=1000000)
        manifest.update(manifest_changes or {})
        (path / 'manifest.json').write_text(json.dumps(manifest))
        records = [json.dumps(e) for e in events]
        if footer:
            records.append(json.dumps(dict(type='footer', complete=True, written=len(events), dropped=0)))
        (path / 'events.jsonl').write_text('\n'.join(records) + '\n' + suffix)
        return trace.load_bundle(path)

    def pair(self, owner):
        server = event('snapshotBuilt', 1, **{'pass': 0})
        server['role'] = 'server'
        arrived = event('snapshotArrived', 1, **{'pass': 1})
        return [self.bundle('A', [server]), self.bundle('B', [arrived] + owner)]

    def test_first_input_error_preserves_mapped_and_original_tick(self):
        predicted = event('predictedHistory', 2, **{'pass': 1})
        baseline = event('authoritativeBaseline', 3, **{'pass': 1})
        baseline['state'] = state(1, 4)
        result = trace.analyze(self.pair([predicted, baseline]))
        first = result['firstObservedDifference']
        self.assertEqual('input/tick mismatch', first['classification'])
        self.assertEqual(25, first['tick'])
        self.assertEqual(40, first['sourceTick'])
        self.assertEqual('origin.x', first['differences'][0]['field'])
        self.assertEqual(1, first['differences'][0]['absolute'])
        self.assertEqual(1, result['snapshotLinks'])

    def test_equal_captured_fields_are_not_determinism_proof(self):
        result = trace.analyze(self.pair([event('predictedHistory', 2, **{'pass': 1}),
                                          event('authoritativeBaseline', 3, **{'pass': 1})]))
        self.assertIsNone(result['firstObservedDifference'])
        self.assertIn('unsupported', result['scope'])
        self.assertTrue(result['complete'])

    def test_missing_history_is_not_equality(self):
        result = trace.analyze(self.pair([event('authoritativeBaseline', 2, **{'pass': 1})]))
        self.assertEqual('insufficient evidence', result['findings'][0]['classification'])

    def test_explicit_absent_state_overrides_inline_default_state(self):
        predicted = event('predictedHistory', 2, **{'pass': 1})
        predicted['hasState'] = False
        result = trace.analyze(self.pair([predicted, event('authoritativeBaseline', 3, **{'pass': 1})]))
        self.assertEqual('insufficient evidence', result['findings'][0]['classification'])

    def test_restore_failure_is_separate_from_prediction(self):
        restored = event('restored', 4, **{'pass': 1})
        restored['state'] = state(2)
        result = trace.analyze(self.pair([event('predictedHistory', 2, **{'pass': 1}),
            event('authoritativeBaseline', 3, **{'pass': 1}), restored]))
        self.assertEqual('restore/replay mismatch', result['findings'][0]['classification'])

    def test_proxy_presentation_is_not_compared_to_owner(self):
        proxy = event('authoritativeBaseline', 2, **{'pass': 1})
        proxy['role'] = 'proxy'; proxy['state'] = state(100)
        presentation = copy.deepcopy(proxy); presentation['type'] = 'presentation'; presentation['sequence'] = 3
        result = trace.analyze(self.pair([proxy, presentation]))
        self.assertEqual([], result['findings'])

    def test_replay_revisions_do_not_overwrite_history(self):
        original = event('predictedHistory', 2, **{'pass': 1})
        other = event('predictedHistory', 3, **{'pass': 2}); other['state'] = state(100)
        result = trace.analyze(self.pair([original, other, event('authoritativeBaseline', 4, **{'pass': 1})]))
        self.assertEqual([], result['findings'])

    def test_signed_zero_raw_bits_not_lost(self):
        baseline = event('authoritativeBaseline', 3, **{'pass': 1}); baseline['state'] = state(-0.0)
        result = trace.analyze(self.pair([event('predictedHistory', 2, **{'pass': 1}), baseline]))
        self.assertTrue(result['firstObservedDifference']['rawBitDifferences'])

    def test_truncated_record_and_missing_footer_reported(self):
        bundle = self.bundle('A', [event('postTick', 1, **{'pass': 0})], footer=False, suffix='{"type":')
        self.assertEqual(1, len(bundle['events']))
        self.assertEqual(2, len(bundle['gaps']))
        self.assertFalse(trace.analyze([bundle])['complete'])

    def test_sequence_gaps_duplicates_reorder_are_not_transport_loss(self):
        bundle = self.bundle('A', [event('postTick', n, **{'pass': 0}) for n in (1, 3, 3, 2)])
        self.assertEqual(3, len(bundle['gaps']))

    def test_mismatched_build_and_session_rejected_for_paired_evidence(self):
        bundles = self.pair([])
        bundles[1]['manifest']['session'] = 'another'; bundles[1]['manifest']['build'] = 'another'
        result = trace.analyze(bundles)
        self.assertFalse(result['complete'])
        self.assertIn('Incompatible session', result['compatibilityIssues'])
        self.assertIn('Incompatible build', result['compatibilityIssues'])

    def test_schema_rejected(self):
        with self.assertRaises(ValueError):
            self.bundle('A', [], manifest_changes={'schema': 999})

    def test_nonfinite_values_reported_and_html_escaped(self):
        invalid = event('postTick', 2, **{'pass': 0}); invalid['state'] = state(float('nan'))
        marker = event('marker', 3, **{'pass': 0}); marker.update(marker='id', reason='<script>alert(1)</script>')
        result = trace.analyze(self.pair([invalid, marker]))
        self.assertTrue(any('Non-finite' in v for v in result['compatibilityIssues']))
        trace.write_report(result, self.root / 'out')
        output = (self.root / 'out/report.html').read_text(encoding='utf-8')
        self.assertNotIn('<script>', output)
        json.loads((self.root / 'out/findings.json').read_text())


class DivergenceClassificationTests(TraceReportTests):
    """Server postTick joined with the owner's first-pass postTick, classified at the first divergent tick."""

    def setUp(self):
        super().setUp()
        self.sequence = {'server': 0, 'owner': 0}

    def record(self, role, kind, tick, x=0, buttons=0, replay_pass=0, **extra):
        self.sequence[role] += 1
        value = state(x, buttons)
        value['lastInput']['frame'] = tick
        return dict(type=kind, sequence=self.sequence[role], entity=3, epoch=1, tick=tick, sourceTick=-1, role=role,
                    hasState=True, state=value, input={'frame': tick, 'characterObjectId': 3, 'buttons': buttons},
                    **{'pass': replay_pass}, **extra)

    def tick(self, role, tick, before=0, after=0, buttons=0, created=True, replay_pass=0):
        reason = 'created input' if created else 'predicted held input or empty seed'
        return [self.record(role, 'preTick', tick, before, buttons, replay_pass, reason=reason),
                self.record(role, 'postTick', tick, after, buttons, replay_pass)]

    def marker(self, role, note):
        self.sequence[role] += 1
        return dict(type='marker', sequence=self.sequence[role], marker='id' + note, reason=note)

    def divergence(self, server, owner):
        return trace.analyze([self.bundle('A', server), self.bundle('B', owner)])['divergence']

    def test_identical_ticks_have_no_episode(self):
        result = self.divergence(self.tick('server', 10, 0, 1) + self.tick('server', 11, 1, 2),
                                 self.tick('owner', 10, 0, 1) + self.tick('owner', 11, 1, 2))
        self.assertEqual(2, result['joinedTicks'])
        self.assertEqual([], result['episodes'])

    def test_substituted_server_input_is_class_a(self):
        server = self.tick('server', 10, 0, 1) + self.tick('server', 11, 1, 2, buttons=0, created=False)
        owner = self.tick('owner', 10, 0, 1) + self.tick('owner', 11, 1, 3, buttons=1)
        result = self.divergence(server, owner)
        self.assertEqual('A', result['episodes'][0]['class'])
        self.assertTrue(result['episodes'][0]['serverSubstituted'])
        self.assertIn('substituted', result['episodes'][0]['reason'])
        self.assertEqual(1, result['serverSubstitutedTicks'])

    def test_tick_only_one_side_ran_is_class_a(self):
        server = self.tick('server', 10, 0, 1) + self.tick('server', 12, 2, 3)
        owner = self.tick('owner', 10, 0, 1) + self.tick('owner', 11, 1, 2) + self.tick('owner', 12, 2, 3)
        result = self.divergence(server, owner)
        self.assertEqual(['A'], [e['class'] for e in result['episodes']])
        self.assertEqual(11, result['episodes'][0]['tick'])

    def test_same_pre_and_input_different_post_is_class_b(self):
        result = self.divergence(self.tick('server', 10, 0, 1) + self.tick('server', 11, 1, 2),
                                 self.tick('owner', 10, 0, 1) + self.tick('owner', 11, 1, 2.5))
        episode = result['episodes'][0]
        self.assertEqual('B', episode['class'])
        self.assertEqual(['origin.x'], episode['firstFields'])
        self.assertEqual({'origin.x': 1}, result['firstDivergentFields'])

    def test_restore_that_differs_from_server_state_is_class_c(self):
        server = self.tick('server', 10, 0, 1) + self.tick('server', 11, 1, 2) + self.tick('server', 12, 2, 3)
        owner = self.tick('owner', 10, 0, 1) + self.tick('owner', 11, 1, 2)
        restored = self.record('owner', 'restored', 10, 1, replay_pass=1)
        restored['state']['wishJump'] = True  # not on the wire: survives from the predicted timeline
        owner += [restored] + self.tick('owner', 11, 1, 2, replay_pass=1) + self.tick('owner', 12, 9, 10)
        episode = self.divergence(server, owner)['episodes'][0]
        self.assertEqual('C', episode['class'])
        self.assertEqual(12, episode['tick'])
        self.assertEqual(['wishJump'], episode['notOnWire'])

    def test_nondeterministic_replay_tick_is_class_b(self):
        server = self.tick('server', 10, 0, 1) + self.tick('server', 11, 1, 2) + self.tick('server', 12, 2, 3)
        owner = (self.tick('owner', 10, 0, 1) + self.tick('owner', 11, 1, 2) + [self.record('owner', 'restored', 10, 1, replay_pass=1)]
                 + self.tick('owner', 11, 1, 2.5, replay_pass=1) + self.tick('owner', 12, 2.5, 3.5))
        episode = self.divergence(server, owner)['episodes'][0]
        self.assertEqual('B', episode['class'])
        self.assertEqual(11, episode['replayTick'])

    def test_pre_state_change_without_restore_is_unclassified(self):
        result = self.divergence(self.tick('server', 10, 0, 1) + self.tick('server', 11, 1, 2),
                                 self.tick('owner', 10, 0, 1) + self.tick('owner', 11, 5, 6))
        self.assertEqual('unclassified', result['episodes'][0]['class'])
        self.assertIn('owner', result['episodes'][0]['reason'])

    def test_consecutive_divergent_ticks_form_one_episode_per_scenario(self):
        server = ([self.marker('server', 'walk')] + self.tick('server', 10, 0, 1) + self.tick('server', 11, 1, 2)
                  + self.tick('server', 12, 2, 3) + [self.marker('server', 'dash')] + self.tick('server', 13, 3, 4)
                  + self.tick('server', 14, 4, 5))
        owner = (self.tick('owner', 10, 0, 1) + self.tick('owner', 11, 1, 7) + self.tick('owner', 12, 7, 8)
                 + self.tick('owner', 13, 3, 4) + self.tick('owner', 14, 4, 9))
        result = self.divergence(server, owner)
        self.assertEqual([2, 1], [e['lengthTicks'] for e in result['episodes']])
        self.assertEqual({'B': {'walk': 1, 'dash': 1}}, result['counts'])

    def test_tick_offset_is_measured_from_snapshot_links(self):
        built = self.record('server', 'snapshotBuilt', 110, 1); built['sourceTick'] = 110
        built['state']['lastInput']['frame'] = 10
        server = self.tick('server', 10, 0, 1) + [built] + self.tick('server', 11, 1, 2)
        arrived = self.record('owner', 'snapshotArrived', 15); arrived['sourceTick'] = 110; arrived['hasState'] = False
        owner = self.tick('owner', 15, 0, 1) + [arrived] + self.tick('owner', 16, 1, 2)
        result = self.divergence(server, owner)
        self.assertEqual(5, result['tickOffset'])
        self.assertEqual(2, result['joinedTicks'])
        self.assertEqual([], result['episodes'])

    def test_bundles_without_server_and_owner_ticks_are_not_applicable(self):
        result = trace.analyze(self.pair([]))['divergence']
        self.assertEqual('not applicable', result['status'])


if __name__ == '__main__':
    unittest.main()
