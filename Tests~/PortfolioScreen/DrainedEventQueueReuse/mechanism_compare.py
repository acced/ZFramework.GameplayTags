"""Inspect persisted semantic/metadata/queue evidence only. Never execute target code."""
from collections import Counter
import copy, json
from pathlib import Path
import metadata_compare as reused
OWNER = 'GameplayTags.GameplayTagCountContainer'
CHANGED = OWNER + '::Enqueue(System.Int32,System.Int32,GameplayTags.OnTagCountChangedDelegate):System.Void'
DECLARATION = 'Only Enqueue body differs; private queue indices/capacities/array identities and resource-failure behavior intentionally differ; public traces and logical pending suffix compare exactly\n'

def compare(folder):
    folder = Path(folder)
    snapshots = {v:reused._load(folder/(v+'.metadata.json'))[0] for v in ('original','candidate')}
    methods = {v:{m['id']:m for t in s['types'] for m in t['methods']} for v,s in snapshots.items()}
    failures = []
    for v in snapshots:
        for m in methods[v].values(): reused._validate_raw_body(m['body'],m['id'])
    if set(methods['original']) != set(methods['candidate']): failures.append('Runtime method inventory changed')
    a,b = copy.deepcopy(snapshots['original']),copy.deepcopy(snapshots['candidate'])
    for s in (a,b):
        for t in s['types']:
            for m in t['methods']:
                if m['id'] == CHANGED: m['body'] = None
    differences = reused._differences(reused._normal(a),reused._normal(b))
    if differences: failures.append('Runtime metadata/unrelated body changed outside sole Enqueue body')
    raw = []
    for name in methods['original'].keys() & methods['candidate'].keys():
        if name == CHANGED: continue
        _,_,illegal = reused._raw_body_changes(methods['original'][name]['body'],methods['candidate'][name]['body'])
        if illegal: raw.append({'method':name,'changes':illegal})
    if raw: failures.append('Unrelated raw IL changed beyond resolved-identical tokens')
    tokens = reused._token_changes(a,b)
    if any('.body.' not in x['path'] and x['path'].endswith('.metadata_token') for x in tokens): failures.append('Existing Runtime definition token moved')
    bodies = {v:methods[v][CHANGED]['body'] for v in snapshots}
    if bodies['original']['il_raw'] == bodies['candidate']['il_raw']: failures.append('Expected Enqueue change missing')
    for key in ('init_locals','locals','exception_regions'):
        if bodies['original'][key] != bodies['candidate'][key]: failures.append('Unexpected changed-body ' + key)
    calls = {v:[(i['opcode'],reused._normal(i['operand'])) for i in body['instructions'] if i['opcode'] in ('call','callvirt','calli','newobj')] for v,body in bodies.items()}
    if calls['original'] != calls['candidate']: failures.append('Resize/notification-constructor call sites changed')
    stores = {v:Counter(i['operand'].get('name') for i in body['instructions'] if i['opcode'] in ('stfld','stsfld')) for v,body in bodies.items()}
    if stores['original'] != Counter({'m_EventCount':1}) or stores['candidate'] != Counter({'m_EventCount':2,'m_NextEvent':1}): failures.append('Unexpected queue-store sites')
    for v,body in bodies.items():
        if any(i['opcode'] in ('box','newarr','localloc','calli','stsfld') for i in body['instructions']): failures.append('Unexpected allocation/boxing/indirect call/static store in '+v)
        if (folder/(v+'.layout.json')).read_text() != DECLARATION: failures.append('Representation declaration missing: '+v)
    # These are descriptive physical observations, deliberately not byte-compared.
    queue_rows = {}
    for v in snapshots:
        lines = (folder/(v+'.raw-queue.txt')).read_text().splitlines()
        if not lines or lines[0] != 'label|capacity|next|count|batch|dispatching|pending': failures.append('Raw queue header missing: '+v)
        rows = []
        for line in lines[1:]:
            p=line.split('|')
            if len(p)!=7: raise ValueError('Malformed queue observation')
            row={'label':p[0],'capacity':int(p[1]),'next':int(p[2]),'count':int(p[3]),'batch':int(p[4]),'dispatching':p[5],'pending':int(p[6])}
            if not 0 <= row['next'] <= row['count'] <= row['capacity'] or row['pending'] != row['count']-row['next']: failures.append('Raw queue range mismatch: '+v)
            rows.append(row)
        queue_rows[v]=rows
    if not queue_rows['original'] or [r['label'] for r in queue_rows['original']] != [r['label'] for r in queue_rows['candidate']]: failures.append('Queue observation inventory mismatch')
    for old,new in zip(queue_rows['original'],queue_rows['candidate']):
        if any(old[k]!=new[k] for k in ('label','batch','dispatching','pending')): failures.append('Logical queue ownership observation mismatch')
    layouts = {v:json.loads((folder/(v+'.managed-layout.json')).read_text()) for v in snapshots}
    keys = {'PointerSize','GameplayTagSize','DeferredEventSize'}
    for v,layout in layouts.items():
        if set(layout) != keys or any(type(layout[k]) is not int or layout[k] <= 0 for k in keys) or layout['PointerSize'] != 8:
            failures.append('Invalid actual managed layout: '+v)
    if layouts['original'] != layouts['candidate']: failures.append('Unchanged managed value layouts differ')
    report={'schema':'drained-event-queue-reuse-portable-mechanism-v1','status':'FAIL' if failures else 'PASS',
        'failures':failures,'metadata_differences':differences,'unrelated_raw_il_failures':raw,'token_changes':tokens,'changed_bodies':bodies,
        'types_compared':len(snapshots['original']['types']),'methods_compared':len(methods['original']),
        'managed_layout':layouts,'queue_observations':queue_rows,'native_capture_performed':False,
        'limitations':['Logical pending/public traces and full storage/callback state compare; physical queue capacity/indices/identity and resource failures intentionally differ.',
            'Portable combined assembly context; no native or inlining portability claim. Earlier local natural capture stays separate.',
            'Reused R2 collector excludes compiler global PrivateImplementationDetails, PE RVAs/MVID and absent conditionals.']}
    with (folder/'mechanism-report.json').open('x') as f: json.dump(report,f,indent=2); f.write('\n')
    return report
