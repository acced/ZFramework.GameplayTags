"""Pure source/projection checks; no compiler, target, or coordinator invocation."""
import ast
from pathlib import Path
import sys
import textwrap
sys.dont_write_bytecode=True
from run import adapt

if len(sys.argv)!=3:raise SystemExit('Original runner and r2 collector paths required')
source=Path(sys.argv[1]).read_text();changed=adapt(source);ast.parse(changed)
sequence=source[source.index("        status['Stage']='semantic'\n"):source.index('    except GateStop: pass\n')]
assert sequence in changed
assert 'completed-semantic-only' not in changed
assert "record['ExitCode']=active_proc.returncode" in changed and 'REPAIR_VERIFY()' in changed
here=Path(__file__).parent
assert (here/'MetadataGate.cs').read_bytes()==Path(sys.argv[2]).read_bytes()
wrapper=(here/'run.py').read_text()
assert "choices=('mono',)" in wrapper and 'choices=(300,),default=300' in wrapper
start=wrapper.index('    counts=tuple(status.get')
end=wrapper.index('    for name in PUBLIC:save(out/name,payloads[name])',start)
tail=compile(textwrap.dedent(wrapper[start:end]),'<fixed-status-source>','exec')

def check(state,stage,counts,lengths,expected,intact=True,within=True,failure=None):
    status=dict(zip(('Builds','SemanticTargets','ColdTargets','WarmTargets','PilotTargets'),counts),State=state,Stage=stage,WithinDeadline=within)
    scope={'status':status,'payloads':{'raw/cold.json':[{}]*lengths[0],'raw/warm.json':[{}]*lengths[1]},'intact':intact,'wrapper_failure':failure}
    exec(tail,scope)
    assert status['State']==expected,(state,stage,status)
check('complete','completed',(2,2,32,56,0),(32,56),'complete')
check('rejected','warm-gate',(2,2,32,56,0),(32,56),'rejected')
check('blocked','cold-gate',(2,2,32,0,0),(32,0),'blocked')
check('complete','completed',(2,2,32,56,0),(32,55),'incomplete')
check('blocked','cold-gate',(2,2,32,1,0),(32,1),'incomplete')
check('complete','completed',(2,2,32,56,0),(32,56),'incomplete',intact=False)
check('complete','completed',(2,2,32,56,0),(32,56),'incomplete',within=False)
check('complete','completed',(2,2,32,56,0),(32,56),'incomplete',failure='UNCLASSIFIED')
check('incomplete','semantic',(2,1,0,0,0),(0,0),'incomplete')
print('PASS unchanged full sequence/cold fail-stop, exact r2 collector, Mono-only cap, preserved blocked/rejected/incomplete states')
