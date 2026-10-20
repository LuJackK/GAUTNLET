"""Offline, dependency-free live capture analysis. Never grants a determinism verdict."""
import argparse
import collections
import html
import json
import math
import pathlib
import struct

FLOATS = set('forwardMove sideMove upMove surfaceFriction gravityFactor walkFactor verticalAxis horizontalAxis crouchLerp renderCrouchLerp maxStaminaBonus staminaRegenBonus stamina staminaRegenTimer dashTimer currentDashDuration dashCooldownTimer jumpTimer coyoteTimer slideSpeedCurrent slideDelay dashSlideTimer slideGraceTimer slopeLimit rigidbodyPushForce defaultHeight crouchingHeight crouchingSpeed fallingVelocity stepOffset meleeTimer meleeCooldownTimer meleeHoldTimer parryTimer parryCooldownTimer'.split())
VECTORS = set('viewTransformDefaultLocalPos origin viewAngles velocity slideDirection dashDirection ladderNormal ladderDirection ladderClimbDir ladderVelocity'.split())
# Fields actually restored from the reconcile payload, as opposed to fields
# inherited from local predicted history by BuildAuthoritativeMoveData.
WIRE_FIELDS = set('origin velocity stamina staminaRegenTimer dashTimer currentDashDuration dashCooldownTimer isDashing canAirDash jumpCount jumpTimer coyoteTimer dashDirection airDashing grounded groundedTemp jumping moveType forwardMove sideMove verticalAxis horizontalAxis surfaceFriction gravityFactor walkFactor fallingVelocity crouching crouchLerp renderCrouchLerp uncrouchDown sliding wasSliding slideSpeedCurrent slideDirection slideDelay dashSlideTimer slideGraceTimer dashStartedThisFrame doubleJumpedThisFrame meleeHitThisFrame meleeState meleeTimer meleeCooldownTimer meleeHoldTimer hasHitTarget meleeHitResolved meleeHitTargetObjectId meleeHitResolveTick isParrying parryTimer parryCooldownTimer parryStartedThisFrame parrySuccessThisFrame lastConsumedParryPressFrame lastConsumedJumpPressFrame lastConsumedDashPressFrame defaultHeight crouchingHeight crouchingSpeed slidingEnabled useStepOffset stepOffset maxStaminaBonus staminaRegenBonus'.split())


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
            'findings': findings, 'markers': markers, 'copyOverhead': summary,
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
    args = parser.parse_args()
    result = analyze([load_bundle(p) for p in args.bundles])
    write_report(result, args.out)
    print(args.out / 'report.html')


if __name__ == '__main__':
    main()
