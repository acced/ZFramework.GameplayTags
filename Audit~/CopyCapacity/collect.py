#!/usr/bin/env python3
from pathlib import Path
root=Path(__file__).resolve().parents[2]
code=(root/'Audit~/CopyCommit/collect.py').read_text().replace('copy-commit-','copy-capacity-').replace('source/Audit~/CopyCommit/review.py','source/Audit~/CopyCapacity/review.py')
exec(compile(code,str(root/'Audit~/CopyCommit/collect.py'),'exec'))
