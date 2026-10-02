#!/usr/bin/env python3
"""Focused follow-up, not a rerun of the entire primary public-operation matrix."""
from pathlib import Path
root=Path(__file__).resolve().parents[2]
code=(root/'Audit~/DenseBulk/run.py').read_text()
code=code.replace("HERE/'Extra.cs'","ROOT/'Audit~/DenseBulk/Extra.cs'")
code=code.replace("dlls['combined']","dlls['delta32']").replace("files['combined']","files['delta32']").replace("'combined','tests'","'delta32','tests'")
marker="    probefile=out/'generated/Probe.cs';probefile.write_text(probe)"
assert marker in code
inject='''    probe=probe.replace('var bulkTests=BulkTests();','var bulkTests=BulkTests(); var streamTests=StreamingTests(); var cacheTests=CacheTests();').replace('bulkTests,localTests,','cacheTests,streamTests,bulkTests,localTests,')
    probe=probe.replace('if(stage=="scale") AppendScale();','if(stage=="focused") { CacheBoundary(); Unequal(); } else if(stage=="scale") AppendScale();')
'''
code=code.replace(marker,inject+marker,1)
marker="    common=list((ROOT/'Runtime').rglob('*.cs'))"
assert marker in code
code=code.replace(marker,"    for src,name in ((ROOT/'Audit~/DenseStream/Extra.cs','StreamExtra.cs'),(ROOT/'Audit~/DenseCache/Extra.cs','CacheExtra.cs'),(ROOT/'Audit~/DenseCacheUnequal/Extra.cs','UnequalExtra.cs')):\n        dest=out/'generated'/name;dest.write_bytes(src.read_bytes());extras.append(dest)\n"+marker,1)
code=code.replace("('bench','pairs','scale')","('focused',)")
code=code.replace('*DirectArraySet:AppendDenseToMicro ', '*DirectArraySet:AppendDenseToMicro *DirectArraySet:AppendDenseSmall *DirectArraySet:AppendDenseUncached ')
exec(compile(code,str(root/'Audit~/DenseBulk/run.py'),'exec'))
