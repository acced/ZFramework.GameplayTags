"""Allowlisted deterministic UPM candidate; does not publish a release/tag."""
import gzip,hashlib,io,json,pathlib,subprocess,sys,tarfile
root=pathlib.Path(__file__).resolve().parents[2];out=pathlib.Path(sys.argv[1]).resolve();out.mkdir(parents=True,exist_ok=False)
files=[]
for folder in ['Runtime','Editor']:
    for p in sorted((root/folder).rglob('*')):
        if p.is_symlink():raise ValueError('Symlink input is not supported: '+str(p))
        if p.is_file():files.append(p)
for name in ['Runtime.meta','Editor.meta','package.json','package.json.meta','README.md','README.md.meta','README_CN.md','README_CN.md.meta','LICENSE','LICENSE.meta','Documentation~/HARDENING.md','Documentation~/HARDENING_RESULTS.md','Documentation~/MIGRATION.md','CHANGELOG.md','CHANGELOG.md.meta']:
    p=root/name
    if p.is_file():files.append(p)
contents={p.relative_to(root).as_posix():p.read_bytes() for p in files}
contents['SOURCE_MANIFEST.json.meta']=b'fileFormatVersion: 2\nguid: 7d5a133df7614e1da4f49b94039bf520\nTextScriptImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n'
manifest={'source_commit':subprocess.check_output(['git','rev-parse','HEAD'],cwd=root,text=True).strip(),'unity_execution':'not-run','files':{n:hashlib.sha256(b).hexdigest() for n,b in contents.items()}}
contents['SOURCE_MANIFEST.json']=(json.dumps(manifest,indent=2)+'\n').encode()
version=json.loads(contents['package.json'])['version'];name=f'com.acced.zframework.gameplaytags-{version}.tgz'
with (out/name).open('wb') as f:
    with gzip.GzipFile(filename='',mode='wb',fileobj=f,mtime=0) as z:
        with tarfile.open(fileobj=z,mode='w',format=tarfile.PAX_FORMAT) as tar:
            for path,data in sorted(contents.items()):
                t=tarfile.TarInfo('package/'+path);t.size=len(data);t.mtime=0;t.mode=0o644;tar.addfile(t,io.BytesIO(data))
with tarfile.open(out/name,'r:gz') as tar:
    seen=set()
    for member in tar:
        key=member.name[len('package/'):]
        assert member.name.startswith('package/') and member.isfile() and key not in seen
        assert tar.extractfile(member).read()==contents[key]
        seen.add(key)
    assert seen==set(contents)
hash=hashlib.sha256((out/name).read_bytes()).hexdigest();(out/(name+'.sha256')).write_text(hash+'  '+name+'\n')
(out/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n');print(name,hash)
