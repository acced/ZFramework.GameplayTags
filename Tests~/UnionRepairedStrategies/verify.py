#!/usr/bin/env python3
"""Verify immutable package, exact control source identities, and frozen maps without executing code."""
import hashlib,json
from pathlib import Path
HERE=Path(__file__).resolve().parent

def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()
def load(path):return json.loads(path.read_text())
def verify():
    manifest=load(HERE/'MANIFEST.json')
    for name,expected in manifest['Files'].items():
        if not (HERE/name).is_file() or sha(HERE/name)!=expected:raise RuntimeError('Frozen package changed: '+name)
    for folder,pattern in [('sources','**/*.cs'),('maps','*.tsv')]:
        actual={str(p.relative_to(HERE))for p in(HERE/folder).glob(pattern)if p.is_file()}
        expected={p for p in manifest['Files']if p.startswith(folder+'/') and(p.endswith('.cs')if folder=='sources'else p.endswith('.tsv'))}
        if actual!=expected:raise RuntimeError('Frozen source/map membership changed')
    identities=load(HERE/'source-identities.json')
    for source,identity in identities.items():
        hashes={str(p.relative_to(HERE/'sources'/source)):sha(p)for p in sorted((HERE/'sources'/source/'Runtime').glob('**/*.cs'))}
        if hashes!=identity['RuntimeFileHashes']:raise RuntimeError('Runtime identity changed')
        tree=hashlib.sha256(json.dumps(hashes,sort_keys=True,separators=(',',':')).encode()).hexdigest()
        if tree!=identity['RuntimeTreeSHA256']:raise RuntimeError('Runtime tree hash mismatch')
        if 'RuntimeAllFileHashes'in identity:
            all_hashes={str(p.relative_to(HERE/'sources'/source)):sha(p)for p in sorted((HERE/'sources'/source/'Runtime').glob('**/*'))if p.is_file()}
            if all_hashes!=identity['RuntimeAllFileHashes']:raise RuntimeError('Full Runtime file-set/content changed')
            all_tree=hashlib.sha256(json.dumps(all_hashes,sort_keys=True,separators=(',',':')).encode()).hexdigest()
            if all_tree!=identity['RuntimeAllFilesTreeSHA256']:raise RuntimeError('Full Runtime tree hash mismatch')
    provenance=load(HERE/'provenance.json')
    if sha(HERE/'candidate.patch')!=provenance['CandidatePatchSHA256']:raise RuntimeError('Candidate patch mismatch')
    expected_rows=load(HERE/'expected-rows.json');timed=[r['Id']for r in expected_rows if not r['Cohort']]
    if len(timed)!=45 or len(expected_rows)!=49:raise RuntimeError('Frozen traversal differs')
    for backend in ['net','mono']:
        p=HERE/'maps'/f'{backend}.tsv'
        if sha(p)!=provenance['BalancedWarmV1']['Maps'][backend]['SHA256']:raise RuntimeError('Balanced map changed')
        mapping={}
        for line in p.read_text().splitlines():
            key,value=line.split('\t')
            if key in mapping:raise RuntimeError('Duplicate map row')
            mapping[key]=int(value)
        if list(mapping)!=timed:raise RuntimeError('Map row order/membership differs')
        for key,value in mapping.items():
            cap={128:4096,1024:64,3000:8}[int(key.split('/')[1][1:])]if key.startswith('deep/')else 50000000
            if value<=0 or value>cap:raise RuntimeError('Iteration bound exceeded')
    return manifest
if __name__=='__main__':
    m=verify();print('Verified '+str(len(m['Files']))+' immutable files; manifest SHA256 '+sha(HERE/'MANIFEST.json')+'; no code execution.')
