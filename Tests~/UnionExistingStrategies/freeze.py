#!/usr/bin/env python3
"""Create the package manifest before any measured execution; never rewrite after timing."""
import hashlib,json
from pathlib import Path
HERE=Path(__file__).resolve().parent
files=[HERE/name for name in ['Program.cs','Comparator.csproj','run.py','run-default.py','README.md','SCOPE.md','variants.json','source-provenance.json','freeze.py','verify.py']]
files+=sorted((HERE/'sources').glob('**/*.cs'))
manifest={'Schema':1,'Purpose':'Frozen supplemental existing-API strategies; no timing results or acceptance claim','Files':{str(p.relative_to(HERE)):hashlib.sha256(p.read_bytes()).hexdigest() for p in files}}
path=HERE/'MANIFEST.json'
if path.exists():raise SystemExit('Refusing to overwrite a frozen manifest; preserve/review prior freeze explicitly.')
path.write_text(json.dumps(manifest,indent=2)+'\n')
print(hashlib.sha256(path.read_bytes()).hexdigest())
