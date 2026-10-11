#!/usr/bin/env python3
"""Verify public numeric map derivation; no external process or measurement."""
import hashlib,json
from decimal import Decimal,localcontext,ROUND_CEILING
from pathlib import Path
HERE=Path(__file__).resolve().parent

def verify_derivation():
    for backend in ['net','mono']:
        data=json.loads((HERE/'derivation'/f'{backend}.json').read_text());raw=(HERE/'maps'/f'{backend}.tsv').read_bytes()
        mapping={k:int(v)for k,v in(line.split('\t')for line in raw.decode().splitlines())}
        assert hashlib.sha256(raw).hexdigest()==data['MapSHA256']
        assert len(mapping)==45 and len(data['ChangedRows'])==7 and len(data['UnchangedRows'])==38
        assert set(mapping)==set(data['OriginalCounts'])
        pairs={(label,r)for label in data['OriginalStrategyLabels']for r in range(14)}
        assert len(pairs)==70 and len(data['SourceRawHashes'])==70
        changed=set()
        for row in data['ChangedRows']:
            observations=row['OriginalObservations'];assert len(observations)==70
            assert {(x['Label'],x['Round'])for x in observations}==pairs
            assert all(x['RawFile']==f"raw/{x['Label']}-{x['Round']}.json"and x['RawFile']in data['SourceRawHashes']for x in observations)
            with localcontext()as context:
                context.prec=80
                fastest=min(Decimal(x['NsLiteral'])for x in observations)
                expected=max(row['OldIterations'],int((Decimal(500000000)/fastest).to_integral_value(rounding=ROUND_CEILING)))
            assert row['OldIterations']==data['OriginalCounts'][row['Id']]
            assert expected==row['NewIterations']==mapping[row['Id']]
            changed.add(row['Id'])
        assert changed.isdisjoint(data['UnchangedRows'])and changed|set(data['UnchangedRows'])==set(mapping)
        assert all(mapping[k]==data['OriginalCounts'][k]for k in data['UnchangedRows'])
    return True
if __name__=='__main__':
    verify_derivation();print('Verified both original-only fixed maps; no measurement performed.')
