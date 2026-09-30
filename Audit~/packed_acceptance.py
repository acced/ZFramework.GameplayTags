#!/usr/bin/env python3
"""Run unchanged shipping C# through packed audit plus explicit word/layout transitions."""
import hashlib,json,pathlib,re,subprocess
import packed_audit
from packed_experiment import proposed, method
ROOT=pathlib.Path(__file__).resolve().parent.parent
original_project=packed_audit.project

def clean(text):
    # Used only to confirm adoption of the tested trial; compilation is still from
    # the literal shipping file, not this token-normalized text.
    text=re.sub(r'//[^\n]*|/\*.*?\*/','',text,flags=re.S)
    return ''.join(text.split())

def adoption():
    before=subprocess.check_output(['git','show','057261aad066c163aef6822eb9dfe3d46808cb76:Runtime/RuntimeTagSet.cs'],cwd=ROOT,text=True)
    expected=proposed(before)
    # The reverse-mask kernel no longer uses Highest. Remove this dead method.
    expected=method(expected,'internal static int Highest(ulong value)','')
    actual=(ROOT/'Runtime/RuntimeTagSet.cs').read_text()
    if clean(actual)!=clean(expected):raise RuntimeError('Shipping code differs from the tested cursor candidate; reconcile before certifying adoption')
    print('ADOPTED_CURSOR_RUNTIME_SHA256='+hashlib.sha256(actual.encode()).hexdigest(),flush=True)

def extended_project(path,includes,*args,**kwargs):
    includes=list(includes)
    target=ROOT/'Audit~/PackedTests.cs'
    if target in includes:
        original=target.read_text()
        if original.count('        Tests();')!=1:raise RuntimeError('Reconcile transition suite entry')
        source=path.parent/'PackedTestsWithTransitions.cs'
        source.write_text(original.replace('        Tests();','        Tests(); PackedLayoutChecks.Run();'))
        includes[includes.index(target)]=source
        includes.append(ROOT/'Audit~/PackedLayoutChecks.cs')
    return original_project(path,includes,*args,**kwargs)

if __name__=='__main__':
    adoption()
    packed_audit.project=extended_project
    packed_audit.main()
