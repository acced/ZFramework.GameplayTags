#!/usr/bin/env python3
"""Keep frozen public timers and faults; add explicit empty-input admission cases."""
from pathlib import Path
root=Path(__file__).resolve().parents[2]
code=(root/'Audit~/CopyCommit/run.py').read_text()
code=code.replace("'checkpoint'","'boundary'")
code=code.replace("HERE/'Faults.cs'","ROOT/'Audit~/CopyCommit/Faults.cs'")
code=code.replace("(HERE/'Extra.cs').read_bytes()","(ROOT/'Audit~/CopyCommit/Extra.cs').read_bytes()")
tail="exec(compile(code,str(root/'Audit~/DenseBulk/run.py'),'exec'))"
assert code.count(tail)==1
patch='''
code=code.replace("('bench','copy')","('bench','copy','empty')")
marker="    probefile=out/'generated/Probe.cs';probefile.write_text(probe)"
assert code.count(marker)==1
addition="    probe=probe.replace('var bulkTests=BulkTests();','var bulkTests=BulkTests(); var boundaryTests=BoundaryTests();').replace('bulkTests,localTests,','boundaryTests,bulkTests,localTests,').replace('if(stage==\\\"copy\\\") CopyCases();','if(stage==\\\"empty\\\") EmptyCases(); else if(stage==\\\"copy\\\") CopyCases();')\\n"
code=code.replace(marker,addition+marker,1)
marker="    common=list((ROOT/'Runtime').rglob('*.cs'))"
assert code.count(marker)==1
addition="    boundary_extra=out/'generated/BoundaryExtra.cs';boundary_extra.write_bytes((HERE/'Extra.cs').read_bytes());extras.append(boundary_extra)\\n"
code=code.replace(marker,addition+marker,1)
code=code.replace('MixedProbe:CopyLoop ', 'MixedProbe:CopyLoop MixedProbe:EmptyCaseLoop *DirectArraySet:CopyCore *DirectArraySet:CopyFrom *DirectArraySet:Clear *DirectArraySet:CopyDenseGrowing ')
marker="    for p in work.rglob('obj'):shutil.rmtree(p)"
assert code.count(marker)==1
addition="    for name,dll in dlls.items():\\n        run(['dotnet',dll,name,'empty',out/(name+'-empty-codegen.json'),0],name+'-empty-codegen.log',{'DOTNET_JitDisasm':'MixedProbe:EmptyCaseLoop *DirectArraySet:CopyCore *DirectArraySet:CopyFrom *DirectArraySet:Clear *DirectArraySet:CopyDenseToMicro *DirectArraySet:CopyDenseGrowing'})\\n"
code=code.replace(marker,addition+marker,1)
'''
code=code.replace(tail,patch+'\n'+tail,1)
exec(compile(code,str(root/'Audit~/CopyCommit/run.py'),'exec'))
