#!/usr/bin/env python3
"""Reuse the exact PR18 tests/measurement; change only candidate selection."""
from pathlib import Path
root=Path(__file__).resolve().parents[2]
code=(root/'Audit~/DenseBulk/run.py').read_text()
assert "HERE/'Extra.cs'" in code
code=code.replace("HERE/'Extra.cs'","ROOT/'Audit~/DenseBulk/Extra.cs'")
code=code.replace("dlls['combined']","dlls['bounded']").replace("files['combined']","files['bounded']")
# Hardware-disabled/portable tests must target the new implementation, not its control.
code=code.replace("'combined','tests'","'bounded','tests'")
exec(compile(code,str(root/'Audit~/DenseBulk/run.py'),'exec'))
