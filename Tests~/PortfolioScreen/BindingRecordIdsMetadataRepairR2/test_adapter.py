"""Source-only adapter tests; no compiler or target execution."""
import ast
from pathlib import Path
import sys
sys.dont_write_bytecode=True
from run import adapt

if len(sys.argv)!=4:raise SystemExit('Original runner, original collector, and r1 collector source paths required')
source=Path(sys.argv[1]).read_text();changed=adapt(source);ast.parse(changed)
semantic=source[source.index("        status['Stage']='semantic'\n"):source.index("        status['Stage']='cold'\n")]
assert semantic in changed
assert changed.index("status['Stage']='completed-semantic-only'")<changed.index("        status['Stage']='cold'\n")
assert "raise GateStop()\n        status['Stage']='cold'" in changed
assert "record['ExitCode']=active_proc.returncode" in changed and 'REPAIR_VERIFY()' in changed
old=Path(sys.argv[2]).read_text()
expected=old.replace('{ this.module = module; this.bytes = bytes; ta = typeArgs; ma = methodArgs; }','{\n            this.module = module; this.bytes = bytes;\n            ta = typeArgs != null && typeArgs.Length == 0 ? null : typeArgs;\n            ma = methodArgs != null && methodArgs.Length == 0 ? null : methodArgs;\n        }',1)
assert Path(sys.argv[3]).read_text()==expected
expected=expected.replace('item.GetOtherMethods(true).Select(MethodId)', '(item.GetOtherMethods(true) ?? Array.Empty<MethodInfo>()).Select(MethodId)',1)
assert (Path(__file__).parent/'MetadataGate.cs').read_text()==expected
assert 'SignatureFailure' not in expected and 'SIGNATURE_DIAGNOSTIC' not in expected
print('PASS original semantic block unchanged, bounded stop/guards present, exact two-assignment plus null-event-array collector delta')
