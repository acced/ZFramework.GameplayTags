# Bulk-operation regression tests

Run with .NET SDK 8:

```sh
dotnet run --project Tests~/Bulk/GameplayTags.Bulk.Tests.csproj -c Release
```

The executable references the portable runtime as a separate assembly. It uses
only public APIs. Expected explicit membership comes from ordinal
`HashSet<string>` operations; expected implicit ancestors come from splitting
the registered names on `.`. Counted-input expectations use a separate
`Dictionary<string, int>`. No expected result is derived from runtime IDs,
registry hierarchy metadata or container storage.

The five groups cover:

- Copy with small, equal or different requested capacities, large reservations,
  targets and sources that previously grew and then shrank, empty sources, and
  independent changes to both source and destination after copying. A specific
  dense same-capacity case deletes 120 tags and inserts 120 different tags in
  both inputs before copying, then completely drains each side independently.
  Another case repeatedly copies smaller hash and linear sources over a populated
  reserved target, grows and deletes within the reservation, clears the linear
  state and grows again. Every insertion and removal checks for stale retained
  hash slots becoming visible when a previously smaller active index expands.
- Union and explicit intersection at 0%, 25%, 50% and 100% overlap of the smaller
  input, asymmetric input sizes, shared implicit ancestors, explicit ancestor
  and descendant combinations, case-sensitive names and 64-level paths.
- Copy and intersection where output aliases either or both inputs, including
  interface references and value-type proxy inputs that expose the same storage.
  New union and intersection results must remain independent of their inputs.
- Counted inputs with different multiplicities, normalized to a single explicit
  occurrence in ordinary-set outputs, with the counted sources unchanged.
- Deterministic random bulk construction followed by additions, duplicate
  additions, missing removals, random removals, complete draining and reuse.

Checking closure membership immediately after construction is insufficient to
validate ancestor reference counts. The tests remove explicit tags in shuffled
order and compare the full closure after every mutation, including the final
removal of a shared ancestor's last contributor. Fixed seeds are printed in the
result to make failures reproducible. These are behavior checks, not timing or
allocation benchmarks.

`Tests~/DeepHierarchy/Program.cs` separately checks a 3000-level, single-explicit-tag
counted input through ordinary-set Copy, Union and Intersection, including output
aliasing and full removal. It also checks intersections that discard a deep path
and retain only a shallow tag. A queued-union regression adds 14 strict ancestors
in ascending, descending and mixed insertion orders, including parents made
explicit after a child has already contributed to them. Ordinary and counted
sources remain unchanged; each result must drain completely after removing each
explicit tag once. Per-removal checks inspect the 15 explicit candidates and
expected closure depth; source counts use a linear suffix-sum model. Keeping these
cases in the separate process avoids expanding the name universe of every
randomized Bulk assertion.

## Direct compiler fallback and cross-revision validation

For environments where the .NET host works but the SDK CLI or MSBuild does not:

```sh
python3 Tests~/Bulk/run.py --dotnet /path/to/dotnet
```

The fallback uses the installed SDK's compiler and reference packs, builds the
portable runtime with the existing `Tests~/run.py build` command, and builds the
test executable against that assembly. It does not download dependencies,
compile the source generator, or modify the selected runtime sources.

To run this same harness against an earlier runtime checkout:

```sh
python3 Tests~/Bulk/run.py --dotnet /path/to/dotnet \
  --source /path/to/earlier-checkout \
  --output-dir artifacts/bulk-tests-earlier
```

The test source always comes from the checkout containing this script;
`--source` selects only the runtime implementation. Keep distinct output
directories when comparing revisions. CI runs the ordinary project command.
