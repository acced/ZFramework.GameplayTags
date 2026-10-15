"""Allowlisted deterministic UPM candidate; does not publish a release/tag."""
import gzip,hashlib,io,json,pathlib,subprocess,sys,tarfile
root=pathlib.Path(__file__).resolve().parents[2];out=pathlib.Path(sys.argv[1]).resolve();out.mkdir(parents=True,exist_ok=False)
files=[]
for folder in ['Runtime','Editor']:
    files+=sorted(p for p in (root/folder).rglob('*') if p.is_file() and not p.is_symlink())
for name in ['Runtime.meta','Editor.meta','package.json','package.json.meta','README.md','README_CN.md','LICENSE','LICENSE.meta','Documentation~/HARDENING.md','CHANGELOG.md']:
    p=root/name
    if p.is_file():files.append(p)
contents={p.relative_to(root).as_posix():p.read_bytes() for p in files}
manifest={'source_commit':subprocess.check_output(['git','rev-parse','HEAD'],cwd=root,text=True).strip(),'unity_execution':'not-run','files':{n:hashlib.sha256(b).hexdigest() for n,b in contents.items()}}
contents['SOURCE_MANIFEST.json']=(json.dumps(manifest,indent=2)+'\n').encode()
version=json.loads(contents['package.json'])['version'];name=f'com.acced.zframework.gameplaytags-{version}.tgz'
with (out/name).open('wb') as f:
    with gzip.GzipFile(filename='',mode='wb',fileobj=f,mtime=0) as z:
        with tarfile.open(fileobj=z,mode='w',format=tarfile.PAX_FORMAT) as tar:
            for path,data in sorted(contents.items()):
                t=tarfile.TarInfo('package/'+path);t.size=len(data);t.mtime=0;t.mode=0o644;tar.addfile(t,io.BytesIO(data))
with tarfile.open(out/name,'r:gz') as tar:
    for member in tar:
        assert member.isfile() and tar.extractfile(member).read()==contents[member.name[len('package/'):]]
hash=hashlib.sha256((out/name).read_bytes()).hexdigest();(out/(name+'.sha256')).write_text(hash+'  '+name+'\n')
(out/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n');print(name,hash)
