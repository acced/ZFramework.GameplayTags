#!/usr/bin/env python3
"""Isolated kernel experiment. Never patches shipping files; preserve every trial and sample."""
import argparse,hashlib,json,math,os,pathlib,shutil,statistics,subprocess,tempfile,zipfile
from integer_audit import project
from packed_audit import fixture
ROOT=pathlib.Path(__file__).resolve().parent.parent

CTOR='''public RuntimeTagSet(TagRegistry registry, int capacity = 0)
        {
            Registry = registry ?? throw new ArgumentNullException(nameof(registry));
            if (capacity > 1) EnsureCapacity(capacity);
            else if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        }'''
COPY='''public RuntimeTagSet(RuntimeTagSet source)
        {
            Registry = Required(source).Registry;
            if (source.m_WordCount <= 1)
            {
                m_GroupCount = source.m_GroupCount; m_WordCount = source.m_WordCount; m_Count = source.m_Count;
                if (m_WordCount != 0) { m_InlineGroup = source.ReadGroup(0); m_InlineWord = source.ReadWord(0); }
                return;
            }
            EnsureLayout(source.m_GroupCount, source.m_WordCount, true);
            CopyContents(source);
        }'''
CONTAINS='''internal bool ContainsId(int id)
        {
            if (m_GroupCount == 0) return false;
            Group group;
            if (m_GroupCount == 1)
            {
                group = ReadGroup(0);
                if (group.Key != (id >> 12)) return false;
            }
            else
            {
                int at = FindGroup(id >> 12);
                if (at < 0) return false;
                group = ReadGroup(at);
            }
            int slot = (id >> 6) & 63;
            ulong wordBit = 1UL << slot;
            if ((group.Mask & wordBit) == 0) return false;
            int rank = group.Mask == wordBit ? 0 : group.Mask == ulong.MaxValue ? slot
                : Bits.Count(group.Mask & (wordBit - 1));
            return (ReadWord(group.Offset + rank) & (1UL << (id & 63))) != 0;
        }'''
UNION_SINGLE='''
            if (left.m_WordCount == 1 && right.m_WordCount == 1)
            {
                Group first = left.ReadGroup(0), second = right.ReadGroup(0);
                if (first.Key == second.Key && first.Mask == second.Mask)
                {
                    ulong word = left.ReadWord(0) | right.ReadWord(0);
                    result.WriteWord(0, word); result.WriteGroup(0, new Group(first.Key, 0, first.Mask));
                    result.m_GroupCount = result.m_WordCount = 1; result.m_Count = Bits.Count(word);
                    return;
                }
            }
'''
UNION_OLD='''                        ulong remaining = mask;
                        while (remaining != 0)
                        {
                            ulong bit = 1UL << Bits.Highest(remaining);
                            ulong first = (x.Mask & bit) != 0 ? left.ReadWord(xi--) : 0;
                            ulong second = (y.Mask & bit) != 0 ? right.ReadWord(yi--) : 0;
                            result.WriteWord(--wordWrite, first | second); duplicates += Bits.Count(first & second);
                            remaining &= ~bit;
                        }'''
UNION_NEW='''                        // Reverse directory bits once, then merge low bits without a
                        // highest-bit scan or rank operation for every member word.
                        ulong xm = Bits.Reverse(x.Mask), ym = Bits.Reverse(y.Mask);
                        while (xm != 0 && ym != 0)
                        {
                            ulong xb = xm & unchecked(0UL - xm), yb = ym & unchecked(0UL - ym);
                            if (xb < yb) { result.WriteWord(--wordWrite, left.ReadWord(xi--)); xm &= xm - 1; }
                            else if (yb < xb) { result.WriteWord(--wordWrite, right.ReadWord(yi--)); ym &= ym - 1; }
                            else
                            {
                                ulong first = left.ReadWord(xi--), second = right.ReadWord(yi--);
                                result.WriteWord(--wordWrite, first | second); duplicates += Bits.Count(first & second);
                                xm &= xm - 1; ym &= ym - 1;
                            }
                        }
                        if (xm != 0)
                        { int rest = xi - x.Offset + 1; wordWrite -= rest; CopyWords(left, x.Offset, result, wordWrite, rest); }
                        if (ym != 0)
                        { int rest = yi - y.Offset + 1; wordWrite -= rest; CopyWords(right, y.Offset, result, wordWrite, rest); }'''
