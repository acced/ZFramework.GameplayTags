"""Strict portable Runtime metadata/Init IL gate. No target or native execution.

Only the preserved expected-body framework qualifier is adapted for Mono.
All observed metadata is compared within its backend without name normalization.
Reports contain private metadata and must never be uploaded.
"""
from collections import Counter
import copy, json
from pathlib import Path
import metadata_compare as reused
OWNER = 'GameplayTags.GameplayTagManager'
CHANGED = OWNER + '::InitializeIfNeeded():System.Void'
NET_CORE = '[System.Private.CoreLib, Version=8.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e]'
MONO_CORE = '[mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089]'
TEST = '[Test, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null]'


def calls(body):
    return [(i['opcode'], reused._normal(i['operand'])) for i in body['instructions']
            if i['opcode'] in ('call','callvirt','calli','newobj')]


def _expected_qualifier(value, backend):
    if backend not in ('net', 'mono'): raise ValueError('Unsupported backend')
    if isinstance(value, dict): return {k:_expected_qualifier(v,backend) for k,v in value.items()}
    if isinstance(value, list): return [_expected_qualifier(v,backend) for v in value]
    if type(value) is str and backend == 'mono': return value.replace(NET_CORE,MONO_CORE)
    return value


def changed_body_gate(bodies, failures, backend):
    CORE = NET_CORE if backend == 'net' else MONO_CORE
    if backend not in ('net','mono'): raise ValueError('Unsupported backend')
    expected = _expected_qualifier(json.loads((Path(__file__).parent/'f7-initialize-body.json').read_text()), backend)
    if reused._normal(bodies['original']) != reused._normal(expected): failures.append('Original InitializeIfNeeded differs from preserved exact-f7 normalized body')
    old = calls(bodies['original']); new = calls(bodies['candidate'])
    removed_owners = {
        CORE+'System.Reflection.CustomAttributeExtensions',
        CORE+'System.Collections.Generic.IEnumerable`1<'+TEST+'GameplayTags.GameplayTagAttribute>',
        CORE+'System.Collections.Generic.IEnumerator`1<'+TEST+'GameplayTags.GameplayTagAttribute>',
        CORE+'System.Collections.IEnumerator', CORE+'System.IDisposable'}
    retained_old = [c for c in old if c[1]['declaring_type'] not in removed_owners]
    additions = [c for c in new if c[1]['declaring_type'] in (CORE+'System.Type',CORE+'System.Attribute')]
    retained_new = [c for c in new if c not in additions]
    if retained_old != retained_new: failures.append('Changed non-enumerator call sites, signatures or call order')
    expected_calls=[]
    for call in old:
        if call[1]['declaring_type'] == CORE+'System.Reflection.CustomAttributeExtensions': expected_calls.extend(additions)
        elif call[1]['declaring_type'] not in removed_owners: expected_calls.append(call)
    if expected_calls != new: failures.append('New reflection calls are not at the original discovery position')
    if len(additions) != 2: failures.append('Expected exactly Type.GetTypeFromHandle and static Attribute.GetCustomAttributes additions')
    else:
        type_call, attribute_call = additions
        if type_call[0] != 'call' or type_call[1]['name'] != 'GetTypeFromHandle' or not type_call[1]['static'] or [p['type'] for p in type_call[1]['parameters']] != [CORE+'System.RuntimeTypeHandle'] or type_call[1]['return_type'] != CORE+'System.Type': failures.append('Unexpected typeof call signature')
        if attribute_call[0] != 'call' or attribute_call[1]['name'] != 'GetCustomAttributes' or not attribute_call[1]['static'] or attribute_call[1]['generic_arguments'] or [p['type'] for p in attribute_call[1]['parameters']] != [CORE+'System.Reflection.Assembly',CORE+'System.Type'] or attribute_call[1]['return_type'] != CORE+'System.Attribute[]': failures.append('Candidate is not exact two-argument static Assembly API')
    if any(c[1]['declaring_type'] in removed_owners for c in new): failures.append('Candidate retains enumerator/generic extension calls')
    old_removed = Counter(c[1]['name'] for c in old if c[1]['declaring_type'] in removed_owners)
    if old_removed != Counter({'GetCustomAttributes':1,'GetEnumerator':1,'get_Current':1,'MoveNext':1,'Dispose':1}): failures.append('Original enumeration call inventory differs')
    for variant, body in bodies.items():
        instructions = body['instructions']; ops = Counter(i['opcode'] for i in instructions)
        if not body['init_locals']: failures.append('Initialization locals flag changed: '+variant)
        if any(i['opcode'] in ('box','newarr','localloc','calli','stfld') for i in instructions): failures.append('Unexpected direct allocation/boxing/store/indirect call: '+variant)
        stores = [i['operand'] for i in instructions if i['opcode']=='stsfld']
        if len(stores) != 1 or stores[0]['declaring_type'] != TEST+OWNER+'+<>c' or stores[0]['name'] != '<>9__17_0': failures.append('Unchanged lazy assembly-comparison cache store not preserved: '+variant)
        loads = [i for i in instructions if i['opcode']=='ldsfld' and i['operand']['name']=='s_IsInitialized']
        if len(loads)!=2 or ops['volatile.']!=2: failures.append('Two volatile publication checks not preserved: '+variant)
        eh = body['exception_regions']
        if len(eh)!=(2 if variant=='original' else 1) or any(e['flags']!=2 or e['catch_type'] is not None or e['filter_offset'] is not None for e in eh): failures.append('Only enumerator finally may disappear: '+variant)
        # The outer Monitor.Exit handler remains identical, ignoring only its relocation.
        outer = max(eh,key=lambda e:e['try_length'])
        handler = [i for i in instructions if outer['handler_offset'] <= i['offset'] < outer['handler_offset']+outer['handler_length']]
        expected_ops = ['ldloc.1','brfalse.s','ldloc.0','call','endfinally']
        if [i['opcode'] for i in handler] != expected_ops or handler[3]['operand']['declaring_type'] != CORE+'System.Threading.Monitor' or handler[3]['operand']['name']!='Exit' or handler[1]['operand'] != handler[-1]['offset']: failures.append('Monitor lock release handler differs: '+variant)
    candidate = bodies['candidate']; instr = candidate['instructions']
    tokens = [i['operand'] for i in instr if i['opcode']=='ldtoken']
    if tokens != [{'kind':'type','type':TEST+'GameplayTags.GameplayTagAttribute'}]: failures.append('Expected sole GameplayTagAttribute typeof token')
    casts = [i['operand'] for i in instr if i['opcode']=='castclass']
    if len(casts)!=1 or casts[0] != {'kind':'type','type':TEST+'GameplayTags.GameplayTagAttribute[]'}: failures.append('Expected sole exact typed-array cast')
    locals_ = candidate['locals']; expected_types = [CORE+'System.Object', CORE+'System.Boolean', TEST+'GameplayTags.GameplayTagRegistrationContext', CORE+'System.Reflection.Assembly[]', CORE+'System.Int32', TEST+'GameplayTags.GameplayTagAttribute[]', CORE+'System.Int32', TEST+'GameplayTags.GameplayTagAttribute']
    if [x['type'] for x in locals_] != expected_types or any(x['pinned'] for x in locals_): failures.append('Candidate local layout is not array/index/declaration lowering')
    if sum(i['opcode']=='ldlen' for i in instr)!=2 or sum(i['opcode']=='ldelem.ref' for i in instr)!=2: failures.append('Expected assembly and declaration array loops')


