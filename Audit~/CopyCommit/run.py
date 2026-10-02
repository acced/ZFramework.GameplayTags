#!/usr/bin/env python3
"""Reuse frozen public fixtures; isolate allocation faults from all timed binaries."""
from pathlib import Path
root=Path(__file__).resolve().parents[2]
code=(root/'Audit~/DenseBulk/run.py').read_text()
code=code.replace("HERE/'Extra.cs'","ROOT/'Audit~/DenseBulk/Extra.cs'")
code=code.replace("dlls['combined']","dlls['checkpoint']").replace("files['combined']","files['checkpoint']").replace("'combined','tests'","'checkpoint','tests'")
marker="    probefile=out/'generated/Probe.cs';probefile.write_text(probe)"
assert code.count(marker)==1
code=code.replace(marker,"    probe=probe.replace('if(stage==\"scale\") AppendScale();','if(stage==\"copy\") CopyCases(); else if(stage==\"scale\") AppendScale();')\n"+marker,1)
marker="    common=list((ROOT/'Runtime').rglob('*.cs'))"
assert code.count(marker)==1
code=code.replace(marker,"    copy_extra=out/'generated/CopyExtra.cs';copy_extra.write_bytes((HERE/'Extra.cs').read_bytes());extras.append(copy_extra)\n"+marker,1)
marker="    oldtest=out/'generated/FullMicroTests.cs'"
assert code.count(marker)==1
fault='''    fault_hashes={}
    for name in files:
        original=files[name][0].read_text()
        marker_alloc='var next = new Entry[size];'
        assert original.count(marker_alloc)==1
        instrumented=original.replace(marker_alloc,'CopyFault.Before(); '+marker_alloc,1)
        fault_file=out/'generated'/('FaultCore-'+name+'.cs');fault_file.write_text(instrumented)
        f=build(name+'-fault',common+[fault_file,files[name][1],HERE/'Faults.cs'])
        print(run(['dotnet',f,name,out/(name+'-fault.json')],name+'-fault.log').strip(),flush=True)
        fault_hashes[name]=dict(original=sha(files[name][0]),instrumented=sha(fault_file),binary=sha(f))
    (out/'FaultSources.json').write_text(json.dumps(fault_hashes,indent=2))
'''
code=code.replace(marker,fault+marker,1)
code=code.replace("('bench','pairs','scale')","('bench','copy')")
code=code.replace('MixedProbe:CopyLoop ', 'MixedProbe:CopyLoop MixedProbe:CopyCaseLoop ')
exec(compile(code,str(root/'Audit~/DenseBulk/run.py'),'exec'))
