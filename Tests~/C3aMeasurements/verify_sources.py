#!/usr/bin/env python3
"""Read-only source pin check, shared by build preparation and frozen collection."""
import hashlib,json,pathlib
HERE=pathlib.Path(__file__).resolve().parent
REPO=HERE.parent.parent
def sha(p):return hashlib.sha256(pathlib.Path(p).read_bytes()).hexdigest()
def inventory():
    pins=json.loads((HERE/'source_pins.json').read_text())
    result={}
    for version,entry in pins['versions'].items():
        root=(REPO/entry['runtime_path']).resolve()
        actual={p.name:sha(p) for p in sorted(root.iterdir()) if p.is_file()}
        if actual!=entry['files']:raise SystemExit('Source pin mismatch: '+version)
        result[version]={'runtime_path':str(root),'source_id':entry['source_id'],'files':actual}
    snapshot=(REPO/pins['candidate_snapshot']).resolve()
    if {p.name:sha(p) for p in sorted(snapshot.iterdir()) if p.is_file()}!=result['C3a']['files']:
        raise SystemExit('Candidate source differs from its pinned snapshot')
    return result
if __name__=='__main__':print(json.dumps(inventory(),indent=2))