REVERSE='''
            internal static ulong Reverse(ulong value)
            {
                value = ((value >> 1) & 0x5555555555555555UL) | ((value & 0x5555555555555555UL) << 1);
                value = ((value >> 2) & 0x3333333333333333UL) | ((value & 0x3333333333333333UL) << 2);
                value = ((value >> 4) & 0x0F0F0F0F0F0F0F0FUL) | ((value & 0x0F0F0F0F0F0F0F0FUL) << 4);
                value = ((value >> 8) & 0x00FF00FF00FF00FFUL) | ((value & 0x00FF00FF00FF00FFUL) << 8);
                value = ((value >> 16) & 0x0000FFFF0000FFFFUL) | ((value & 0x0000FFFF0000FFFFUL) << 16);
                return (value >> 32) | (value << 32);
            }
'''
SUBSET='''private static void SubsetCore(RuntimeTagSet left, RuntimeTagSet right, RuntimeTagSet result, bool difference)
        {
            if (ReferenceEquals(left, right)) { if (difference) result.Clear(); else result.CopyFrom(left); return; }
            if (left.m_WordCount == 1 && right.m_WordCount == 1)
            {
                Group x = left.ReadGroup(0), y = right.ReadGroup(0);
                ulong other = x.Key == y.Key && x.Mask == y.Mask ? right.ReadWord(0) : 0;
                ulong kept = difference ? left.ReadWord(0) & ~other : left.ReadWord(0) & other;
                if (kept == 0) { result.Clear(); return; }
                result.WriteWord(0, kept); result.WriteGroup(0, new Group(x.Key, 0, x.Mask));
                result.m_GroupCount = result.m_WordCount = 1; result.m_Count = Bits.Count(kept); return;
            }
            int aCount = left.m_GroupCount, bCount = right.m_GroupCount;
            int b = 0, groupWrite = 0, wordWrite = 0, count = 0;
            int groupCapacity = result.GroupCapacity, wordCapacity = result.WordCapacity;
            for (int a = 0; a < aCount; a++)
            {
                Group x = left.ReadGroup(a), y = default;
                while (b < bCount && right.ReadGroup(b).Key < x.Key) b++;
                bool match = b < bCount && (y = right.ReadGroup(b)).Key == x.Key;
                if (!difference && !match) continue;
                ulong xm = x.Mask, ym = match ? y.Mask : 0, keptMask = 0;
                int xi = x.Offset, yi = y.Offset, begin = wordWrite;
                // Ordered directory cursors replace two rank/popcount operations per word.
                while (xm != 0)
                {
                    ulong bit = xm & unchecked(0UL - xm), first = left.ReadWord(xi++), second = 0;
                    while (ym != 0 && (ym & unchecked(0UL - ym)) < bit) { ym &= ym - 1; yi++; }
                    if ((ym & bit) != 0) { second = right.ReadWord(yi++); ym &= ~bit; }
                    ulong kept = difference ? first & ~second : first & second;
                    if (kept != 0)
                    {
                        if (groupWrite == groupCapacity || wordWrite == wordCapacity)
                        {
                            result.EnsureLayout(groupWrite + 1, wordWrite + 1);
                            groupCapacity = result.GroupCapacity; wordCapacity = result.WordCapacity;
                        }
                        result.WriteWord(wordWrite++, kept); keptMask |= bit; count += Bits.Count(kept);
                    }
                    xm &= xm - 1;
                }
                if (keptMask != 0) result.WriteGroup(groupWrite++, new Group(x.Key, begin, keptMask));
            }
            result.m_GroupCount = groupWrite; result.m_WordCount = wordWrite; result.m_Count = count;
        }'''

def method(text,signature,replacement):
    start=text.index(signature);brace=text.index('{',start);depth=0
    for end in range(brace,len(text)):
        if text[end]=='{':depth+=1
        elif text[end]=='}':
            depth-=1
            if depth==0:return text[:start]+replacement+text[end+1:]
    raise RuntimeError('Unbalanced method')

def proposed(text):
    text=method(text,'public RuntimeTagSet(TagRegistry registry, int capacity = 0)',CTOR)
    text=method(text,'public RuntimeTagSet(RuntimeTagSet source)',COPY)
    text=method(text,'internal bool ContainsId(int id)',CONTAINS)
    text=method(text,'private static void SubsetCore(',SUBSET)
    mark='            // Size directories, not individual members. Reserve only the actual result words.'
    if mark not in text or UNION_OLD not in text:raise RuntimeError('Reconcile union experiment source')
    text=text.replace(mark,UNION_SINGLE+mark).replace(UNION_OLD,UNION_NEW)
    return text.replace('        private static class Bits\n        {','        private static class Bits\n        {'+REVERSE)

