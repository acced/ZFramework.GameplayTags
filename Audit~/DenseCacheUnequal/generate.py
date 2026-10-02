from pathlib import Path
import importlib.util
root=Path(__file__).resolve().parents[2]
spec=importlib.util.spec_from_file_location('cache_frozen',root/'Audit~/DenseCache/generate.py')
parent=importlib.util.module_from_spec(spec);spec.loader.exec_module(parent)
generate,BASE=parent.generate,parent.BASE
