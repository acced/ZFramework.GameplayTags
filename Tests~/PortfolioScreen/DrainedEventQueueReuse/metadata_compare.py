"""Compare MetadataGate v1 snapshots without loading or executing either assembly.

Prepared source-only. Integration:
    report = compare_metadata(baseline_path, candidate_path, report_output_path)
The function writes a deterministic JSON report, returns it, and never runs a target.
CLI exit codes: 0 = PASS, 1 = FAIL, 2 = invalid/unreadable snapshot or report path.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
from typing import Any

SCHEMA = "two-capacity-metadata-v1"
EXPECTED_ADDITIONS = {
    "GameplayTags.GameplayTagContainer::.ctor(System.Int32,System.Int32):System.Void": 0x1886,
    "GameplayTags.GameplayTagContainer::EnsureCapacity(System.Int32,System.Int32):System.Void": 0x86,
    "GameplayTags.GameplayTagCountContainer::.ctor(System.Int32,System.Int32):System.Void": 0x1886,
    "GameplayTags.GameplayTagCountContainer::EnsureCapacity(System.Int32,System.Int32):System.Void": 0x86,
    "GameplayTags.TagStorage::EnsureCapacity(System.Int32,System.Int32):System.Void": 0x83,
}
TAG_STORAGE_CTOR = "GameplayTags.TagStorage::.ctor():System.Void"
CONTAINER_CCTOR = "GameplayTags.GameplayTagContainer::.cctor():System.Void"
RAW_KEYS = frozenset({
    "metadata_token", "local_signature_token", "local_signature_raw", "il_raw", "operand_raw",
})
TOKEN_OPERANDS = frozenset({
    "InlineMethod", "InlineField", "InlineType", "InlineTok", "InlineString", "InlineSig",
})


def _normal(value: Any) -> Any:
    """Only token/layout encodings are ignored; every semantic field is retained."""
    if isinstance(value, dict):
        return {k: _normal(v) for k, v in value.items() if k not in RAW_KEYS}
    if isinstance(value, list):
        return [_normal(v) for v in value]
    return value


def _differences(left: Any, right: Any, path: str = "") -> list[dict[str, Any]]:
    if type(left) is not type(right):
        return [{"path": path, "baseline": left, "candidate": right}]
    if isinstance(left, dict):
        result = []
        for key in sorted(left.keys() | right.keys()):
            next_path = f"{path}.{key}" if path else key
            if key not in left or key not in right:
                result.append({"path": next_path, "baseline": left.get(key), "candidate": right.get(key),
                               "missing_from": "baseline" if key not in left else "candidate"})
            else:
                result.extend(_differences(left[key], right[key], next_path))
        return result
    if isinstance(left, list):
        if len(left) != len(right):
            return [{"path": path, "baseline": left, "candidate": right}]
        return [change for i, (a, b) in enumerate(zip(left, right))
                for change in _differences(a, b, f"{path}[{i}]")]
    return [] if left == right else [{"path": path, "baseline": left, "candidate": right}]


def _index(items: list[dict[str, Any]], label: str) -> dict[str, dict[str, Any]]:
    result = {}
    for item in items:
        name = item["id"]
        if name in result:
            raise ValueError(f"Duplicate {label}: {name}")
        result[name] = item
    return result


def _load(path: str | Path) -> tuple[dict[str, Any], str]:
    raw = Path(path).read_bytes()
    value = json.loads(raw)
    if value.get("schema") != SCHEMA:
        raise ValueError(f"Unsupported metadata schema in {path}")
    if value.get("assembly", "").split(",", 1)[0] != "Test":
        raise ValueError(f"Assembly in {path} is not named Test")
    if not value.get("types"):
        raise ValueError(f"No Runtime types in {path}")
    for item in value["types"]:
        if not item["id"].startswith("GameplayTags."):
            raise ValueError(f"Out-of-scope Runtime type in {path}: {item['id']}")
        _index(item["fields"], "field")
        _index(item["properties"], "property")
        _index(item["events"], "event")
        methods = _index(item["methods"], "method")
        tokens = [int(m["metadata_token"], 16) for m in methods.values()]
        if len(set(tokens)) != len(tokens) or any(t >> 24 != 6 for t in tokens):
            raise ValueError(f"Invalid or duplicate MethodDef token in {item['id']}")
        if [m["ordinal"] for m in item["methods"]] != list(range(len(methods))):
            raise ValueError(f"Method ordinals are not contiguous in {item['id']}")
        if tokens != sorted(tokens):
            raise ValueError(f"Method tokens are out of order in {item['id']}")
    return value, hashlib.sha256(raw).hexdigest()


def _token_changes(left: Any, right: Any, path: str = "") -> list[dict[str, Any]]:
    """Record token changes even when normalized metadata is identical."""
    if isinstance(left, dict) and isinstance(right, dict):
        result = []
        for key in sorted(left.keys() & right.keys()):
            at = f"{path}.{key}" if path else key
            a, b = left[key], right[key]
            if key in {"metadata_token", "local_signature_token"} and a != b:
                result.append({"path": at, "baseline": a, "candidate": b})
            elif key not in RAW_KEYS:
                result.extend(_token_changes(a, b, at))
        return result
    if isinstance(left, list) and isinstance(right, list) and len(left) == len(right):
        return [change for i, (a, b) in enumerate(zip(left, right))
                for change in _token_changes(a, b, f"{path}[{i}]")]
    return []


def _validate_raw_body(body: dict[str, Any] | None, method_id: str) -> None:
    if body is None:
        return
    raw = bytes.fromhex(body["il_raw"])
    if len(raw) != body["il_length"]:
        raise ValueError(f"IL length mismatch: {method_id}")
    position = 0
    for ins in body["instructions"]:
        if ins["offset"] != position:
            raise ValueError(f"Noncontiguous instruction offsets: {method_id}")
        op_bytes = 2 if ins["opcode_value"] > 0xFF else 1
        code_bytes = ins["opcode_value"].to_bytes(op_bytes, "big")
        if raw[position:position + op_bytes] != code_bytes:
            raise ValueError(f"Opcode bytes disagree with decoded instruction: {method_id}")
        operand_offset = position + op_bytes
        if ins["operand_offset"] != operand_offset:
            raise ValueError(f"Bad operand offset: {method_id}")
        end = position + ins["size"]
        if end < operand_offset or end > len(raw):
            raise ValueError(f"Bad instruction size: {method_id}")
        if raw[operand_offset:end].hex() != ins["operand_raw"]:
            raise ValueError(f"Raw operand disagrees with IL bytes: {method_id}")
        token = ins["metadata_token"]
        if ins["operand_kind"] in TOKEN_OPERANDS:
            if end - operand_offset != 4 or token is None or int(token, 16) != int.from_bytes(raw[operand_offset:end], "little"):
                raise ValueError(f"Malformed token operand: {method_id}")
        elif token is not None:
            raise ValueError(f"Unexpected token on nontoken operand: {method_id}")
        position = end
    if position != len(raw):
        raise ValueError(f"Decoded instructions do not cover all IL bytes: {method_id}")


def _raw_body_changes(left: dict[str, Any] | None, right: dict[str, Any] | None) -> tuple[list, list, list]:
    token_rows, signature_rows, illegal_changes = [], [], []
    if left is None or right is None:
        return token_rows, signature_rows, illegal_changes
    a = bytes.fromhex(left["il_raw"])
    b = bytes.fromhex(right["il_raw"])
    if len(a) != len(b) or len(left["instructions"]) != len(right["instructions"]):
        if a != b:
            illegal_changes.append({"reason": "Instruction or byte count changed."})
        return token_rows, signature_rows, illegal_changes
    permitted_bytes: set[int] = set()
    for old, new in zip(left["instructions"], right["instructions"]):
        if old["operand_raw"] == new["operand_raw"]:
            continue
        resolved_identical = _normal(old) == _normal(new)
        permitted = old["operand_kind"] in TOKEN_OPERANDS and resolved_identical
        row = {
            "offset": old["offset"], "opcode": old["opcode"], "operand_kind": old["operand_kind"],
            "baseline_token": old["metadata_token"], "candidate_token": new["metadata_token"],
            "baseline_raw": old["operand_raw"], "candidate_raw": new["operand_raw"],
            "resolved_operand_identical": resolved_identical,
            "resolved_operand": old["operand"],
            "status": "EXPECTED_TOKEN_RENUMBERING" if permitted else "FAIL",
        }
        if permitted:
            permitted_bytes.update(range(old["operand_offset"], old["operand_offset"] + 4))
            token_rows.append(row)
        else:
            illegal_changes.append(row)
    unexpected_offsets = [i for i, (x, y) in enumerate(zip(a, b)) if x != y and i not in permitted_bytes]
    if unexpected_offsets:
        illegal_changes.append({"reason": "IL bytes changed outside resolved-identical metadata token operands.",
                                "byte_offsets": unexpected_offsets})
    if left["local_signature_raw"] != right["local_signature_raw"]:
        identical = left["local_signature"] == right["local_signature"]
        signature_rows.append({
            "baseline_raw": left["local_signature_raw"], "candidate_raw": right["local_signature_raw"],
            "resolved_signature_identical": identical,
            "resolved_signature": left["local_signature"],
            "status": "EXPECTED_TYPE_TOKEN_RENUMBERING" if identical else "FAIL",
        })
        if not identical:
            illegal_changes.append({"reason": "Normalized local signature changed."})
    return token_rows, signature_rows, illegal_changes


def _new_method_issues(method: dict[str, Any], expected_attributes: int) -> list[str]:
    issues = []
    if method["attributes"] != expected_attributes:
        issues.append("Unexpected method access/flags.")
    if method["implementation_flags"] != 0:
        issues.append("Expected managed IL implementation flags (0).")
    if method["generic_parameters"] or method["signature"]["generic_arguments"]:
        issues.append("Expected nongeneric method.")
    if method["signature"]["static"] or method["signature"]["calling_convention"] != 33:
        issues.append("Expected an ordinary instance method calling convention.")
    if not method["signature"]["return_type"].endswith("]System.Void"):
        issues.append("Expected System.Void return type.")
    parameters = method["parameters"]
    if len(parameters) != 2:
        issues.append("Expected exactly two parameters.")
    else:
        for index, name in enumerate(("totalCapacity", "explicitCapacity")):
            parameter = parameters[index]
            if parameter["name"] != name or parameter["position"] != index:
                issues.append(f"Incorrect parameter {index} name/position.")
            if not parameter["parameter_type"].endswith("]System.Int32"):
                issues.append(f"Incorrect parameter {index} type.")
            if parameter["attributes"] or parameter["has_default"] or parameter["required_modifiers"] or parameter["optional_modifiers"]:
                issues.append(f"Unexpected parameter {index} flags/default/modifiers.")
    if method["body"] is None:
        issues.append("Added method has no IL body.")
    return issues


def compare_metadata(baseline_path: str | Path, candidate_path: str | Path,
                     report_output_path: str | Path) -> dict[str, Any]:
    """Write/return a strict, deterministic semantic gate and explicit token movement report."""
    baseline, baseline_sha = _load(baseline_path)
    candidate, candidate_sha = _load(candidate_path)
    old_types = _index(baseline["types"], "type")
    new_types = _index(candidate["types"], "type")
    old_methods = _index([m for t in old_types.values() for m in t["methods"]], "global method")
    new_methods = _index([m for t in new_types.values() for m in t["methods"]], "global method")
    for name, method in list(old_methods.items()) + list(new_methods.items()):
        _validate_raw_body(method["body"], name)
    failures: list[dict[str, Any]] = []
    removed_types = sorted(old_types.keys() - new_types.keys())
    added_types = sorted(new_types.keys() - old_types.keys())
    removed_methods = sorted(old_methods.keys() - new_methods.keys())
    added_methods = sorted(new_methods.keys() - old_methods.keys())
    if baseline["assembly"] != candidate["assembly"]:
        failures.append({"check": "assembly_identity", "baseline": baseline["assembly"], "candidate": candidate["assembly"]})
    if baseline["selection"] != candidate["selection"]:
        failures.append({"check": "runtime_selection", "reason": "Runtime selection rules differ."})
    if added_types or removed_types:
        failures.append({"check": "runtime_type_set", "added": added_types, "removed": removed_types})
    if removed_methods or set(added_methods) != EXPECTED_ADDITIONS.keys():
        failures.append({"check": "exact_five_added_methods", "added": added_methods, "removed": removed_methods,
                         "expected": sorted(EXPECTED_ADDITIONS)})
    for required in ("GameplayTags.TagStorage", "GameplayTags.GameplayTagContainer", "GameplayTags.GameplayTagCountContainer"):
        if required not in old_types or required not in new_types:
            failures.append({"check": "required_runtime_type", "name": required})

    type_checks, order_checks = [], []
    for name in sorted(old_types.keys() & new_types.keys()):
        old, new = old_types[name], new_types[name]
        old_meta = {k: v for k, v in old.items() if k != "methods"}
        new_meta = {k: v for k, v in new.items() if k != "methods"}
        differences = _differences(_normal(old_meta), _normal(new_meta))
        row = {"name": name, "status": "FAIL" if differences else "PASS", "differences": differences,
               "beforefieldinit_baseline": old["beforefieldinit"], "beforefieldinit_candidate": new["beforefieldinit"],
               "raw_metadata_token_changes": _token_changes(old_meta, new_meta)}
        type_checks.append(row)
        if differences:
            failures.append({"check": "old_type_metadata", "name": name, "differences": differences})
        old_order = [m["id"] for m in old["methods"]]
        new_old_order = [m["id"] for m in new["methods"] if m["id"] in old_methods]
        order_ok = old_order == new_old_order
        order_checks.append({"name": name, "status": "PASS" if order_ok else "FAIL",
                             "baseline_old_method_order": old_order, "candidate_old_method_order": new_old_order})
        if not order_ok:
            failures.append({"check": "old_method_relative_order", "name": name})

    method_checks, movements = [], []
    for name in sorted(old_methods.keys() & new_methods.keys()):
        old, new = old_methods[name], new_methods[name]
        old_meta = {k: v for k, v in old.items() if k != "ordinal"}
        new_meta = {k: v for k, v in new.items() if k != "ordinal"}
        differences = _differences(_normal(old_meta), _normal(new_meta))
        operand_changes, signature_changes, illegal = _raw_body_changes(old["body"], new["body"])
        row = {"name": name, "status": "FAIL" if differences or illegal else "PASS",
               "normalized_differences": differences, "illegal_raw_il_changes": illegal,
               "raw_metadata_token_changes": _token_changes(old_meta, new_meta),
               "il_token_operand_changes": operand_changes, "local_signature_encoding_changes": signature_changes,
               "raw_il_identical": (old["body"] or {}).get("il_raw") == (new["body"] or {}).get("il_raw")}
        method_checks.append(row)
        if differences or illegal:
            failures.append({"check": "old_method_equivalence", "name": name,
                             "normalized_differences": differences, "illegal_raw_il_changes": illegal})
        if old["metadata_token"] != new["metadata_token"] or old["ordinal"] != new["ordinal"]:
            movements.append({"name": name, "baseline_token": old["metadata_token"], "candidate_token": new["metadata_token"],
                              "methoddef_row_delta": int(new["metadata_token"], 16) - int(old["metadata_token"], 16),
                              "baseline_type_ordinal": old["ordinal"], "candidate_type_ordinal": new["ordinal"],
                              "type_ordinal_delta": new["ordinal"] - old["ordinal"]})

    # Check ordering globally too; appending definitions shifts global MethodDef rows,
    # but must not reorder the pre-existing Runtime methods among themselves.
    old_global_order = sorted(old_methods, key=lambda name: int(old_methods[name]["metadata_token"], 16))
    new_global_old_order = sorted((name for name in new_methods if name in old_methods),
                                  key=lambda name: int(new_methods[name]["metadata_token"], 16))
    global_order_ok = old_global_order == new_global_old_order
    if not global_order_ok:
        failures.append({"check": "global_old_method_relative_order", "reason": "Old Runtime MethodDef ordering changed."})

    addition_checks = []
    for name, flags in sorted(EXPECTED_ADDITIONS.items()):
        issues = _new_method_issues(new_methods[name], flags) if name in new_methods else ["Missing method."]
        if name in old_methods:
            issues.append("Method already existed in baseline.")
        addition_checks.append({"name": name, "status": "FAIL" if issues else "PASS", "issues": issues,
                                "candidate_metadata_token": new_methods.get(name, {}).get("metadata_token")})
        if issues:
            failures.append({"check": "added_method_contract", "name": name, "issues": issues})

    method_status = {item["name"]: item["status"] for item in method_checks}
    old_storage_ctors = sorted(name for name in old_methods if name.startswith("GameplayTags.TagStorage::.ctor("))
    new_storage_ctors = sorted(name for name in new_methods if name.startswith("GameplayTags.TagStorage::.ctor("))
    ctor_ok = (old_storage_ctors == new_storage_ctors == [TAG_STORAGE_CTOR] and
               method_status.get(TAG_STORAGE_CTOR) == "PASS" and
               old_methods[TAG_STORAGE_CTOR]["ordinal"] == new_methods[TAG_STORAGE_CTOR]["ordinal"])
    ctor_check = {"name": TAG_STORAGE_CTOR, "status": "PASS" if ctor_ok else "FAIL",
                  "baseline_constructors": old_storage_ctors, "candidate_constructors": new_storage_ctors,
                  "explanation": "The explicit public empty constructor replaces the original synthesized constructor; it must preserve the existing definition, per-type position, metadata, initializers, and normalized IL. It is not a sixth added method."}
    if not ctor_ok:
        failures.append({"check": "tagstorage_constructor_equivalence", "detail": ctor_check})

    old_cctor, new_cctor = old_methods.get(CONTAINER_CCTOR), new_methods.get(CONTAINER_CCTOR)
    cctor_ok = (old_cctor is not None and new_cctor is not None and
                method_status.get(CONTAINER_CCTOR) == "PASS" and new_cctor["ordinal"] - old_cctor["ordinal"] == 2 and
                old_types["GameplayTags.GameplayTagContainer"]["beforefieldinit"] is True and
                new_types["GameplayTags.GameplayTagContainer"]["beforefieldinit"] is True)
    cctor_check = {"name": CONTAINER_CCTOR, "status": "PASS" if cctor_ok else "FAIL",
                   "baseline_token": old_cctor and old_cctor["metadata_token"],
                   "candidate_token": new_cctor and new_cctor["metadata_token"],
                   "baseline_type_ordinal": old_cctor and old_cctor["ordinal"],
                   "candidate_type_ordinal": new_cctor and new_cctor["ordinal"],
                   "explanation": "The ordinary container's compiler-synthesized .cctor follows its declared methods. Appending the two public overloads moves that .cctor two per-type MethodDef positions later. Its normalized body and metadata, old-method relative order, and beforefieldinit must remain unchanged. This is expected definition placement, not an explicit static constructor."}
    if not cctor_ok:
        failures.append({"check": "container_synthesized_cctor_placement", "detail": cctor_check})

    report = {
        "schema": "two-capacity-metadata-comparison-v1", "status": "FAIL" if failures else "PASS",
        "baseline": {"path": str(baseline_path), "sha256": baseline_sha},
        "candidate": {"path": str(candidate_path), "sha256": candidate_sha},
        "scope": {
            "included": baseline["selection"],
            "excluded_baseline_types": baseline.get("excluded_types", []),
            "excluded_candidate_types": candidate.get("excluded_types", []),
            "limitations": [
                "Reflection snapshots cover the compiled GameplayTags Runtime type set, including nested compiler-generated types. Conditional branches absent from this build are not tested.",
                "Global <PrivateImplementationDetails> types are explicitly excluded because their fields can combine Runtime and harness contributions; this gate does not claim their bytes were compared.",
                "PE layout/RVAs, MVIDs, debug/PDB metadata, and assembly file byte equality are outside this semantic gate.",
                "New overloads can break name-only reflection GetMethod calls through AmbiguousMatchException; the functional harness records this separately.",
            ],
        },
        "counts": {"baseline_types": len(old_types), "candidate_types": len(new_types),
                   "baseline_methods": len(old_methods), "candidate_methods": len(new_methods),
                   "added_methods": len(added_methods), "removed_methods": len(removed_methods),
                   "old_methods_compared": len(method_checks), "old_methods_passed": sum(r["status"] == "PASS" for r in method_checks),
                   "methoddef_movements": len(movements),
                   "il_token_operand_changes": sum(len(r["il_token_operand_changes"]) for r in method_checks)},
        "added_types": added_types, "removed_types": removed_types,
        "added_methods": added_methods, "removed_methods": removed_methods,
        "added_method_checks": addition_checks, "old_type_checks": type_checks,
        "old_method_checks": method_checks, "old_method_order_checks": order_checks,
        "global_old_method_order": {"status": "PASS" if global_order_ok else "FAIL"},
        "methoddef_movements": movements,
        "methoddef_movement_explanation": "MethodDef tokens are module-wide row indices. Appending exactly five definitions can renumber later Runtime methods and their IL metadata operands. Renumbering is accepted only when normalized old definitions and resolved IL operands stay identical, all changed raw IL bytes are token bytes, and old Runtime method ordering is preserved.",
        "tagstorage_constructor": ctor_check, "container_synthesized_cctor": cctor_check,
        "failures": failures,
    }
    # Deterministic, no timestamps; do not overwrite an earlier evidence report.
    with Path(report_output_path).open("x", encoding="utf-8") as stream:
        json.dump(report, stream, ensure_ascii=True, indent=2, sort_keys=True)
        stream.write("\n")
    return report


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("baseline")
    parser.add_argument("candidate")
    parser.add_argument("report")
    args = parser.parse_args()
    try:
        report = compare_metadata(args.baseline, args.candidate, args.report)
    except (OSError, ValueError, KeyError, TypeError, OverflowError) as error:
        print(json.dumps({"status": "ERROR", "error": str(error)}, sort_keys=True))
        return 2
    print(json.dumps({"status": report["status"], "counts": report["counts"], "report": args.report}, sort_keys=True))
    return 0 if report["status"] == "PASS" else 1


if __name__ == "__main__":
    raise SystemExit(main())
