"""Compare saved metadata and measured layouts from one backend without executing targets.

Call compare(folder) separately for each backend's original/candidate pair. The
detailed mechanism-report.json is private; publish only the runner's allowlist.
"""
import copy
import json
from pathlib import Path

import metadata_compare as reused

OWNER = 'GameplayTags.GameplayTagContainerBinds'
RECORD = OWNER + '+BindData'
BIND = OWNER + '::Bind(GameplayTags.GameplayTag,System.Action`1<System.Boolean>):System.Void'
UNBIND = OWNER + '::UnbindAll():System.Void'
CHANGED = {BIND, UNBIND}
ADDITIONS = {
    BIND: ('call', 'GameplayTags.GameplayTag::get_RuntimeIndex():System.Int32'),
    UNBIND: ('newobj', 'GameplayTags.GameplayTag::.ctor(System.String,System.Int32):System.Void'),
}


def calls(body):
    return [(x['opcode'], x['operand']) for x in body['instructions']
            if x['opcode'] in ('call', 'callvirt', 'calli', 'newobj', 'newarr')]


def is_member(operand, owner, name):
    return (isinstance(operand, dict)
            and operand.get('declaring_type', '').endswith(']' + owner)
            and operand.get('name') == name)


def compare(folder):
    """Write and return a strict, untimed gate for one backend's saved pair."""
    folder = Path(folder)
    loaded = {v: reused._load(folder / (v + '.metadata.json'))
              for v in ('original', 'candidate')}
    snapshots = {v: value[0] for v, value in loaded.items()}
    types = {v: reused._index(s['types'], 'type') for v, s in snapshots.items()}
    methods = {v: reused._index([m for t in s['types'] for m in t['methods']], 'method')
               for v, s in snapshots.items()}
    failures = []

    def fail(check, reason, **details):
        failures.append({'check': check, 'reason': reason, **details})

    for variant in snapshots:
        for method in methods[variant].values():
            reused._validate_raw_body(method['body'], method['id'])
    if set(types['original']) != set(types['candidate']):
        fail('runtime_type_inventory', 'GameplayTags type inventory changed.',
             removed=sorted(types['original'].keys() - types['candidate'].keys()),
             added=sorted(types['candidate'].keys() - types['original'].keys()))
    if set(methods['original']) != set(methods['candidate']):
        fail('runtime_method_inventory', 'GameplayTags method inventory changed.',
             removed=sorted(methods['original'].keys() - methods['candidate'].keys()),
             added=sorted(methods['candidate'].keys() - methods['original'].keys()))

    a, b = copy.deepcopy(snapshots['original']), copy.deepcopy(snapshots['candidate'])
    old_record = next((t for t in a['types'] if t['id'] == RECORD), None)
    new_record = next((t for t in b['types'] if t['id'] == RECORD), None)
    for variant, record in (('original', old_record), ('candidate', new_record)):
        if record is None:
            fail('private_record', 'Missing BindData record.', variant=variant)
        elif (record['attributes'] & 7) != 3 or not record['base_type'].endswith(']System.ValueType'):
            fail('private_record', 'BindData must remain a nested private value type.', variant=variant)
        elif len(record['fields']) != 2:
            fail('record_fields', 'Expected exactly two BindData fields.', variant=variant)
    if old_record and new_record and old_record['fields'] and new_record['fields']:
        old_field, new_field = old_record['fields'][0], new_record['fields'][0]
        expected_tag = '[' + snapshots['original']['assembly'] + ']GameplayTags.GameplayTag'
        index_getter = methods['original'].get(ADDITIONS[BIND][1], {})
        expected_int = index_getter.get('signature', {}).get('return_type', '')
        old_ok = old_field['id'] == RECORD + '::Tag' and old_field['field_type'] == expected_tag
        new_ok = (new_field['id'] == RECORD + '::TagId'
                  and expected_int.endswith(']System.Int32')
                  and new_field['field_type'] == expected_int)
        if not old_ok:
            fail('original_record_field', 'Expected first field BindData.Tag:GameplayTag.')
        if not new_ok:
            fail('candidate_record_field', 'Expected first field BindData.TagId:System.Int32.')
        if old_ok and new_ok:
            new_field['id'], new_field['field_type'] = old_field['id'], old_field['field_type']

    for snapshot in (a, b):
        snapshot.pop('excluded_types', None)
        for item in snapshot['types']:
            for method in item['methods']:
                if method['id'] in CHANGED:
                    method['body'] = None
    differences = reused._differences(reused._normal(a), reused._normal(b))
    if differences:
        fail('runtime_metadata', 'GameplayTags metadata or unrelated normalized IL changed outside the exact allowlist.')

    raw_failures, raw_token_changes, signature_changes = [], [], []
    common_methods = sorted(methods['original'].keys() & methods['candidate'].keys())
    for name in common_methods:
        if name in CHANGED:
            continue
        operands, signatures, illegal = reused._raw_body_changes(
            methods['original'][name]['body'], methods['candidate'][name]['body'])
        if operands:
            raw_token_changes.append({'method': name, 'changes': operands})
        if signatures:
            signature_changes.append({'method': name, 'changes': signatures})
        if illegal:
            raw_failures.append({'method': name, 'changes': illegal})
    if raw_failures:
        fail('unrelated_raw_il', 'Unrelated raw IL changed outside resolved-identical token encodings.')
    tokens = reused._token_changes(a, b)
    definition_movements = [x for x in tokens
                            if '.body.' not in x['path'] and x['path'].endswith('.metadata_token')]
    if definition_movements:
        fail('definition_tokens', 'Existing GameplayTags definition token moved.', changes=definition_movements)

    call_reports = {}
    for name in sorted(CHANGED):
        if any(name not in methods[v] or methods[v][name]['body'] is None for v in snapshots):
            fail('required_method_body', 'Missing scoped method or IL body.', method=name)
            continue
        old, new = methods['original'][name]['body'], methods['candidate'][name]['body']
        before, after = calls(old), calls(new)
        expected_opcode, expected_id = ADDITIONS[name]
        expected_definition = methods['candidate'].get(expected_id)
        expected = (expected_opcode, expected_definition['signature']) if expected_definition else None
        additions = [entry for entry in after if entry == expected]
        filtered = [entry for entry in after if entry != expected]
        if len(additions) != 1 or filtered != before:
            fail('changed_method_calls', 'Expected exactly the specified added call/value constructor and unchanged remaining call/allocation order.',
                 method=name, expected_opcode=expected_opcode, expected_method=expected_id)
        if (old['locals'] != new['locals'] or old['local_signature'] != new['local_signature']
                or old['exception_regions'] != new['exception_regions']):
            fail('locals_and_eh', 'Changed locals, local signature, or exception regions.', method=name)
        prohibited = [x['opcode'] for x in new['instructions'] if x['opcode'] in ('box', 'newarr')]
        if prohibited:
            fail('boxing_or_array', 'Boxing or newarr occurs in a changed method.', method=name, opcodes=prohibited)
        if any(isinstance(member, dict) and 'GameplayTagManager' in member.get('declaring_type', '')
               for _, member in after):
            fail('manager_lookup', 'Manager lookup occurs in a changed method.', method=name)
        delegate_order = {}
        if name == BIND:
            for variant, body in (('original', old), ('candidate', new)):
                delegates = [i for i, x in enumerate(body['instructions'])
                             if x['opcode'] == 'newobj'
                             and is_member(x['operand'], 'GameplayTags.OnTagCountChangedDelegate', '.ctor')]
                count_stores = [i for i, x in enumerate(body['instructions'])
                                if x['opcode'] == 'stfld'
                                and is_member(x['operand'], OWNER + '+BindState', 'Count')]
                order_ok = len(delegates) == 2 and len(count_stores) == 1 and count_stores[0] < delegates[0]
                delegate_order[variant] = {'delegate_constructions': len(delegates),
                                           'count_writes': len(count_stores),
                                           'count_write_before_delegates': order_ok}
                if not order_ok:
                    fail('delegates_and_count_order', 'Expected two delegate constructions after one Count write.', variant=variant)
        call_reports[name] = {'old_il_bytes': old['il_length'], 'new_il_bytes': new['il_length'],
                              'single_expected_addition': additions, 'delegate_order': delegate_order}

    layouts = {v: json.loads((folder / (v + '.layout.json')).read_text(encoding='utf-8'))
               for v in snapshots}
    layout_fields = ('PointerSize', 'GameplayTagSize', 'BindDataSize')
    layout_valid = True
    for variant, layout in layouts.items():
        if not isinstance(layout, dict) or any(type(layout.get(key)) is not int for key in layout_fields):
            layout_valid = False
            fail('layout_shape', 'Measured layout requires integer PointerSize, GameplayTagSize, and BindDataSize.', variant=variant)
    if layout_valid:
        old_layout, new_layout = layouts['original'], layouts['candidate']
        if old_layout['PointerSize'] != 8 or new_layout['PointerSize'] != 8:
            fail('pointer_size', 'Both measured pointer sizes must be 8.',
                 original=old_layout['PointerSize'], candidate=new_layout['PointerSize'])
        if not (old_layout['GameplayTagSize'] == new_layout['GameplayTagSize'] > 0):
            fail('tag_size', 'Measured GameplayTag sizes must be equal and positive within this backend.',
                 original=old_layout['GameplayTagSize'], candidate=new_layout['GameplayTagSize'])
        if not (old_layout['BindDataSize'] > new_layout['BindDataSize'] > 0):
            fail('record_size', 'Measured BindData size must strictly decrease to a positive size within this backend.',
                 original=old_layout['BindDataSize'], candidate=new_layout['BindDataSize'])

    report = {
        'schema': 'binding-record-id-mechanism-v2',
        'status': 'FAIL' if failures else 'PASS',
        'decision': 'STOP' if failures else 'UNTIMED_GATE_PASS',
        'failures': failures,
        'metadata_sha256': {v: value[1] for v, value in loaded.items()},
        'types_compared': len(types['original'].keys() & types['candidate'].keys()),
        'methods_compared': len(common_methods),
        'unrelated_methods_compared': len(set(common_methods) - CHANGED),
        'type_counts': {v: len(items) for v, items in types.items()},
        'method_counts': {v: len(items) for v, items in methods.items()},
        'method_il_lengths': {name: {v: (methods[v][name]['body'] or {}).get('il_length')
                                    for v in snapshots} for name in common_methods},
        'metadata_differences': differences,
        'unrelated_raw_il_failures': raw_failures,
        'unrelated_resolved_identical_token_changes': raw_token_changes,
        'unrelated_local_signature_encoding_changes': signature_changes,
        'token_changes': tokens,
        'changed_calls': call_reports,
        'managed_layout': layouts,
        'intentional_private_graph_change': 'BindData.Tag (GameplayTag/name reference plus ID) becomes BindData.TagId (int). Arbitrary mutable names need not stay rooted; WeakReference observations may differ. Subscription ownership, callback identity and release remain the contract.',
        'limitations': [
            'This report compares only the supplied original/candidate pair within one backend; it does not compare or assume layouts across backends.',
            'No elapsed-time, allocation-counter, whole-GC retained-byte, or first-call latency measurement is performed by this comparator.',
            'Global compiler PrivateImplementationDetails, PE RVAs/MVID and absent conditional builds are outside reused metadata scope.',
        ],
        'performance_authorized': False,
        'performance_claim': None,
    }
    with (folder / 'mechanism-report.json').open('x', encoding='utf-8') as stream:
        json.dump(report, stream, indent=2)
        stream.write('\n')
    return report
