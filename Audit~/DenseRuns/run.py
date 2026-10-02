#!/usr/bin/env python3
"""Reuse the pinned full correctness protocol; isolate focused append timing."""
from pathlib import Path
root=Path(__file__).resolve().parents[2]
code=(root/'Audit~/DenseBulk/run.py').read_text()
code=code.replace("HERE/'Extra.cs'","ROOT/'Audit~/DenseBulk/Extra.cs'")
code=code.replace("dlls['combined']","dlls['buffer32']").replace("files['combined']","files['buffer32']")
code=code.replace("'combined','tests'","'buffer32','tests'")
anchor="    probefile=out/'generated/Probe.cs';probefile.write_text(probe)"
assert code.count(anchor)==1
code=code.replace(anchor,"""    probe=probe.replace('var bulkTests=BulkTests();','var bulkTests=BulkTests(); var streamTests=StreamingTests(); var runTests=RunMergeTests();').replace('bulkTests,localTests,','runTests,streamTests,bulkTests,localTests,')
    probe=probe.replace('if(stage=="scale") AppendScale();', 'if(stage=="focus") FocusAppend(); else if(stage=="holdout") HoldoutAppend(); else if(stage=="scale") AppendScale();')
"""+anchor,1)
anchor="    common=list((ROOT/'Runtime').rglob('*.cs'))"
assert code.count(anchor)==1
code=code.replace(anchor,"""    for original,name in ((ROOT/'Audit~/DenseStream/Extra.cs','StreamExtra.cs'),(HERE/'Extra.cs','RunsExtra.cs')):
        dest=out/'generated'/name;dest.write_bytes(original.read_bytes());extras.append(dest)
"""+anchor,1)
# Focus uses EXACT primary append inputs/capacities, not the old reduced smoke data.
code=code.replace("for stage in ('bench','pairs','scale')","for stage in ('focus','scale','holdout')")
code=code.replace("*Records:MoveNext'","*Records:MoveNext *DirectArraySet:MergeOwnedRuns *DirectArraySet:MergeRuns *DirectArraySet:MergeSmallRun *DirectArraySet:RotateRuns *DirectArraySet:ReverseRun'")
exec(compile(code,str(root/'Audit~/DenseBulk/run.py'),'exec'))
