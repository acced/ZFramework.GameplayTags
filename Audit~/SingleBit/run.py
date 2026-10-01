#!/usr/bin/env python3
"""Execute the pinned PR8 full-operation protocol with one-variable singleton variants.
The resolved runner is archived so string transformations are never hidden evidence.
"""
import pathlib,subprocess,sys,difflib,json,hashlib
ROOT=pathlib.Path(__file__).resolve().parents[2]
PIN='2866adfc6bfc21a11177c6b5873fbc54afebfe13'
BASEFILE='Audit~/ArrayBuild/run.py'
def git(*a):return subprocess.check_output(['git',*a],cwd=ROOT)
for rel in (BASEFILE,'Audit~/ArrayBuild/build_candidates.py','Audit~/Triad/run_triad.py','Audit~/Fusion/DenseFusion.cs'):
    if git('show',PIN+':'+rel)!=(ROOT/rel).read_bytes():raise RuntimeError('Changed pinned source '+rel)
old=git('show',PIN+':'+BASEFILE).decode()
text=old
changes=[("HERE=ROOT/'Audit~/ArrayBuild'","HERE=ROOT/'Audit~/SingleBit'"),
         ('from build_candidates import candidates','from singleton_candidates import candidates'),
         ('DirectGrowthSetTests.cs','SingleBitSetTests.cs'),
         ("[('auto',AUTO),('micro',MICRO),('fusion',BASE)]","[('auto',AUTO),('micro',MICRO),('fusion',BASE),('array','"+PIN+"')]")]
for a,b in changes:
    if text.count(a)!=1:raise RuntimeError('Runner transform not unique: '+a)
    text=text.replace(a,b,1)
# Labels now denote the real independent comparison: direct / removal-only / all three operations.
text=text.replace("'arrays'","'remove'").replace("'growth'","'single'").replace('kernel','direct').replace('ARRAY_','SINGLEBIT_')
text=text.replace('arrays: fixed buffers in count/merge/remove, block-copy tails; growth: only adds direct merge into replacement buffer when expansion is necessary',
    'remove: count==used avoids all popcounts in array-backed Micro removal; single: additionally simplifies overlap counting in Micro Union/Append when either side is singleton')
# Untimed supplementary metadata: determine input eligibility independently of Count/used.
# The timed public methods are NOT instrumented. Mask width is the pinned narrow format.
text=text.replace("text=fixture(original,kind,contract=='prepared')", """text=fixture(original,kind,contract=='prepared')
                    text=text.replace('private static int checksum;', '''private static int checksum;
    private static bool SingleRecords(Set s) {
        int previous=-1;
        foreach(var tag in s) { int block=tag.RuntimeIndex>>4; if(block==previous)return false; previous=block; }
        return true;
    }''')
                    text=text.replace('input_storage = StorageName(input)', 'input_single_records = SingleRecords(input), input_storage = StorageName(input)')""")
# Preserve targeted results for the no-intrinsics configuration too.
needle="            run(['dotnet','exec',targeted_dll,out/(label+'-builder-tests.json')],label+'-builder-tests.log',work)"
assert text.count(needle)==1
text=text.replace(needle,needle+"\n            run(['dotnet','exec',targeted_dll,out/(label+'-builder-nohw-tests.json')],label+'-builder-nohw-tests.log',work,{'DOTNET_EnableHWIntrinsic':'0'})")
# The field is descriptive, not a different timing protocol.
text=text.replace("'input_layout':rows[0]['input_storage'],'input_digest':rows[0]['inputDigest']", "'input_layout':rows[0]['input_storage'],'input_digest':rows[0]['inputDigest'],'input_single_records':rows[0].get('input_single_records')")
if '--output' not in sys.argv:raise SystemExit('--output is required')
out=pathlib.Path(sys.argv[sys.argv.index('--output')+1]).resolve();out.mkdir(parents=True,exist_ok=True)
(out/'resolved-runner.py').write_text(text)
(out/'runner-transform.patch').write_text(''.join(difflib.unified_diff(old.splitlines(True),text.splitlines(True),fromfile=PIN+':'+BASEFILE,tofile='resolved-runner.py')))
(out/'runner-origin.json').write_text(json.dumps({'pinned_commit':PIN,'source_file':BASEFILE,'original_sha256':hashlib.sha256(old.encode()).hexdigest(),'resolved_sha256':hashlib.sha256(text.encode()).hexdigest(),'note':'Complete operation samples retained; no changed settling/iterations. Untimed eligibility metadata added.'},indent=2))
exec(compile(text,str(out/'resolved-runner.py'),'exec'),{'__file__':str(ROOT/'Audit~/SingleBit/run.py'),'__name__':'__main__'})
