#!/usr/bin/env python3
import hashlib,json
from pathlib import Path
HERE=Path(__file__).resolve().parent
manifest=json.loads((HERE/'MANIFEST.json').read_text())
errors=[]
for name,expected in manifest['Files'].items():
    p=HERE/name
    if not p.is_file() or hashlib.sha256(p.read_bytes()).hexdigest()!=expected:errors.append(name)
if errors:raise SystemExit('Package verification failed: '+', '.join(errors))
actual={str(p.relative_to(HERE)) for p in (HERE/'sources').glob('**/*.cs')}
expected={n for n in manifest['Files'] if n.startswith('sources/')}
if actual!=expected:raise SystemExit('Runtime source tree membership changed')
print('Verified '+str(len(manifest['Files']))+' frozen files; manifest SHA256 '+hashlib.sha256((HERE/'MANIFEST.json').read_bytes()).hexdigest())
