# Retained failures and limitations

The complete final raw matrix is preserved. Its four median regressions and six invalid controls are listed in [RESULTS.md](RESULTS.md); none was removed, reclassified, averaged away, or replaced by a later diagnostic.

| Historical record | Status retained | Interpretation |
|---|---|---|
| `benchmark/results/final-*` | Four clear median regressions and six invalid allocating controls | Selected final performance has explicit limitations; invalid controls are unranked |
| `benchmark/diagnostics/sorted-history/results` | Original Work + prefix reproduction prerequisite fails | 36 successful processes and valid target samples do not establish the cause of the original Dense sorted-construction loss; the original verdict remains unchanged |
| `benchmark/results/bridge-split-factor-nohw` | Two split/legacy median losses | Both remain explicit; split/old-Direct has no clear median regression in this targeted matrix |
| `benchmark/results/factor-v3-nohw` | Direct v3/v2 has four gains and four losses | The selected targeted v3/baseline comparisons are uncertain; this is not a universal speedup |
| `independent-tests/results/{baseline-checked,combined-checked}` | Three independent groups fail at checked narrowing operations | Earlier source versions fail; later checked fixes have separately bound evidence |
| `independent-tests/results/{baseline-focused,combined-focused}/unionfault` | 292 invalid partial Count states per recorded process | These earlier observation runs deliberately record the pre-existing fault-state issue; a zero runner exit is not a valid-state pass |
| `independent-tests/integration-results/runtime-v2-{net8,standard}-checked/original` | Original suite exits 1 on both backends | Earlier checked integration failure logs are retained, alongside later normalized integration receipts |
| `benchmark/results/query-default` | Incomplete manifest after one calibration | Not an accepted performance cohort; no completed matrix or timing verdict is inferred |
| `independent-tests/runner-hardening-tests.json` | Nonzero exits when reusing populated labels | Expected overwrite-protection checks; recorded evidence remained preserved |

Failure archives contain evidence records and the relevant original receipts. Their historical binaries and some older source snapshots are omitted. The later pass records remain tied to their own source and DLL identities. This publication makes no claim that the entire sequence can be replayed from these selected files.
