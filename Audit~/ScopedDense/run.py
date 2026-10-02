#!/usr/bin/env python3
"""Reuse fixed PR15 measurement protocol; add CopyFrom and private-path regressions."""
import pathlib,sys,importlib.util,hashlib,json
HERE=pathlib.Path(__file__).resolve().parent;ROOT=HERE.parents[1]
spec=importlib.util.spec_from_file_location("scoped_generation",HERE/"generate.py")
generation=importlib.util.module_from_spec(spec);spec.loader.exec_module(generation)

def replace_one(text,old,new):
    assert text.count(old)==1,old[:120]
    return text.replace(old,new,1)

def probe_text():
    text=(ROOT/"Audit~/MixedOperations/Probe.cs").read_text()
    extra=r'''
    static long ScopedTests()
    {
        long before=assertions;
        var r=Registry(257);
        var full=new Set(r,257,Layout.Dense);
        int[] localIds={0,16,128,240,255};
        var local=Make(r,localIds,Layout.Micro,32);
        var output=new Set(r,257,Layout.Micro);
        for(int mask=0;mask<65536;mask++)
        {
            full.Clear(); full.AddTag(r.GetTagAt(256));
            var expected=new List<int>{256};
            for(int bit=0;bit<16;bit++) if((mask&(1<<bit))!=0)
            { int id=240+bit; full.AddTag(r.GetTagAt(id)); expected.Add(id); }
            output.AddTag(r.GetTagAt(64));
            long capacity=output.BufferBytes;
            Set.UnionInto(full,local,output); Verify(output,expected.Concat(localIds));
            Check(output.BufferBytes==capacity,"scoped union changed sufficient capacity");
            output.CopyFrom(full); Verify(output,expected);
        }
        var high=Registry(262145);var random=new Random(2026100208);
        for(int i=0;i<192;i++)
        {
            int offset=65536+((i&3)<<4);
            int[] aIds=Enumerable.Range(offset,16).Where(_=>random.Next(2)==0)
                .Concat(new[]{262144}).OrderBy(x=>x).ToArray();
            int[] bIds=Enumerable.Range(offset,16).Where(_=>random.Next(2)==0)
                .Concat(new[]{0,262143}).OrderBy(x=>x).ToArray();
            var a=Make(high,aIds,Layout.Dense);var b=Make(high,bIds,Layout.Micro);
            int[] expected=aIds.Concat(bIds).Distinct().OrderBy(x=>x).ToArray();
            int blocks=expected.Select(x=>x>>4).Distinct().Count();
            var target=new Set(high,blocks,Layout.Micro);long cap=target.BufferBytes;
            target.AddTag(high.GetTagAt(511));
            Set.UnionInto(a,b,target);Verify(target,expected);
            Check(target.BufferBytes==cap,"actual record capacity should suffice");
            Set.UnionInto(b,a,target);Verify(target,expected);
            Set.UnionInto(a,a,target);Verify(target,aIds);
            var alias=new Set(b);Set.UnionInto(a,alias,alias);Verify(alias,expected);
            a.Clear();target.CopyFrom(a);Verify(target,Array.Empty<int>());
            Check(target.BufferBytes==cap,"empty copy shrank capacity");
        }
        var reg=Registry(513);
        var d=Make(reg,new[]{0,15,63,64,511,512},Layout.Dense);
        var m=Make(reg,new[]{1,15,31,512},Layout.Micro);
        var result=new Set(reg,64,Layout.Micro);
        for(int i=0;i<100;i++) {result.CopyFrom(d);Set.UnionInto(d,m,result);}
        GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
        long start=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<1000;i++) {result.CopyFrom(d);Set.UnionInto(d,m,result);}
        Check(GC.GetAllocatedBytesForCurrentThread()==start,"private traversal allocated");
        Verify(result,new[]{0,1,15,31,63,64,511,512});
        return assertions-before;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static int CopyLoop(Set source,Set output,Layout mode,bool fresh,int repeats)
    {
        for(int i=0;i<repeats;i++)
        {
            Set result=fresh?new Set(source.Registry,Math.Min(source.Registry.Count,2*source.Count+17),mode):output;
            result.CopyFrom(source);sink=result;
        }
        return sink.Count;
    }
'''
    text=replace_one(text,"    static int[] Sequence(",extra+"\n    static int[] Sequence(")
    text=replace_one(text,"        var r2=Registry(10000);", "        long scopedAssertions=ScopedTests();\n        var r2=Registry(10000);")
    text=replace_one(text,"        return new{assertions,failures=0,positiveAllocationBytes=positive,allocation,", """        Check(cursorBytes==16 && Unsafe.SizeOf<Set.Enumerator>()==32,"public cursor layout changed");
        var privateType=typeof(Set).GetNestedType("DenseRecordCursor",BindingFlags.NonPublic);
        int privateCursorBytes=privateType==null?0:(int)typeof(Unsafe).GetMethod("SizeOf").MakeGenericMethod(privateType).Invoke(null,null);
        return new{assertions,scopedAssertions,privateCursorBytes,failures=0,positiveAllocationBytes=positive,allocation,""")
    marker="""                    foreach(var rm in layouts)
                    {
                        var b=Make(r,y,rm,Math.Min(u,2*n+17));"""
    copybench="""                    foreach(var copyMode in layouts)
                    {
                        var copyOutput=new Set(r,Math.Min(u,2*n+17),copyMode);
                        foreach(bool freshCopy in new[]{true,false})
                        {
                            CopyLoop(a,copyOutput,copyMode,freshCopy,1);Verify(sink,x);
                            Measure(shape0,freshCopy?"fresh_copyfrom":"reuse_copyfrom",copyMode.ToString(),
                                rep=>CopyLoop(a,copyOutput,copyMode,freshCopy,rep),!freshCopy);
                            Verify(sink,x);
                        }
                    }
"""
    text=replace_one(text,marker,copybench+marker)
    return text

def main():
    # The inherited parser and complete protocol remain authoritative.
    oi=sys.argv.index("--output");out=pathlib.Path(sys.argv[oi+1]).resolve()
    fixture=out/"harness";fixture.mkdir(parents=True,exist_ok=True)
    probe=fixture/"Probe.cs";probe.write_text(probe_text())
    script=(ROOT/"Audit~/MixedOperations/run.py").read_text()
    script=replace_one(script,"from generate import generate,PIN", "# generation injected from this experiment")
    script=script.replace("MixedProbe:UnionLoop MixedProbe:EnumerateLoop GameplayTags.Experiments.DirectArraySet:UnionCore *Records:MoveNext",
        "MixedProbe:UnionLoop MixedProbe:EnumerateLoop MixedProbe:CopyLoop GameplayTags.Experiments.DirectArraySet:UnionCore GameplayTags.Experiments.DirectArraySet:UnionDenseToMicro GameplayTags.Experiments.DirectArraySet:CopyDenseToMicro *DenseRecordCursor:MoveNext")
    ns={"__file__":str(HERE/"driver.py"),"__name__":"scoped_driver"}
    exec(compile(script,str(HERE/"driver.py"),"exec"),ns)
    ns.update(HERE=fixture,ROOT=ROOT,generate=generation.generate,PIN=generation.PIN)
    (out/"harness.json").write_text(json.dumps({"Probe.cs":hashlib.sha256(probe.read_bytes()).hexdigest(),
        "base_fixture":"Audit~/MixedOperations/Probe.cs","public_enumerator_unchanged":True},indent=2))
    ns["main"]()
if __name__=="__main__":main()
