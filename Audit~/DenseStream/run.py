#!/usr/bin/env python3
from pathlib import Path
root=Path(__file__).resolve().parents[2]
code=(root/'Audit~/DenseBulk/run.py').read_text()
code=code.replace("HERE/'Extra.cs'","ROOT/'Audit~/DenseBulk/Extra.cs'")
code=code.replace("dlls['combined']","dlls['stream']").replace("files['combined']","files['stream']").replace("'combined','tests'","'stream','tests'")
marker="    probefile=out/'generated/Probe.cs';probefile.write_text(probe)"
assert marker in code
code=code.replace(marker,"    probe=probe.replace('var bulkTests=BulkTests();','var bulkTests=BulkTests(); var streamTests=StreamingTests();').replace('bulkTests,localTests,','streamTests,bulkTests,localTests,')\n"+marker,1)
marker="    common=list((ROOT/'Runtime').rglob('*.cs'))"
assert marker in code
code=code.replace(marker,"    stream_extra=out/'generated/StreamExtra.cs';stream_extra.write_bytes((HERE/'Extra.cs').read_bytes());extras.append(stream_extra)\n"+marker,1)
exec(compile(code,str(root/'Audit~/DenseBulk/run.py'),'exec'))
