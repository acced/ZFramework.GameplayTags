#!/usr/bin/env python3
"""Reuse original counter probe; count scalar AND block suffix traffic, not time."""
from pathlib import Path
import subprocess
root=Path(__file__).resolve().parents[2]
subprocess.check_call(['git','diff','--exit-code','49731dcc487a3168b2007252efa56d51f608f31f','HEAD','--','Audit~/DenseDeltaSafety'],cwd=root)
code=(root/'Audit~/DenseStreamWork/run.py').read_text()
code=code.replace("ROOT/'Audit~/DenseStream/generate.py'","ROOT/'Audit~/DenseDeltaSafety/generate.py'")
marker="    folder=work/name;folder.mkdir(exist_ok=True)"
assert code.count(marker)==1
inject='''    # A block Array.Copy still moves records; do not label it zero movement.
    for signature in ('private void AppendDenseSmall(', 'private void MergeMissingBatch('):
        if signature not in code: continue
        old=g.block(code,signature)
        anchor='if (tail < used) Array.Copy(entries, tail, entries, tail + n, used - tail);'
        new=old.replace(anchor,'if (tail < used) { WorkSuffixMoves += used-tail; Array.Copy(entries, tail, entries, tail+n, used-tail); }')
        if new!=old: code=code.replace(old,new,1)
'''
code=code.replace(marker,inject+marker,1)
code=code.replace('四个源算法与PR20主计时构建逐字节相同','五个源算法来自PR24固定生成器（入口先确认全部算法文件与49731dcc一致）')
code=code.replace('SuffixMoves是单次插入Array.Copy搬移的记录总量','SuffixMoves包含单次插入和缓存批量Array.Copy搬移的记录总量')
exec(compile(code,str(root/'Audit~/DenseStreamWork/run.py'),'exec'))
