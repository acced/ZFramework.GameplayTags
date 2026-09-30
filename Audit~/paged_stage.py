#!/usr/bin/env python3
"""Explicit staged reference selection. Never rewrite kernel or timed library source."""
import json,pathlib,sys
import paged_pilot as pilot

core='--tests-only' in sys.argv
omit='--without-alex' in sys.argv
sys.argv=[a for a in sys.argv if a not in ('--tests-only','--without-alex')]
all_pins=dict(pilot.PINS)
if core:
    pilot.PINS={'auto':pilot.PINS['auto']}
    pilot.BASELINES=['auto']
    sys.argv.append('--skip-bench')
elif omit:
    pilot.PINS={k:v for k,v in pilot.PINS.items() if k!='alex'}
    pilot.BASELINES=[k for k in pilot.BASELINES if k!='alex']
out=pathlib.Path(sys.argv[sys.argv.index('--output')+1]).resolve();out.mkdir(parents=True,exist_ok=True)
selection={'stage':'correctness-only' if core else 'comparison','included':pilot.PINS,
    'omitted':{k:v for k,v in all_pins.items() if k not in pilot.PINS},
    'omission_reason':'Core tests have no external reference dependency.' if core else
        ('Bounded external fetch failed; losing to included Auto is sufficient to reject, never sufficient to approve.' if omit else None),
    'all_references_available':not core and not omit,'production_promoted':False,'release_approved':False}
(out/'reference-selection.json').write_text(json.dumps(selection,indent=2))
print('REFERENCE_SELECTION '+json.dumps(selection),flush=True)
pilot.main()
