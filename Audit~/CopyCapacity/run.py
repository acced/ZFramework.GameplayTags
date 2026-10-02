#!/usr/bin/env python3
from pathlib import Path
root=Path(__file__).resolve().parents[2]
code=(root/'Audit~/CopyCommit/run.py').read_text()
code=code.replace("'checkpoint'","'guarded'")
code=code.replace("HERE/'Faults.cs'","ROOT/'Audit~/CopyCommit/Faults.cs'")
code=code.replace("(HERE/'Extra.cs').read_bytes()","(ROOT/'Audit~/CopyCommit/Extra.cs').read_bytes()")
# Preserve the original BulkExtra redirection and all primary timers/input data.
tail="exec(compile(code,str(root/'Audit~/DenseBulk/run.py'),'exec'))"
assert code.count(tail)==1
code=code.replace(tail,"code=code.replace('*DirectArraySet:CopyDenseToMicro ', '*DirectArraySet:CopyDenseToMicro *DirectArraySet:CopyDenseGrowing ')\n"+tail,1)
exec(compile(code,str(root/'Audit~/CopyCommit/run.py'),'exec'))
