#!/usr/bin/env python3
"""Independently rebuild every median, allocation and A/A comparison from raw rows."""
import pathlib,sys,importlib.util,json,hashlib
HERE=pathlib.Path(__file__).resolve().parent;ROOT=HERE.parents[1]
spec=importlib.util.spec_from_file_location("scoped_review_generation",HERE/"generate.py")
generation=importlib.util.module_from_spec(spec);spec.loader.exec_module(generation)
def main():
    program=(ROOT/"Audit~/MixedOperations/review.py").read_text()
    program=program.replace("from generate import EXPECTED,cursor,dense_result","# deterministic generation supplied by caller")
    start=program.index('    g=out/"generated"; original=')
    end=program.index('    for name,code in expected.items():',start)
    program=program[:start]+'''    g=out/"generated"
    original=(g/"base/PR14_DirectArraySet.cs").read_text()
    assert sha((g/"base/PR14_DirectArraySet.cs").read_bytes())==EXPECTED["micro/DirectArraySet.cs"]
    expected=variant_sources(original)
'''+program[end:]
    program=program.replace('["control","cursor","dense"]','["control","union","copy"]')
    program=program.replace('"reuse_copy_append","reuse_copy_remove")','"reuse_copy_append","reuse_copy_remove","reuse_copyfrom")')
    program=program.replace('("fresh_union","union_into","enumerate","copy_append","copy_remove")','("fresh_union","union_into","enumerate","copy_append","copy_remove","fresh_copyfrom","reuse_copyfrom")')
    ns={"__file__":str(HERE/"review_driver.py"),"__name__":"scoped_review_driver",
        "EXPECTED":generation.EXPECTED,"variant_sources":generation.variant_sources}
    exec(compile(program,str(HERE/"review_driver.py"),"exec"),ns)
    ns["main"]()
    root=pathlib.Path(sys.argv[sys.argv.index("--root")+1]);out=root/"results"
    m=json.loads((out/"manifest.json").read_text())
    spec2=importlib.util.spec_from_file_location("scoped_fixture_review",HERE/"run.py")
    runner=importlib.util.module_from_spec(spec2);spec2.loader.exec_module(runner)
    assert (out/"harness/Probe.cs").read_text()==runner.probe_text()
    check=json.loads((out/"recheck.json").read_text())
    for name,test in check["tests"].items():
        if "cursorBytes" in test:
            assert test["cursorBytes"]==16 and test["enumeratorBytes"]==32
            assert test["scopedAssertions"]>0
            if name!="control-tests.json":assert test["privateCursorBytes"]>0
    check.update(public_records_bytes=16,public_enumerator_bytes=32,
        public_layout_and_source_unchanged=True,
        control_identity="PR15 direct-Dense-result build; not the rejected combined cursor",
        harness_sha256=hashlib.sha256((out/"harness/Probe.cs").read_bytes()).hexdigest(),
        private_cursor_scope="mixed independent Micro Union and/or CopyFrom only")
    (out/"recheck.json").write_text(json.dumps(check,indent=2))
    print("SCOPED_RECHECK "+json.dumps({k:v for k,v in check.items() if k not in ("tests","groups","AA")}))
if __name__=="__main__":main()
