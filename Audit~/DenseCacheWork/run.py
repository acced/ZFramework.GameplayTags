#!/usr/bin/env python3
"""Separate counter binaries; never mixed with timing evidence."""
from pathlib import Path
root=Path(__file__).resolve().parents[2]
code=(root/'Audit~/DenseStreamWork/run.py').read_text()
code=code.replace("ROOT/'Audit~/DenseStream/generate.py'","ROOT/'Audit~/DenseCache/generate.py'")
code=code.replace('WorkWordReads, WorkEmits, WorkWrites, WorkFinds, WorkSuffixMoves, WorkGrowthCopies;','WorkWordReads, WorkEmits, WorkWrites, WorkFinds, WorkSuffixMoves, WorkGrowthCopies, WorkCacheWrites, WorkCacheReads;')
old="    old=g.block(code,'private void AppendDenseToMicro(')\n    new=old.replace('if (at < used) Array.Copy(entries, at, entries, at + 1, used - at);','if (at < used) { WorkSuffixMoves += used - at; Array.Copy(entries, at, entries, at + 1, used - at); }')\n    code=code.replace(old,new,1)"
new="""    for signature in ('private void AppendDenseToMicro(', 'private void AppendDenseSmall(', 'private void AppendDenseUncached('):
        if signature not in code: continue
        old=g.block(code,signature)
        new=old.replace('if (at < used) Array.Copy(entries, at, entries, at + 1, used - at);','if (at < used) { WorkSuffixMoves += used - at; Array.Copy(entries, at, entries, at + 1, used - at); }')
        new=new.replace('records[n++] = value;', 'records[n++] = value; WorkCacheWrites++;')
        new=new.replace('Entry a = Read(read), b = records[pending];', 'Entry a = Read(read), b = records[pending]; WorkCacheReads++;')
        new=new.replace('while (pending >= 0) Write(write--, records[pending--]);', 'while (pending >= 0) { WorkCacheReads++; Write(write--, records[pending--]); }')
        code=code.replace(old,new,1)"""
assert old in code
code=code.replace(old,new,1)
marker="rows=[];digests={}"
assert marker in code
prep='''probe=(ROOT/'Audit~/DenseStreamWork/Probe.cs').read_text()
probe=probe.replace('Set.WorkWordReads=Set.WorkEmits=', 'Set.WorkCacheReads=Set.WorkCacheWrites=Set.WorkWordReads=Set.WorkEmits=')
probe=probe.replace('growth=Set.WorkGrowthCopies;', 'growth=Set.WorkGrowthCopies, cacheReads=Set.WorkCacheReads, cacheWrites=Set.WorkCacheWrites;')
probe=probe.replace('growthCopies=growth}', 'growthCopies=growth,cacheReads,cacheWrites}')
probe=probe.replace('foreach(int n in new[]{8,128,4096})', 'foreach(int n in new[]{8,31,32,33,128,4096})')
marker='        File.WriteAllText(args[1],JsonSerializer.Serialize(rows));'
addition="""        foreach(int n in new[]{8,31,32,33,128})
        {
            var random=new Random(2026100233+n);var ids=new SortedSet<int>();
            while(ids.Count<2*n)ids.Add(random.Next(u));
            var seq=ids.OrderBy(_=>random.Next()).ToArray();
            int[] x=seq.Take(n).OrderBy(i=>i).ToArray();
            int[] y=x.Take(n/2).Concat(seq.Skip(n).Take(n-n/2)).OrderBy(i=>i).ToArray();
            MeasureWork(\"global-scattered\",x,y);
        }
"""
assert marker in probe
probe=probe.replace(marker,addition+marker)
probe_file=out/'WorkProbe.cs';probe_file.write_text(probe)
'''
code=code.replace(marker,prep+marker,1)
code=code.replace("ROOT/'Audit~/DenseStreamWork/Probe.cs'","probe_file")
# Restore the generator's read of the original fixture, not its output path.
code=code.replace("probe=(probe_file).read_text()","probe=(ROOT/'Audit~/DenseStreamWork/Probe.cs').read_text()")
code=code.replace('四个源算法与PR20主计时构建逐字节相同','五个源算法由DenseCache同一生成器产生；原始哈希须与主计时产物核对')
code=code.replace('它们不是周期数/缓存未命中数，也不涵盖所有机器指令。','CacheWrites/Reads统计栈缓存存取。它们不是周期数/缓存未命中数，也不涵盖所有机器指令。')
code=code.replace('|后缀搬移|扩容复制|','|后缀搬移|扩容复制|缓存读|缓存写|').replace('|---:|---:|---:|---:|---:|---:|\'','|---:|---:|---:|---:|---:|---:|---:|---:|\'')
code=code.replace("'suffixMoves','growthCopies'))", "'suffixMoves','growthCopies','cacheReads','cacheWrites'))")
exec(compile(code,str(root/'Audit~/DenseStreamWork/run.py'),'exec'))
