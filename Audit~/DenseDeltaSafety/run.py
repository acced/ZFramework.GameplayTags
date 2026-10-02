#!/usr/bin/env python3
"""Reuse pinned complete fixtures, with isolated allocation-failure builds before timing."""
from pathlib import Path
root=Path(__file__).resolve().parents[2]
code=(root/'Audit~/DenseBulk/run.py').read_text()
code=code.replace("HERE/'Extra.cs'","ROOT/'Audit~/DenseBulk/Extra.cs'")
code=code.replace("dlls['combined']","dlls['batch32']").replace("files['combined']","files['batch32']").replace("'combined','tests'","'batch32','tests'")
marker="    probefile=out/'generated/Probe.cs';probefile.write_text(probe)"
assert code.count(marker)==1
inject='''    probe=probe.replace('var bulkTests=BulkTests();','var bulkTests=BulkTests(); var streamTests=StreamingTests(); var cacheTests=CacheTests(); var batchTests=BatchTests();').replace('bulkTests,localTests,','batchTests,cacheTests,streamTests,bulkTests,localTests,')
    probe=probe.replace('if(stage=="scale") AppendScale();','if(stage=="focused") { CacheBoundary(); Unequal(); } else if(stage=="holdout") BatchHoldout(); else if(stage=="scale") AppendScale();')
'''
code=code.replace(marker,inject+marker,1)
marker="    common=list((ROOT/'Runtime').rglob('*.cs'))"
assert code.count(marker)==1
code=code.replace(marker,"    for src,name in ((ROOT/'Audit~/DenseStream/Extra.cs','StreamExtra.cs'),(ROOT/'Audit~/DenseCache/Extra.cs','CacheExtra.cs'),(ROOT/'Audit~/DenseCacheUnequal/Extra.cs','UnequalExtra.cs'),(HERE/'Extra.cs','BatchExtra.cs')):\n        dest=out/'generated'/name;dest.write_bytes(src.read_bytes());extras.append(dest)\n"+marker,1)
marker="    oldtest=out/'generated/FullMicroTests.cs'"
assert code.count(marker)==1
fault='''    fault_hashes={}
    for name in files:
        original=files[name][0].read_text()
        marker_alloc='var next = new Entry[size];'
        assert original.count(marker_alloc)==1
        instrumented=original.replace(marker_alloc,'AllocationFault.BeforeAllocation(size); '+marker_alloc,1)
        fault_file=out/'generated'/('FaultCore-'+name+'.cs');fault_file.write_text(instrumented)
        f=build(name+'-fault',common+[fault_file,files[name][1],HERE/'Faults.cs'])
        print(run(['dotnet',f,name,out/(name+'-fault.json')],name+'-fault.log').strip(),flush=True)
        fault_hashes[name]=dict(original=sha(files[name][0]),instrumented=sha(fault_file),binary=sha(f))
    (out/'FaultSources.json').write_text(json.dumps(fault_hashes,indent=2))
'''
code=code.replace(marker,fault+marker,1)
code=code.replace("('bench','pairs','scale')","('bench','pairs','scale','focused','holdout')")
code=code.replace('*DirectArraySet:AppendDenseToMicro ', '*DirectArraySet:AppendDenseToMicro *DirectArraySet:AppendDenseSmall *DirectArraySet:AppendDenseUncached *DirectArraySet:MergeMissingBatch ')
exec(compile(code,str(root/'Audit~/DenseBulk/run.py'),'exec'))
