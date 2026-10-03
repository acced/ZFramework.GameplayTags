#!/usr/bin/env python3
"""Focused reporter integrity checks; no benchmark or source mutations."""
import copy,math,sys
from analyze import check_row,expected_keys
sample=dict(sample=0,elapsed_ms=5,ns_per_op=5,bytes_per_op=0,total_bytes=0,gcs=[0,0,0],valid=True)
row=dict(samples=[dict(sample, sample=i) for i in range(7)],iterations=1000000,op='query64',valid=True)
assert check_row(row)
def rejected(name,change):
 bad=copy.deepcopy(row);change(bad)
 try:check_row(bad)
 except ValueError:print('PASS',name);return
 raise AssertionError('accepted '+name)
rejected('truncated sample list',lambda r:r['samples'].pop())
rejected('duplicate sample',lambda r:r['samples'][1].update(sample=0))
rejected('NaN timing',lambda r:r['samples'][0].update(ns_per_op=float('nan')))
rejected('zero timing',lambda r:r['samples'][0].update(elapsed_ms=0,ns_per_op=0))
rejected('allocation mismatch',lambda r:r['samples'][0].update(total_bytes=24))
rejected('prepared allocation',lambda r:r['samples'][0].update(total_bytes=24,bytes_per_op=.000024))
rejected('claimed short sample validity',lambda r:r['samples'][0].update(elapsed_ms=.5,ns_per_op=.5))
bad=copy.deepcopy(row);bad['valid']=False;bad['samples'][0].update(elapsed_ms=.5,ns_per_op=.5,valid=False);assert not check_row(bad);print('PASS preserved invalid short sample')
bad=copy.deepcopy(row);bad['op']='build';bad['samples'][0].update(total_bytes=67108865,bytes_per_op=67.108865);assert not check_row(bad);print('PASS preserved over-budget sample')
assert [len(expected_keys(g)) for g in ['query','builder','mixed']]==[37,30,75]
print('PASS fixed independent matrix identities')
