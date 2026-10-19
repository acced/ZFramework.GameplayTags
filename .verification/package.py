from pathlib import Path
import gzip,hashlib,io,json,re,subprocess,sys,tarfile,zipfile
root=Path(__file__).resolve().parents[1];out=Path(sys.argv[1]);out.mkdir(parents=True,exist_ok=True)
files={}
for folder in ['Runtime','Editor']:
 for p in sorted((root/folder).rglob('*')):
  if p.is_file():files[p.relative_to(root).as_posix()]=p.read_bytes()
for name in ['package.json','package.json.meta','README.md','README_CN.md','README.md.meta','README_CN.md.meta','LICENSE','LICENSE.meta','Runtime.meta','Editor.meta','GameplayTags.Runtime.csproj','.gitignore']:
 files[name]=(root/name).read_bytes()
assert not any('GameplayTagPresenceSet' in n or b'GameplayTagPresenceSet' in d for n,d in files.items())
guids=[]
for n,d in files.items():
 if n.endswith('.meta'):
  guid=re.search(rb'^guid: ([0-9a-f]{32})$',d,re.M);assert guid,n;guids.append(guid[1])
 if n.endswith('.cs'):assert n+'.meta' in files,n
assert len(guids)==len(set(guids))
for n in ['README.md','README_CN.md']:
 text=files[n].decode(); anchors=re.findall(r'<a (?:name|id)="([^"]+)"',text)
 assert len(anchors)==len(set(anchors)),n
 for link in re.findall(r'\]\(([^)]+)\)',text):
  if link.startswith('#'):assert link[1:] in anchors,(n,link)
  elif '://' not in link and not link.startswith('mailto:'):
   path=link.split('#')[0]
   if path:assert (root/path).exists(),(n,link)
manifest={'source_commit':subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip(),
 'validation':'Managed runtime and compiled metadata fixtures; no Unity execution or performance acceptance',
 'files':{n:hashlib.sha256(d).hexdigest() for n,d in sorted(files.items())}}
version=json.loads(files['package.json'])['version'];name='com.acced.zframework.gameplaytags-'+version+'.tgz'
buffer=io.BytesIO()
with gzip.GzipFile(fileobj=buffer,mode='wb',mtime=0) as gz:
 with tarfile.open(fileobj=gz,mode='w') as t:
  for n,d in sorted(files.items()):
   info=tarfile.TarInfo('package/'+n);info.size=len(d);info.mode=0o644;info.mtime=0;t.addfile(info,io.BytesIO(d))
data=buffer.getvalue();(out/name).write_bytes(data)
(out/(name+'.sha256')).write_text(hashlib.sha256(data).hexdigest()+'  '+name+'\n')
(out/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
with tarfile.open(out/name) as t:
 members=t.getmembers();assert len(members)==len(files)
 for m in members:assert t.extractfile(m).read()==files[m.name[8:]]
with zipfile.ZipFile(out/'source.zip','w',compression=zipfile.ZIP_DEFLATED) as z:
 for n,d in sorted(files.items()):z.writestr(n,d)
print('PASS package',name,'files',len(files),'GUIDs',len(guids),'SHA256',hashlib.sha256(data).hexdigest())
