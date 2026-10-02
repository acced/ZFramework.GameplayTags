#!/usr/bin/env python3
from pathlib import Path
root=Path(__file__).resolve().parents[2]
code=(root/'Audit~/DenseBulk/collect.py').read_text()
code=code.replace('dense-bulk-','dense-cache-').replace('Audit~/DenseBulk/review.py','Audit~/DenseCache/review.py')
exec(compile(code,str(root/'Audit~/DenseBulk/collect.py'),'exec'))
