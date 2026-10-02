#!/usr/bin/env python3
"""Redownload both originals and rerun the new experiment's verifier."""
from pathlib import Path
root=Path(__file__).resolve().parents[2]
code=(root/'Audit~/DenseBulk/collect.py').read_text()
code=code.replace('dense-bulk-', 'dense-bounded-')
code=code.replace('Audit~/DenseBulk/review.py','Audit~/DenseBounded/review.py')
exec(compile(code,str(root/'Audit~/DenseBulk/collect.py'),'exec'))
