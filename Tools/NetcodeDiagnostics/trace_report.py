"""Offline, dependency-free live capture analysis. Never grants a determinism verdict."""
import argparse
import collections
import html
import json
import math
import pathlib
import struct

FLOATS = set('forwardMove sideMove upMove surfaceFriction gravityFactor walkFactor verticalAxis horizontalAxis crouchLerp renderCrouchLerp maxStaminaBonus staminaRegenBonus stamina staminaRegenTimer dashTimer currentDashDuration dashCooldownTimer jumpTimer coyoteTimer slideSpeedCurrent slideDelay dashSlideTimer slideGraceTimer dashJumpAirControlTimer wallJumpFatigueTime mantleTime mantlePushTimer mantleDistance mantleHeight mantleOffset mantleVerticalTime slopeLimit rigidbodyPushForce defaultHeight crouchingHeight crouchingSpeed fallingVelocity stepOffset meleeTimer meleeCooldownTimer meleeHoldTimer parryTimer parryCooldownTimer'.split())
VECTORS = set('viewTransformDefaultLocalPos origin viewAngles velocity slideDirection dashDirection wallNormal wallSeenOrigin mantleStart mantleDirection ladderNormal ladderDirection ladderClimbDir ladderVelocity'.split())
# Fields actually restored from the reconcile payload, as opposed to fields
# inherited from local predicted history by BuildAuthoritativeMoveData.
WIRE_FIELDS = set('origin velocity stamina staminaRegenTimer dashTimer currentDashDuration dashCooldownTimer isDashing canAirDash jumpCount jumpTimer coyoteTimer dashDirection airDashing grounded groundedTemp jumping moveType forwardMove sideMove verticalAxis horizontalAxis surfaceFriction gravityFactor walkFactor fallingVelocity crouching crouchLerp renderCrouchLerp uncrouchDown sliding wasSliding slideSpeedCurrent slideDirection slideDelay dashSlideTimer slideGraceTimer canDashJump dashJumpAirControlTimer wallNormal wallSeenOrigin wallJumpFatigueTime consecutiveWallJumps mantling mantleTime mantlePushTimer mantleStart mantleDirection mantleDistance mantleHeight mantleOffset mantleVerticalTime mantleJumpBuffered dashStartedThisFrame doubleJumpedThisFrame meleeHitThisFrame meleeState meleeTimer meleeCooldownTimer meleeHoldTimer hasHitTarget meleeHitResolved meleeHitTargetObjectId meleeHitResolveTick isParrying parryTimer parryCooldownTimer parryStartedThisFrame parrySuccessThisFrame lastConsumedParryPressFrame lastConsumedJumpPressFrame lastConsumedDashPressFrame defaultHeight crouchingHeight crouchingSpeed slidingEnabled useStepOffset stepOffset maxStaminaBonus staminaRegenBonus'.split())


def float_value(bits):
    return struct.unpack('<f', struct.pack('<I', bits & 0xffffffff))[0]


def readable(state):
    result = {}
    for name, value in state.items():
        if name in FLOATS:
            result[name] = float_value(value)
        elif name in VECTORS:
            result[name] = {axis: float_value(bits) for axis, bits in value.items()}
        else:
            result[name] = value
    return result


def differences(left, right, prefix=''):
    """Missing fields are evidence gaps, never silently zero or equal."""
    result = []
    for name in sorted(left.keys() | right.keys()):
        path = prefix + name
        a, b = left.get(name), right.get(name)
        if isinstance(a, dict) and isinstance(b, dict):
            result.extend(differences(a, b, path + '.'))
        elif a != b or name not in left or name not in right:
            item = {'field': path, 'left': a, 'right': b}
            if isinstance(a, (float, int)) and isinstance(b, (float, int)):
                item['absolute'] = abs(a - b)
                item['relative'] = abs(a - b) / max(abs(a), abs(b), 1e-30)
            result.append(item)
    return result


