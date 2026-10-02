#!/usr/bin/env python3
"""Keep the complete PR20 protocol; add a boundary stage and cache assertions."""
from pathlib import Path
root=Path(__file__).resolve().parents[2]
code=(root/'Audit~/DenseBulk/run.py').read_text()
code=code.replace("HERE/'Extra.cs'","ROOT/'Audit~/DenseBulk/Extra.cs'")
code=code.replace("dlls['combined']","dlls['cache32']").replace("files['combined']","files['cache32']").replace("'combined','tests'","'cache32','tests'")
marker="    probefile=out/'generated/Probe.cs';probefile.write_text(probe)"
assert marker in code
code=code.replace(marker,"    probe=probe.replace('var bulkTests=BulkTests();','var bulkTests=BulkTests(); var streamTests=StreamingTests(); var stackTests=StackCacheTests();').replace('bulkTests,localTests,','stackTests,streamTests,bulkTests,localTests,')\n    probe=probe.replace('if(stage==\"scale\") AppendScale();','if(stage==\"boundary\") CacheBoundary(); else if(stage==\"scale\") AppendScale();')\n"+marker,1)
marker="    common=list((ROOT/'Runtime').rglob('*.cs'))"
assert marker in code
code=code.replace(marker,"    for src,name in ((ROOT/'Audit~/DenseStream/Extra.cs','StreamExtra.cs'),(HERE/'Extra.cs','StackExtra.cs')):\n        dst=out/'generated'/name;dst.write_bytes(src.read_bytes());extras.append(dst)\n"+marker,1)
code=code.replace("('bench','pairs','scale')","('bench','pairs','scale','boundary')")
code=code.replace('*DirectArraySet:AppendDenseToMicro ', '*DirectArraySet:AppendDenseToMicro *DirectArraySet:AppendDenseCached *DirectArraySet:AppendDenseUncached ')
exec(compile(code,str(root/'Audit~/DenseBulk/run.py'),'exec'))
