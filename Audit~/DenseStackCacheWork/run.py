#!/usr/bin/env python3
"""Untimed only. Reuse PR20 counters on byte-identical PR23 source alternatives."""
from pathlib import Path
root=Path(__file__).resolve().parents[2]
code=(root/'Audit~/DenseStreamWork/run.py').read_text()
code=code.replace("ROOT/'Audit~/DenseStream/generate.py'","ROOT/'Audit~/DenseStackCache/generate.py'")
code=code.replace("ROOT/'Audit~/DenseStreamWork/Probe.cs'","ROOT/'Audit~/DenseStackCacheWork/Probe.cs'")
code=code.replace('WorkSuffixMoves, WorkGrowthCopies;', 'WorkSuffixMoves, WorkGrowthCopies, WorkCacheWrites, WorkCacheReads;')
marker="    folder=work/name;folder.mkdir(exist_ok=True)"
assert marker in code
extra="""    if 'private void AppendDenseCached(' in code:
        helper='private static int CountCacheRead(int index) { WorkCacheReads++; return index; }\\n        '
        code=code.replace('public TagRegistry Registry',helper+'public TagRegistry Registry',1)
        old=g.block(code,'private void AppendDenseCached(')
        new=old.replace('records[sourceRecords++] = value;','records[sourceRecords++] = value; WorkCacheWrites++;')
        new=new.replace('records[pending]','records[CountCacheRead(pending)]').replace('records[pending--]','records[CountCacheRead(pending--)]')
        assert new!=old;code=code.replace(old,new,1)
"""
code=code.replace(marker,extra+marker,1)
# Original report's standard counters remain; cache counters are in JSON/console.
code=code.replace('PR20主计时构建','PR23主计时构建')
exec(compile(code,str(root/'Audit~/DenseStreamWork/run.py'),'exec'))