def load_bundle(path):
    path = pathlib.Path(path)
    manifest = json.loads((path / 'manifest.json').read_text(encoding='utf-8-sig'))
    if manifest.get('schema') != 1:
        raise ValueError(f'Unsupported trace schema: {manifest.get("schema")}')
    events, gaps, footer = [], [], None
    previous = None
    with (path / 'events.jsonl').open(encoding='utf-8-sig') as stream:
        for line, raw in enumerate(stream, 1):
            try:
                event = json.loads(raw)
            except (ValueError, UnicodeError):
                gaps.append(f'Malformed/truncated record at line {line}')
                continue
            if event.get('type') == 'footer':
                if footer is not None:
                    gaps.append('Duplicate footer')
                footer = event
                continue
            if footer is not None:
                gaps.append('Records after footer')
            event['_line'] = line
            event['_process'] = manifest['process']
            event['_raw'] = (path / 'events.jsonl').resolve().as_uri()
            seq = event.get('sequence')
            if previous is not None and seq != previous + 1:
                gaps.append(f'Sequence gap/duplicate/reorder at line {line}: {previous} -> {seq}')
            previous = seq
            events.append(event)
    if footer is None:
        gaps.append('Missing footer: process stopped or export incomplete')
    elif not footer.get('complete') or footer.get('dropped', 0) or footer.get('error'):
        gaps.append(f'Incomplete capture: dropped={footer.get("dropped")}, error={footer.get("error")}')
    if footer and footer.get('written') != len(events):
        gaps.append('Footer record count does not match file')
    if not events:
        gaps.append('No captured events')
    return {'manifest': manifest, 'events': events, 'gaps': gaps, 'footer': footer}


def evidence(event):
    return {k: event.get(k) for k in ('_process', '_raw', '_line', 'entity', 'epoch', 'tick', 'sourceTick', 'role', 'pass')}


CLASS_NAMES = {'A': 'input mismatch', 'B': 'sim nondeterminism', 'C': 'restore gap',
               'unclassified': 'unclassified'}


def _state(event):
    if not event or not event.get('hasState', bool(event.get('state'))):
        return None
    return event.get('state') or None


def _sim(state):
    """Simulation state without the tick stamp and the input seed, which are compared as input."""
    return {k: v for k, v in (state or {}).items() if k not in ('frame', 'lastInput')}


def _controls(value):
    controls = dict(value or {})
    controls.pop('frame', None)
    return controls


def _fields(diffs):
    return [d['field'] for d in diffs]


def _ticks(events, role):
    """Pair each preTick with its postTick. Result: (entity, epoch) -> pass -> tick -> [record]."""
    result = collections.defaultdict(lambda: collections.defaultdict(lambda: collections.defaultdict(list)))
    pending = {}
    for event in events:
        if event.get('role') != role:
            continue
        key = (event.get('entity'), event.get('epoch'), event.get('pass'), event.get('tick'))
        if event.get('type') == 'preTick':
            pending[key] = event
        elif event.get('type') == 'postTick':
            pre = pending.pop(key, None)
            result[key[:2]][key[2]][key[3]].append({
                'pre': pre, 'post': event, 'input': event.get('input'),
                'created': bool(pre) and pre.get('reason') == 'created input'})
    return result


def _scenario(labels, sequence):
    name = 'unmarked'
    for at, label in labels:
        if at > sequence:
            break
        name = label
    return name


