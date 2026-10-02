#!/usr/bin/env python3
from pathlib import Path
import subprocess,sys
root=Path(__file__).resolve().parents[2]
code=(root/'Audit~/DenseBulk/collect.py').read_text().replace('dense-bulk-','dense-stream-').replace('Audit~/DenseBulk/review.py','Audit~/DenseStream/review.py')
exec(compile(code,str(root/'Audit~/DenseBulk/collect.py'),'exec'))
subprocess.check_call(['python3',str(root/'Audit~/DenseStream/report.py'),sys.argv[1]])
