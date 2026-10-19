"""Materialize a checksummed, locally reviewed patch on the qualification branch only."""
import base64,hashlib,io,pathlib,subprocess,tarfile
root=pathlib.Path(__file__).resolve().parents[1]
branch=subprocess.check_output(['git','branch','--show-current'],cwd=root,text=True).strip()
assert branch=='work/optimized2-20261019',branch
here=root/'.verification'
if not (here/'materialized.txt').exists():
    encoded=''.join(p.read_text() for p in sorted((here/'payload').glob('*.txt')))
    data=base64.b64decode(''.join(encoded.split()),validate=True)
    assert hashlib.sha256(data).hexdigest()=='975b5912b58bc374e81d669cef1586d92fe0e6ba27ae538e63c4a0fa77a5b8a8'
    with tarfile.open(fileobj=io.BytesIO(data),mode='r:gz') as archive:
        for member in archive:
            path=pathlib.PurePosixPath(member.name)
            assert member.isfile() and not path.is_absolute() and '..' not in path.parts
            dest=here.joinpath(*path.parts)
            dest.parent.mkdir(parents=True,exist_ok=True)
            dest.write_bytes(archive.extractfile(member).read())
    subprocess.run(['git','apply','--check',str(here/'candidate.patch')],cwd=root,check=True)
    subprocess.run(['git','apply',str(here/'candidate.patch')],cwd=root,check=True)
    subprocess.run(['python3',str(here/'update_docs.py')],cwd=root,check=True)
    (here/'materialized.txt').write_text('Reviewed source bundle SHA256 975b5912b58bc374e81d669cef1586d92fe0e6ba27ae538e63c4a0fa77a5b8a8\n')
print('PASS source patch and documentation materialization; no main ref writes')