def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--output',required=True);p.add_argument('--rounds',type=int,default=3);a=p.parse_args()
    output=pathlib.Path(a.output).resolve();output.mkdir(parents=True,exist_ok=True)
    env=dict(os.environ,DOTNET_TieredCompilation='0',DOTNET_NOLOGO='1',DOTNET_CLI_TELEMETRY_OPTOUT='1')
    def run(command,log,cwd):
        proc=subprocess.run(list(map(str,command)),cwd=cwd,env=env,text=True,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,timeout=900)
        (output/log).write_text(proc.stdout);print(proc.stdout,flush=True)
        if proc.returncode:raise RuntimeError(log+' failed')
    with tempfile.TemporaryDirectory(prefix='packed-trials-') as directory:
        work=pathlib.Path(directory);(work/'global.json').write_text('{"sdk":{"version":"8.0.425","rollForward":"disable"}}');(work/'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
        base=(ROOT/'Runtime/RuntimeTagSet.cs').read_text();builds={};inventory={}
        for name,text in [('base',base),('cursor',proposed(base))]:
            src=work/name;shutil.copytree(ROOT/'Runtime',src/'Runtime');(src/'Runtime/RuntimeTagSet.cs').write_text(text)
            (output/(name+'-RuntimeTagSet.cs')).write_text(text);inventory[name]=hashlib.sha256(text.encode()).hexdigest()
            includes=[src/'Runtime/**/*.cs',ROOT/'Editor/**/*.cs',ROOT/'Samples~/**/*.cs',ROOT/'Audit~/UnityStubs.cs']
            project(src/'Tests.csproj',includes+[ROOT/'Audit~/PackedTests.cs'])
            run(['dotnet','build',src/'Tests.csproj','-c','Release','-o',src/'out-tests'],name+'-build-tests.log',src)
            run(['dotnet','exec',src/'out-tests/Tests.dll',output/(name+'-tests.json')],name+'-tests.log',src)
            code=fixture((ROOT/'Audit~/FourWay.cs').read_text(),packed=True)
            (src/'Bench.cs').write_text(code);project(src/'Bench.csproj',includes+[src/'Bench.cs'],'CANDIDATE')
            run(['dotnet','build',src/'Bench.csproj','-c','Release','-p:BaseIntermediateOutputPath=obj-bench/','-o',src/'out-bench'],name+'-bench-build.log',src)
            builds[name]=(src/'out-bench/Bench.dll',src)
        for r in range(a.rounds):
            for name in (['base','cursor'] if r%2==0 else ['cursor','base']):
                dll,cwd=builds[name];run(['dotnet','exec',dll,output/(name+'-r'+str(r)+'.json'),name,'Auto'],name+'-r'+str(r)+'.log',cwd)
        tables={};keys=None
        for name in builds:
            table={}
            for r in range(a.rounds):
                rows=json.loads((output/(name+'-r'+str(r)+'.json')).read_text())['rows'];seen=set()
                for row in rows:
                    key=(row['operation'],row['size'],row['universe'],row['distribution'])
                    if row['status']!='measured' or key in seen or len(row['ns'])!=7 or any(not math.isfinite(x) or x<=0 for x in row['ns']):raise RuntimeError('Invalid experiment result')
                    seen.add(key);table.setdefault(key,[]).append(row)
                if keys is None:keys=seen
                if keys!=seen:raise RuntimeError('Missing experimental row')
            tables[name]=table
        comparisons=[]
        for key in sorted(keys):
            base=tables['base'][key];trial=tables['cursor'][key]
            if len({r['inputDigest'] for r in base+trial})!=1:raise RuntimeError('Input differs')
            old=statistics.median(statistics.median(r['ns']) for r in base);new=statistics.median(statistics.median(r['ns']) for r in trial)
            comparisons.append({'operation':key[0],'members':key[1],'definitions':key[2],'distribution':key[3],'base_ns':old,'cursor_ns':new,'ratio':new/old,'base':base,'cursor':trial})
        (output/'comparison.json').write_text(json.dumps(comparisons,indent=2))
        summary={'experimental_not_shipped':True,'runtime_hashes':inventory,'rows':len(comparisons),'wins':sum(r['ratio']<1 for r in comparisons),'over_5_percent':[ {k:v for k,v in r.items() if k not in ('base','cursor')} for r in comparisons if r['ratio']>1.05],'release_approved':False}
        (output/'summary.json').write_text(json.dumps(summary,indent=2));print('EXPERIMENT',json.dumps(summary),flush=True)
        for r in comparisons:
            if r['operation']!='exact_miss' and (r['definitions'],r['members']) in [(10000,1024),(262144,1),(262144,8),(262144,128)]:
                print('EXPERIMENT_ROW',json.dumps({k:v for k,v in r.items() if k not in ('base','cursor')}),flush=True)
if __name__=='__main__':main()
