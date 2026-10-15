#!/usr/bin/env python3
"""Offline extraction only. Preserve instruction offsets/constants; mask observed canonical native-address forms and generated method-label IDs."""
import pathlib,re,json,hashlib,difflib,argparse
parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--input',type=pathlib.Path,required=True);parser.add_argument('--out',type=pathlib.Path,required=True);args=parser.parse_args()
HERE=args.out.resolve();HERE.mkdir(parents=True,exist_ok=False);JIT=args.input.resolve()
chosen=('CopyValidated','CopyFrom','AllRegistered','UnionSorted','IntersectionExactSorted','<Run>b__10(int)','GameplayTagContainer:Union(','GameplayTagContainer:IntersectionExact(','GameplayTagContainer:EnsureCapacity(','List`1[GameplayTags.GameplayTag]:AddRange(','List`1[GameplayTags.GameplayTag]:CopyTo(','List`1[GameplayTags.GameplayTag]:AddWithResize(')
result={};normalized={}
for version in ('B11','C1','current792'):
    text=(JIT/(version+'.jit.asm')).read_text();entries={}
    for block in text.split('; Assembly listing for method ')[1:]:
        name=block.splitlines()[0]
        if not any(x in name for x in chosen):continue
        # These Linux x64 captures use canonical 12-digit native addresses beginning
        # with 7. Preserve 14-digit packed UTF-16 string-comparison constants, and
        # all smaller constants/field/stack offsets. Review any other capture layout.
        clean=re.sub(r'0x7[0-9A-Fa-f]{11}\b','0xNATIVE_POINTER',block)
        clean=re.sub(r'G_M\d+_IG','G_M_IG',clean)
        n=re.search(r'; Total bytes of code (\d+)',block)
        entries[name]={'code_bytes':int(n.group(1)) if n else None,'normalized_sha256':hashlib.sha256(clean.encode()).hexdigest(),'calls':[x.strip() for x in block.splitlines() if re.match(r'\s+(call|tail.jmp)',x)]}
        normalized[(version,name)]=clean
    result[version]=entries
for version in ('B11','C1','current792'):
    (HERE/(version+'.selected.normalized.asm')).write_text('\n\n'.join('; Assembly listing for method '+normalized[(version,name)] for name in result[version]))
comparisons={}
for a,b in [('B11','C1'),('C1','current792')]:
    common=sorted(set(result[a]) & set(result[b]));comparisons[a+'__'+b]={name:result[a][name]['normalized_sha256']==result[b][name]['normalized_sha256'] for name in common}
    diff=difflib.unified_diff((HERE/(a+'.selected.normalized.asm')).read_text().splitlines(),(HERE/(b+'.selected.normalized.asm')).read_text().splitlines(),fromfile=a,tofile=b,lineterm='')
    (HERE/(a+'__'+b+'.diff')).write_text('\n'.join(diff)+'\n')
(HERE/'jit-comparison.json').write_text(json.dumps({'normalization':'Only the observed canonical 12-digit 0x7-prefixed native-address forms and generated G_M label IDs; packed UTF-16 constants and all other immediates/offsets remain. This is a capture-specific Linux x64 normalization rule, not a general address classifier. Raw dumps preserved. This is a diagnostic textual comparison, not proof of identical addresses, alignment, runtime helper implementations, or timings.','methods':result,'equal_normalized_methods':comparisons},indent=2)+'\n')
print(json.dumps(comparisons,indent=2))
