#!/usr/bin/env python3
from pathlib import Path
root=Path(__file__).resolve().parents[2]
code=(root/'Audit~/DenseBulk/collect.py').read_text()
code=code.replace('dense-bulk-', 'dense-runs-').replace('Audit~/DenseBulk/review.py','Audit~/DenseRuns/review.py')
marker="        before=json.loads((results/'recheck.json').read_text())"
assert marker in code
code=code.replace(marker,"""        faults=json.loads((results/'FaultResults.json').read_text())
        for name,check in faults.items():
            assert check['failed']==0 and check['cases']==225 and check['injectedExceptions']>0
            assert check['maximumRecords']==(65536 if name in ('rotate','buffer32') else 0)
            assert check['changedFailures']==0 and check['changedCases']==(180 if name in ('rotate','buffer32') else 0)
            assert check['core_sha256']==hashlib.sha256((results/'generated'/name/'DirectArraySet.cs').read_bytes()).hexdigest()
"""+marker,1)
exec(compile(code,str(root/'Audit~/DenseBulk/collect.py'),'exec'))