def compare(folder, backend):
    if backend not in ('net','mono'): raise ValueError('Unsupported backend')
    folder = Path(folder); failures=[]; reports={}; metadata_differences=[]; raw_failures=[]; token_changes=[]
    type_count=method_count=None
    for mode in ('empty','retry'):
        snapshots = {v:reused._load(folder/(mode+'-'+v+'.metadata.json'))[0] for v in ('original','candidate')}
        methods = {v:{m['id']:m for t in snap['types'] for m in t['methods']} for v,snap in snapshots.items()}
        for v in snapshots:
            for m in methods[v].values(): reused._validate_raw_body(m['body'],m['id'])
        if set(methods['original']) != set(methods['candidate']): failures.append(mode+': Runtime method inventory changed')
        a,b = copy.deepcopy(snapshots['original']),copy.deepcopy(snapshots['candidate'])
        for snap in (a,b):
            for t in snap['types']:
                for m in t['methods']:
                    if m['id'] == CHANGED: m['body'] = None
        differences = reused._differences(reused._normal(a),reused._normal(b))
        if differences: failures.append(mode+': Runtime metadata/unrelated body changed outside sole InitializeIfNeeded body')
        raw=[]
        for name in methods['original'].keys() & methods['candidate'].keys():
            if name == CHANGED: continue
            _,_,illegal = reused._raw_body_changes(methods['original'][name]['body'],methods['candidate'][name]['body'])
            if illegal: raw.append({'method':name,'changes':illegal})
        if raw: failures.append(mode+': unrelated raw IL changed beyond resolved-identical tokens')
        tokens = reused._token_changes(a,b)
        if any('.body.' not in x['path'] and x['path'].endswith('.metadata_token') for x in tokens): failures.append(mode+': existing Runtime definition token moved')
        bodies={v:methods[v][CHANGED]['body'] for v in snapshots}; local_failures=[]
        changed_body_gate(bodies,local_failures,backend); failures += [mode+': '+x for x in local_failures]
        reports[mode]={'metadata_differences':differences,'unrelated_raw_il_failures':raw,'token_changes':tokens,'changed_bodies':bodies,'changed_body_failures':local_failures}
        metadata_differences.extend(differences); raw_failures.extend(raw); token_changes.extend(tokens)
        counts=(len(a['types']),len(methods['original']))
        if type_count is not None and counts != (type_count,method_count): failures.append('Runtime inventory varies between modes')
        type_count,method_count=counts
    for v in ('original','candidate'):
        if (folder/('empty-'+v+'.metadata.json')).read_bytes() != (folder/('retry-'+v+'.metadata.json')).read_bytes(): failures.append('Runtime metadata varies between modes: '+v)
    report={'schema':'assembly-discovery-array-portable-metadata-v1','status':'FAIL' if failures else 'PASS',
        'decision':'STOP' if failures else 'REVIEW_ONLY','backend':backend,'failures':failures,
        'types_compared':type_count,'methods_compared':method_count,'metadata_differences':metadata_differences,
        'unrelated_raw_il_failures':raw_failures,'token_changes':token_changes,'modes':reports,
        'native_capture':False,'expected_template_qualifier_only':True,'generated_name_normalization':False}
    with (folder/'mechanism-report.json').open('x') as f: json.dump(report,f,indent=2);f.write('\n')
    return report
