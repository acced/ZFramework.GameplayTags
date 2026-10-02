#!/usr/bin/env python3
from pathlib import Path
root=Path(__file__).resolve().parents[2]
code=(root/'Audit~/DenseBulk/collect.py').read_text().replace('dense-bulk-','copy-commit-').replace('source/Audit~/DenseBulk/review.py','source/Audit~/CopyCommit/review.py')
code=code.replace("if k not in ('tests','AA')","if k not in ('tests','faults','AA')")
exec(compile(code,str(root/'Audit~/DenseBulk/collect.py'),'exec'))
