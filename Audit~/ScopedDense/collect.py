#!/usr/bin/env python3
"""Redownload exact CI artifacts, rerun reconstruction, retain both unmodified archives."""
import pathlib,os,subprocess,json,hashlib,zipfile,io,csv,sys
ROOT=pathlib.Path(__file__).resolve().parents[2]
def api(path):return subprocess.check_output(["gh","api",path])
def main():
    out=pathlib.Path(sys.argv[1]).resolve();out.mkdir(parents=True,exist_ok=True)
    repo=os.environ["GITHUB_REPOSITORY"];run=os.environ["GITHUB_RUN_ID"]
    docs=json.loads(api(f"/repos/{repo}/actions/runs/{run}/artifacts?per_page=100"))["artifacts"]
    summaries={};source=None;generated=None;allrows=[];comparisons=[]
    for arch in ("x64","arm64"):
        matches=[a for a in docs if a["name"]==f"scoped-dense-{arch}-{run}"]
        assert len(matches)==1
        meta=matches[0];data=api(f"/repos/{repo}/actions/artifacts/{meta['id']}/zip")
        digest=hashlib.sha256(data).hexdigest();assert meta["digest"]=="sha256:"+digest
        (out/(arch+"-raw.zip")).write_bytes(data)
        work=out/arch;work.mkdir(exist_ok=True)
        with zipfile.ZipFile(io.BytesIO(data)) as z:
            for name in z.namelist():
                assert not name.startswith("/") and ".." not in pathlib.PurePosixPath(name).parts
            z.extractall(work)
        res=work/"results"
        old=json.loads((res/"recheck.json").read_text())
        p=subprocess.run(["python3",str(ROOT/"Audit~/ScopedDense/review.py"),"--root",str(work)],text=True,stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
        (out/(arch+"-independent-recheck.log")).write_text(p.stdout)
        if p.returncode:raise RuntimeError(p.stdout[-14000:])
        checked=json.loads((res/"recheck.json").read_text());assert checked==old
        assert checked["head"]==os.environ["GITHUB_SHA"]
        rawsource=(work/"source.zip").read_bytes()
        if source is None:source=rawsource
        else:assert source==rawsource
        gs={str(p.relative_to(res/"generated")):hashlib.sha256(p.read_bytes()).hexdigest() for p in (res/"generated").rglob("*.cs")}
        if generated is None:generated=gs
        else:assert generated==gs
        for filename,dest in (("All_Results.csv",allrows),("Comparisons.csv",comparisons)):
            with (res/filename).open(encoding="utf-8-sig") as f:
                for row in csv.DictReader(f):dest.append(dict(architecture=arch,**row))
        summaries[arch]=dict(artifact_id=meta["id"],archive_sha256=digest,
            **{k:v for k,v in checked.items() if k!="tests"})
        print("SCOPED_COLLECTION_ARCH "+json.dumps({"arch":arch,"artifact":meta["id"],"head":checked["head"],"rows":checked["aggregate_rows"],"samples":checked["timing_samples"],"zero_samples":checked["zero_allocation_samples"]}))
    (out/"GameplayTags_ScopedDense_TestedSource.zip").write_bytes(source)
    for name,rows in (("All_Architectures.csv",allrows),("All_Comparisons.csv",comparisons)):
        with (out/name).open("w",encoding="utf-8-sig",newline="") as f:
            w=csv.DictWriter(f,fieldnames=list(rows[0]));w.writeheader();w.writerows(rows)
    focus=[r for r in comparisons if r["control"]=="control" and r["candidate"] in ("union","copy","combined") and r["universe"]=="262144" and r["members"] in ("8","4096") and r["distribution"] in ("contiguous","scattered") and json.loads(r["shape"])["seed"]==20261031 and r["output"]=="Micro" and r["operation"] in ("fresh_union","union_into","fresh_copyfrom","reuse_copyfrom")]
    negatives=[r for r in comparisons if r["control"]=="control" and r["slower5"]=="True"]
    (out/"focus.json").write_text(json.dumps(focus,indent=2))
    (out/"all_regressions.json").write_text(json.dumps(negatives,indent=2))
    (out/"collection.json").write_text(json.dumps({"head":os.environ["GITHUB_SHA"],"source_and_generated_equal":True,"architectures":summaries,"all_rows":len(allrows),"comparison_rows":len(comparisons),"production_promoted":False},indent=2))
    print("SCOPED_FOCUS "+json.dumps(focus))
    print("SCOPED_REGRESSIONS "+json.dumps(negatives))
    print("SCOPED_COLLECTION_COMPLETE "+os.environ["GITHUB_SHA"])
if __name__=="__main__":main()
