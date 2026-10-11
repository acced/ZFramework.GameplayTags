#!/usr/bin/env python3
"""Pinned original/accepted controls; passes measurement options unchanged to run.py."""
import json
from pathlib import Path
import subprocess
import sys
HERE = Path(__file__).resolve().parent
variants = json.loads((HERE / 'variants.json').read_text())
args = [sys.executable, str(HERE / 'run.py')]
for item in variants:
    args += ['--variant', item['label'] + '=' + str(HERE / item['source']),
             '--revision', item['label'] + '=' + item['revision'],
             '--strategy', item['label'] + '=' + item['strategy']]
raise SystemExit(subprocess.call(args + sys.argv[1:]))