def classify_divergence(bundles):
    """Join the server's ticks with the owner's first-pass ticks and classify each episode's first tick.

    A: the two sides did not run the same input for the tick (or one side did not run the tick once).
    B: identical pre-tick bits and identical input produced different post-tick bits.
    C: the divergence enters through a reconcile restore.
    Equality here covers captured fields only; it is evidence, not a determinism proof.
    """
    empty = {'status': 'not applicable', 'joinedTicks': 0, 'divergentTicks': 0, 'episodes': [],
             'counts': {}, 'firstDivergentFields': {}, 'serverSubstitutedTicks': 0}
    if len(bundles) != 2:
        return {**empty, 'reason': 'Two bundles are required'}
    servers = [b for b in bundles if any(e.get('type') == 'postTick' and e.get('role') == 'server' for e in b['events'])]
    clients = [b for b in bundles if any(e.get('type') == 'postTick' and e.get('role') == 'owner' for e in b['events'])]
    if len(servers) != 1 or len(clients) != 1 or servers[0] is clients[0]:
        return {**empty, 'reason': 'Need exactly one bundle with server ticks and one with remote owner ticks'}
    server, client = servers[0], clients[0]
    server_ticks, client_ticks = _ticks(server['events'], 'server'), _ticks(client['events'], 'owner')
    labels = {id(b): sorted((e.get('sequence', 0), e.get('reason') or e.get('marker') or 'marker')
                            for e in b['events'] if e.get('type') in ('marker', 'markerReceived')
                            and e.get('marker') != 'capture-start') for b in (server, client)}

    # Tick mapping. The server stamps a tick with the input's own tick, so the
    # offset should be zero; the snapshot links measure it instead of assuming it.
    built = {}
    for event in server['events']:
        if event.get('type') == 'snapshotBuilt' and _state(event):
            built[(event.get('entity'), event.get('epoch'), event.get('sourceTick'))] = event
    offsets = collections.Counter()
    for event in client['events']:
        if event.get('type') == 'snapshotArrived' and event.get('role') == 'owner' and event.get('tick', -1) >= 0:
            source = built.get((event.get('entity'), event.get('epoch'), event.get('sourceTick')))
            frame = source['state'].get('lastInput', {}).get('frame', -1) if source else -1
            if frame >= 0:
                offsets[event['tick'] - frame] += 1
    offset = offsets.most_common(1)[0][0] if offsets else 0
    notes = []
    if not offsets:
        notes.append('Tick mapping unverified: no snapshot link carried a server input tick; offset 0 assumed')
    elif len(offsets) > 1:
        notes.append('Tick mapping is not constant across snapshot links; the most common offset is used')

    restores = collections.defaultdict(list)
    for event in client['events']:
        if event.get('type') == 'restored' and event.get('role') == 'owner':
            restores[(event.get('entity'), event.get('epoch'))].append(event)

    episodes, joined, divergent, substituted = [], 0, 0, 0
    for key in sorted(client_ticks, key=str):
        live = client_ticks[key].get(0, {})
        theirs = {tick + offset: records for tick, records in server_ticks.get(key, {}).get(0, {}).items()}
        if not live or not theirs:
            notes.append(f'Entity {key[0]} epoch {key[1]}: no overlapping server and owner ticks')
            continue
        low, high = max(min(live), min(theirs)), min(max(live), max(theirs))
        previous, open_episode = None, None
        for tick in range(low, high + 1):
            s, c = theirs.get(tick, []), live.get(tick, [])
            if not s and not c:
                continue
            joined += 1
            substituted += sum(1 for r in s if not r['created'])
            structural = None
            if not s: structural = 'The server never simulated this owner tick'
            elif not c: structural = 'The server simulated a tick the owner did not run'
            elif len(s) > 1: structural = 'The server simulated this tick more than once'
            elif len(c) > 1: structural = 'The owner ran this tick more than once on its first pass'
            post = [] if structural else differences(_sim(_state(s[0]['post'])), _sim(_state(c[0]['post'])))
            if not structural and not post:
                previous, open_episode = (s[0], c[0]), None
                continue
            divergent += 1
            if open_episode is not None:
                open_episode['lengthTicks'] += 1
                continue
            anchor = (s or c)[0]['post']
            episode = {'entity': key[0], 'epoch': key[1], 'tick': tick, 'serverTick': tick - offset, 'lengthTicks': 1,
                       'scenario': _scenario(labels[id(server if s else client)], anchor.get('sequence', 0)),
                       'firstFields': _fields(post), 'serverSubstituted': any(not r['created'] for r in s),
                       'server': evidence(s[0]['post']) if s else None, 'client': evidence(c[0]['post']) if c else None}
            if structural:
                episode.update({'class': 'A', 'reason': structural})
            else:
                episode.update(_classify_first_tick(s[0], c[0], previous, restores[key], client_ticks[key], theirs))
            episode['classification'] = CLASS_NAMES[episode['class']]
            episodes.append(episode)
            open_episode = episode
    counts = collections.defaultdict(collections.Counter)
    histogram = collections.Counter()
    for episode in episodes:
        counts[episode['class']][episode['scenario']] += 1
        histogram.update(episode['firstFields'])
    return {'status': 'joined' if joined else 'not applicable', 'joinedTicks': joined, 'divergentTicks': divergent,
            'serverSubstitutedTicks': substituted, 'tickOffset': offset,
            'tickOffsetDistribution': {str(k): v for k, v in sorted(offsets.items())}, 'notes': notes,
            'counts': {k: dict(v) for k, v in sorted(counts.items())},
            'firstDivergentFields': dict(histogram.most_common()), 'episodes': episodes}


