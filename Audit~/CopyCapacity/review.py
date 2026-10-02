#!/usr/bin/env python3
from pathlib import Path
root=Path(__file__).resolve().parents[2]
code=(root/'Audit~/CopyCommit/review.py').read_text().replace("'checkpoint'","'guarded'")
exec(compile(code,str(root/'Audit~/CopyCommit/review.py'),'exec'))
