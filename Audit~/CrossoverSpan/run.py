#!/usr/bin/env python3
"""Supplement only: BCL Span.IndexOf vs the same real query caller, no bulk rerun."""
import pathlib,sys,json,hashlib
ROOT=pathlib.Path(__file__).resolve().parents[2]
HERE=ROOT/'Audit~/Crossover';sys.path.insert(0,str(HERE));import prepare
original_generate=prepare.generate

def with_span(out):
 variants,micro=original_generate(out);base=variants['binary'];out=pathlib.Path(out)
 expression='System.MemoryExtensions.IndexOf(new Span<int>(m_Ids, 0, m_Count), id) >= 0'
 for label,expr in [('span',expression),('span32','m_Count <= 32 ? '+expression+' : IndexOf(id) >= 0'),('span64','m_Count <= 64 ? '+expression+' : IndexOf(id) >= 0')]:
  code=base.replace(': IndexOf(id) >= 0;',': ('+expr+');');assert code!=base
  folder=out/label;folder.mkdir(exist_ok=True);(folder/'RuntimeTagSet.cs').write_text(code);variants[label]=code
 (out/'span-manifest.json').write_text(json.dumps({'all_candidates_sha256':{k:hashlib.sha256(v.encode()).hexdigest() for k,v in variants.items()},'policy':'span32 and span64 are predeclared alternatives, not per-row picking','api':'.NET Standard 2.1 MemoryExtensions.IndexOf; target backend still needs validation'},indent=2))
 return variants,micro
prepare.generate=with_span
text=(HERE/'run.py').read_text()
text=text.replace("('original','binary','linear','vector','bounded')","('original','binary','linear','vector','bounded','span','span32','span64')")
text=text.replace("('binary','linear','vector','bounded')","('binary','linear','vector','bounded','span','span32','span64')")
text=text.replace("('vector','bounded','micro')","('vector','bounded','micro','span','span32','span64')")
text=text.replace("[('binary','query'),('vector','query'),('bounded','query'),('dense','sets')]","[('binary','query'),('vector','query'),('span','query'),('span32','query'),('span64','query')]")
if '--stage' not in sys.argv:sys.argv+=['--stage','query']
exec(compile(text,str(HERE/'run.py'),'exec'),{'__file__':str(HERE/'run.py'),'__name__':'__main__'})