def _classify_first_tick(s, c, previous, restores, client_passes, server_by_tick):
    inputs = differences(_controls(s['input']), _controls(c['input']))
    if inputs:
        reason = 'The server consumed a different input than the owner'
        if not s['created']:
            reason += '; the server had no input for this tick and substituted the held one'
        return {'class': 'A', 'reason': reason, 'inputDifferences': inputs}
    s_pre, c_pre = _state(s['pre']), _state(c['pre'])
    if s_pre is None or c_pre is None:
        return {'class': 'unclassified', 'reason': 'Pre-tick state was not captured on both sides'}
    pre = differences(_sim(s_pre), _sim(c_pre))
    if not pre:
        return {'class': 'B', 'reason': 'Same pre-tick bits and same input, different post-tick bits'}
    result = {'preFields': _fields(pre)}
    if previous is None:
        return {**result, 'class': 'unclassified', 'reason': 'Already divergent at the first joined tick'}
    after, before = previous[1]['post'].get('sequence', 0), c['pre'].get('sequence', 0)
    restore = next((r for r in reversed(restores) if after < r.get('sequence', 0) < before), None)
    if restore is None:
        moved = differences(_sim(_state(previous[0]['post'])), _sim(s_pre))
        side = 'server' if moved else 'owner'
        return {**result, 'class': 'unclassified',
                'reason': f'Pre-tick state changed on the {side} between ticks with no reconcile restore in between'}
    result['restore'] = evidence(restore)
    source = server_by_tick.get(restore.get('tick'), [])
    if len(source) == 1 and _state(restore):
        gap = differences(_sim(_state(source[0]['post'])), _sim(_state(restore)))
        if gap:
            names = _fields(gap)
            return {**result, 'class': 'C', 'restoreFields': names,
                    'notOnWire': [n for n in names if n.split('.')[0] not in WIRE_FIELDS],
                    'reason': 'The restored state differs from the server state at the same tick'}
    replay = client_passes.get(restore.get('pass'), {})
    for tick in sorted(replay):
        theirs = server_by_tick.get(tick, [])
        if len(theirs) != 1 or len(replay[tick]) != 1:
            continue
        a, b = theirs[0], replay[tick][0]
        replay_inputs = differences(_controls(a['input']), _controls(b['input']))
        if replay_inputs:
            return {**result, 'class': 'A', 'replayTick': tick, 'inputDifferences': replay_inputs,
                    'reason': 'A replayed tick used a different input than the server consumed'}
        if differences(_sim(_state(a['post'])), _sim(_state(b['post']))):
            same_start = not differences(_sim(_state(a['pre'])), _sim(_state(b['pre'])))
            return {**result, 'replayTick': tick, 'class': 'B' if same_start else 'C',
                    'reason': 'A replayed tick started from the same bits and input as the server and ended differently'
                    if same_start else 'A replayed tick started from different bits than the server after the restore'}
    return {**result, 'class': 'C', 'reason': 'Divergence first appears right after a reconcile restore'}


