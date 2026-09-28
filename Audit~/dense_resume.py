#!/usr/bin/env python3
"""Same-protocol comparison against the fixed pre-resumption tree; never selects winners."""
import argparse
import pathlib
import dense_results

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--reference', required=True)
parser.add_argument('--results', required=True)
parser.add_argument('--rounds', type=int, default=3)
args = parser.parse_args()
if args.rounds < 1:
    parser.error('rounds must be positive')

# The child benchmark executes in its temporary build directory. Pass absolute
# archive/output paths instead of interpreting repository-relative paths there.
reference = pathlib.Path(args.reference).resolve()
output = pathlib.Path(args.results).resolve() / 'resumption'
output.mkdir(parents=True, exist_ok=True)
dense_results.INITIAL = '3939cb8cb55746d37d5db0c6ab9c7483415a26f8'
dense_results.INITIAL_TREE = '789cb0e6eaf5680f4366732a90fad9d941190199'
dense_results.refinement(reference, output, 'dotnet', args.rounds)
