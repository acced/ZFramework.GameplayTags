#!/usr/bin/env python3
"""Repeat PR18's full raw-data/source checks with three fixed alternatives."""
from pathlib import Path
root=Path(__file__).resolve().parents[2]
code=(root/'Audit~/DenseBulk/review.py').read_text()
code=code.replace('from generate import EXPECTED,BASE,prior,fixed,bulk,block','from generate import EXPECTED,BASE,prior,fixed,bulk,block,bounded')
code=code.replace("NAMES=('direct','split','fixed','bulk','combined')","NAMES=('fixed','combined','bounded')")
start=code.index('PAIRS=');end=code.index('\ndef sha',start)
code=code[:start]+"PAIRS=(('combined','fixed'),('bounded','combined'),('bounded','fixed'))"+code[end:]
old="expected=dict(direct=direct,split=split,fixed=fixed(split),bulk=bulk(split),combined=bulk(fixed(split)))"
assert old in code
code=code.replace(old,"expected=dict(fixed=fixed(split),combined=bulk(fixed(split)),bounded=bounded(bulk(fixed(split))))")
code=code.replace("assert len(m['jobs'])==30","assert len(m['jobs'])==18")
exec(compile(code,str(root/'Audit~/DenseBulk/review.py'),'exec'))