def analyze(bundles):
    manifests = [b['manifest'] for b in bundles]
    issues = []
    if len(bundles) != 2:
        issues.append('Exactly two bundles are required for paired evidence')
    if len({m['process'] for m in manifests}) != len(manifests):
        issues.append('Duplicate process bundle')
    for key in ('schema', 'session', 'build', 'unity', 'scene', 'tickRate', 'fixedDelta', 'configuration', 'revision', 'artifactHash', 'contentHash', 'backend', 'physics'):
        if len({json.dumps(m.get(key), sort_keys=True) for m in manifests}) != 1:
            issues.append(f'Incompatible {key}')
    for m in manifests:
        if not m.get('build') or m.get('revision') in (None, '', 'unknown'):
            issues.append('Build/source provenance is unverified')
        if m.get('artifactHash') in (None, '', 'unknown') or m.get('contentHash') in (None, '', 'unknown'):
            issues.append('Build artifact or scene content hash is unavailable')
        if len(manifests) == 2 and m.get('peer') not in {x['process'] for x in manifests if x != m}:
            issues.append('Peer roster does not match supplied bundles')
    findings, markers, overheads, snapshots = [], [], [], collections.defaultdict(list)
    for bundle in bundles:
        m = bundle['manifest']
        issues.extend(f'{m["process"]}: {g}' for g in bundle['gaps'])
        history = {}
        for event in bundle['events']:
            key = (event.get('entity'), event.get('epoch'), event.get('tick'), event.get('pass'))
            kind = event.get('type')
            state = event.get('state') if event.get('hasState', bool(event.get('state'))) else None
            if kind in ('marker', 'markerReceived'):
                markers.append({**evidence(event), 'id': event.get('marker'), 'note': event.get('reason'), 'receiveServerTick': event.get('serverTick')})
            if event.get('overhead'):
                overheads.append(event['overhead'] * 1000 / m['frequency'])
            if kind == 'snapshotBuilt':
                snapshots[(event.get('entity'), event.get('epoch'), event.get('sourceTick'))].append(event)
            if kind == 'predictedHistory':
                history[key] = event
            elif kind == 'authoritativeBaseline' and event.get('role') != 'proxy':
                predicted = history.get(key)
                if not predicted or not predicted.get('hasState', bool(predicted.get('state'))) or not predicted.get('state'):
                    findings.append({'classification': 'insufficient evidence', 'reason': 'Missing predicted history', **evidence(event)})
                elif state:
                    raw_a = {k: v for k, v in predicted['state'].items() if k in WIRE_FIELDS}
                    raw_b = {k: v for k, v in state.items() if k in WIRE_FIELDS}
                    diffs = differences(readable(raw_a), readable(raw_b))
                    controls_a = dict(predicted['state'].get('lastInput', {}))
                    controls_b = dict(state.get('lastInput', {}))
                    controls_a.pop('frame', None); controls_b.pop('frame', None)
                    input_diffs = differences(controls_a, controls_b)
                    raw_diffs = differences(raw_a, raw_b)
                    if input_diffs or diffs or raw_diffs:
                        findings.append({'classification': 'input/tick mismatch' if input_diffs else 'expected prediction',
                            'reason': 'Live prediction differs from authoritative baseline; cause unproven',
                            'differences': diffs, 'inputDifferences': input_diffs,
                            'rawBitDifferences': raw_diffs, **evidence(event)})
                history[('baseline',) + key] = event
            elif kind == 'restored':
                baseline = history.get(('baseline',) + key)
                if baseline and baseline.get('state') and state and baseline['state'] != state:
                    findings.append({'classification': 'restore/replay mismatch', 'reason': 'Restored values differ from supplied baseline',
                        'differences': differences(readable(baseline['state']), readable(state)), **evidence(event)})
            elif kind in ('inputRejected', 'reconcileRejected', 'captureFailure'):
                findings.append({'classification': 'insufficient evidence' if kind == 'captureFailure' else 'input/tick mismatch',
                                 'reason': event.get('reason'), **evidence(event)})
            if state:
                decoded = readable(state)
                def nonfinite(value):
                    if isinstance(value, dict): return any(nonfinite(v) for v in value.values())
                    return isinstance(value, float) and not math.isfinite(value)
                if nonfinite(decoded):
                    issues.append(f'Non-finite state at {m["process"]}:{event["_line"]}')
    matched, missing = 0, 0
    for bundle in bundles:
        for event in bundle['events']:
            if event.get('type') == 'snapshotArrived':
                candidates = snapshots.get((event.get('entity'), event.get('epoch'), event.get('sourceTick')), [])
                candidates = [x for x in candidates if x['_process'] != event['_process']]
                if len(candidates) == 1:
                    matched += 1
                else:
                    missing += 1
    if not matched:
        issues.append('No unambiguous cross-process snapshot identities matched')
    if missing:
        issues.append(f'{missing} received snapshots lack a unique captured source; not proof of transport loss')
    overheads.sort()
    summary = {'samples': len(overheads)}
    if overheads:
        summary.update(medianMs=overheads[len(overheads)//2], p95Ms=overheads[min(len(overheads)-1, int(len(overheads)*.95))], maxMs=max(overheads))
    # Cross-process clocks/ticks are not globally sorted. First is explicitly
    # capture order within the supplied process, not a claimed universal first.
    return {'schema': 1, 'scope': 'Live movement observations only; full-world replay and cross-machine determinism are unsupported',
            'compatibilityIssues': sorted(set(issues)), 'complete': not issues,
            'snapshotLinks': matched, 'unmatchedSnapshots': missing,
            'firstObservedDifference': next((f for f in findings if f.get('differences') or f.get('inputDifferences') or f.get('rawBitDifferences')), None),
            'findings': findings, 'divergence': classify_divergence(bundles), 'markers': markers, 'copyOverhead': summary,
            'performanceScope': 'DTO copy time only; excludes enqueue, GC, disk worker and control traffic; 2% performance gate unverified',
            'manifests': manifests}


def safe_json(value):
    if isinstance(value, dict): return {k: safe_json(v) for k, v in value.items()}
    if isinstance(value, list): return [safe_json(v) for v in value]
    if isinstance(value, float) and not math.isfinite(value): return str(value)
    return value


def write_report(result, destination):
    destination = pathlib.Path(destination)
    destination.mkdir(parents=True, exist_ok=True)
    result = safe_json(result)
    (destination / 'findings.json').write_text(json.dumps(result, indent=2, allow_nan=False), encoding='utf-8')
    esc = lambda v: html.escape(str(v))
    sections = ['<!doctype html><meta charset="utf-8"><title>LAN netcode capture</title>',
        '<style>body{font:15px system-ui;max-width:1200px;margin:40px auto;background:#101722;color:#e2e8f0}h1,h2{color:#67e8f9}pre{white-space:pre-wrap}td,th{padding:10px;border-bottom:1px solid #334155;text-align:left}a{color:#67e8f9}</style>',
        '<h1>LAN netcode capture</h1><p>' + esc(result['scope']) + '</p>',
        '<h2>Compatibility and coverage</h2><pre>' + esc(json.dumps(result['compatibilityIssues'], indent=2)) + '</pre>',
        '<p>Matched snapshot identities: ' + str(result['snapshotLinks']) + '</p>',
        '<h2>First observed difference (process capture order)</h2><pre>' + esc(json.dumps(result['firstObservedDifference'], indent=2)) + '</pre>',
        '<h2>Server vs owner, tick by tick</h2><p>A: input mismatch. B: sim nondeterminism. C: restore gap. '
        'Counts are per class and scenario; each episode is classified at its first divergent tick.</p><pre>'
        + esc(json.dumps({k: v for k, v in result['divergence'].items() if k != 'episodes'}, indent=2)) + '</pre>'
        '<details><summary>' + str(len(result['divergence']['episodes'])) + ' episodes</summary><pre>'
        + esc(json.dumps(result['divergence']['episodes'], indent=2)) + '</pre></details>',
        '<h2>Markers</h2><pre>' + esc(json.dumps(result['markers'], indent=2)) + '</pre>',
        '<h2>Recorder copy overhead</h2><pre>' + esc(json.dumps(result['copyOverhead'], indent=2)) + '</pre><p>' + esc(result['performanceScope']) + '</p>',
        '<h2>Findings</h2><table><tr><th>Class</th><th>Process / entity / tick / pass</th><th>Evidence</th></tr>']
    for f in result['findings']:
        sections.append('<tr><td>' + esc(f['classification']) + '</td><td>' + esc(f'{f.get("_process")} / {f.get("entity")} / {f.get("tick")} / {f.get("pass")}') + '</td><td><a href="' + esc(f['_raw']) + '">Raw line ' + str(f['_line']) + '</a><details><summary>' + esc(f['reason']) + '</summary><pre>' + esc(json.dumps(f, indent=2)) + '</pre></details></td></tr>')
    sections.append('</table><p>Game units are preserved; no metre conversion is assumed. Exact comparisons cover captured fields only.</p>')
    (destination / 'report.html').write_text('\n'.join(sections), encoding='utf-8')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('bundles', nargs='+', type=pathlib.Path)
    parser.add_argument('--out', type=pathlib.Path, required=True)
    parser.add_argument('--gate', action='store_true',
                        help='exit 2 on any class B or C episode, or when no ticks could be joined')
    args = parser.parse_args()
    result = analyze([load_bundle(p) for p in args.bundles])
    write_report(result, args.out)
    print(args.out / 'report.html')
    divergence = result['divergence']
    print('joined ticks:', divergence['joinedTicks'], '| divergent:', divergence['divergentTicks'],
          '| episodes by class:', {k: sum(v.values()) for k, v in divergence['counts'].items()})
    if args.gate and (not divergence['joinedTicks'] or any(k in divergence['counts'] for k in ('B', 'C'))):
        raise SystemExit(2)


if __name__ == '__main__':
    main()
