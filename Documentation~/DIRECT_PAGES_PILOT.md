# Direct-addressed bitmap pilot — not a shipping refactor

Base: Auto `84729cc3b6e1b4681328806df2d7c8a1d8f568f6`.
The production Runtime, Editor, samples, tests and package version remain unchanged.
The candidate lives in `Audit~/PagedBitmapSet.cs` and is excluded from Unity compilation.

## Contract

This is a genuinely materialized bitmap-page prototype. Four separate builds fix
64, 128, 256 or 512 bits per page; none uses member-ID arrays, rank addressing,
binary member search, a pool, COW, borrowed mutable results or deferred Count.
A single page can be inline at any logical page address, not just low IDs.

The general index is two-level DIRECT addressing: a root array, lazy 64-slot
branch records, and a container-owned arena of fixed-size bitmap pages.
Page addresses are metadata, not member lists. Inserting a member never moves
all later words or adjusts unrelated page offsets. Deleting an empty page swaps
at most one final page and fixes one address. Empty directory nodes enter an
in-container free list and are reused; Clear removes only active mappings.

A page is never internally compressed. Each page header stores its logical page
address and eager population. Two arrays hold the directory and page arena;
there are no heap objects per page. Copies duplicate active data and rebuild
only required directory branches. `EnsureCapacity(n)` reserves enough pages
and branches for ANY placement of n members, bounded by the registry geometry.

This is NOT memory independent of the universe: the root array costs four bytes
per group of 64 logical pages once an instance is materialized. Branches cost
260 bytes each, plus array/object overhead. These costs MUST be measured.
Enumeration is allocation-order, not DFS order. Hierarchical/FrozenQuery/full
public API migration is intentionally not done before this pilot earns it.

## Construction experiment

The default new-union constructor reserves the page-count upper bound and
materializes immediately. Separate `EXACT_RESERVE` builds for 64/256-bit pages
pay an exact-layout sizing pass instead. The same page layout and semantics are
used by both; no candidate receives a fused copy-append operation.

## Validation

Fixed seeds, 4096 exhaustive set pairs, arbitrary relocation, cross-word/page/
branch boundaries, dirty outputs, left/right union/intersection aliases,
independent mutable ownership, foreign registry rejection and eager Count.
Prepared allocation checks include a positive control and settled GC/finalizers.
Production source equality with Auto is enforced before any comparisons.

The original FourWay fixture is retained as a source and adapted only for API
syntax, uniform GC preparation, extra sizes/universe and additional varied-key
queries. Literal adapters are archived. Primary operations remain exact lookup,
independent union, actual copy then append, actual copy then remove. Fresh and
prepared contracts are separate. The extra `exact_mix128` row measures 128
queries per action; it must not be reported as nanoseconds per single query.

References: fixed main, optimize, Alex, Auto, forced flat Dense, rejected indexed
Dense and rejected packed bitmap. All references are exported with Git and
validated against their complete tree. Every implementation runs in every round,
with rotated order. All raw samples, source hashes, failed rows and losses stay
in the artifact. A lower median alone does not establish statistical significance.
The provisional gate requires at least 5% margin in every process round for EACH
primary case. Passing correctness/CI is not passing that gate.

## Algorithm sources

- https://docs.rs/hi_sparse_bitset/latest/hi_sparse_bitset/ : direct child-slot
  indirection and hierarchical bitmap operations. No lazy-expression benchmark
  is substituted for result materialization.
- https://llvm.org/docs/doxygen/classllvm_1_1SmallBitVector.html : inline small
  bitmap principle. No C++ pointer tagging or external code is transplanted.
- https://bitmagic.io/design : paging tradeoffs. No GAP/integer-run fallback.

Native Unity/IL2CPP is not run. A failed pilot must not be promoted to Runtime.
