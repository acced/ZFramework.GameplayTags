# Release acceptance — 2.0.0-pre.2

This is a prerelease candidate. Managed checks, native Unity tests and device performance are separate gates.
Do not tag a stable version or approve release just because the managed workflow is green.

## Changes in this finishing pass

- Freeze validates the same active graph before simplifying, but uses one visitation/result dictionary instead of a separate visitation set. Simple tag roots and single-child groups avoid unnecessary traversal objects/arrays.
- Hierarchy-ordered sorting and in-place reduction replace temporary name/redundancy sets and parent-substring walks. Punctuation, Unicode, redirects, duplicate canonical names and source isolation have regression coverage.
- Loading resolution preserves a container's reserved capacity and is allocation-free for an empty container. Failed resolution remains transactional.
- Repeated small appends use amortized capacity growth; disjoint tail appends use List.AddRange. No shared buffers, pools, runtime caches or new runtime dependencies.
- The name input boundary rejects unpaired UTF-16 surrogates rather than letting generated UTF-8 silently change a name. Valid surrogate pairs remain supported.
- Both READMEs use Freeze; complete example classes compile in CI. Assembly boundaries, friend assemblies, native-test source shapes, metadata and links are checked.
- Native asset reload, Undo/Redo, disabled-Domain-Reload and Player tests are supplied, together with an isolated-project runner and an evidence gate.

## Managed validation

Use a Git checkout containing both pinned ancestors, .NET 8 and Python 3.12+:

```sh
python3 -S -m unittest discover -s 'Audit~' -p 'test_*.py'
python3 'Audit~/run.py' --output ../gameplaytags-audit --rounds 3
```

The original upstream is `934b14a47ddf0b6367bf6720be58c18cbcb09896`.
The previous frozen implementation is `35c5b35da32fb6503771a7849d7436c1578976d4`.
Their comparisons are separate (`comparison.json` and `previous-comparison.json`).
Original query construction was a thin wrapper, not equivalent validation/freezing.
Only the previous-candidate comparison compares implementations with the frozen-query contract.

Results include raw samples, allocations, regressions, generated-code compilation, the exact complete README
classes, separate assembly builds and a source digest. The native test source is compiled against an
explicitly labelled facade in this job; it is NOT executed as Unity. No NuGet libraries are required.
A green exit means these managed checks completed, not that performance/native acceptance was approved.
Shared-runner CPU alerts remain visible; do not choose winning rounds or automatically waive alerts.

## Native validation: new disposable projects only

An installed, licensed Unity Editor with the required modules and target devices is necessary.
The runner refuses existing project directories and never edits an existing game project.
It creates an embedded package and default settings in a marked disposable project, then invokes the real
Unity Test Framework. Keep any signing settings private; they are not written to the repository or logs.

Run with a shipping patch of 2021.3 and the actual Unity 6 version used by the game:

```sh
python3 'Audit~/unity_acceptance.py' --unity /path/to/2021.3/Unity --project ../accept-2021 --output ../native-2021 --platform EditMode
python3 'Audit~/unity_acceptance.py' --unity /path/to/Unity6/Unity --project ../accept-unity6 --output ../native-unity6 --platform EditMode
python3 'Audit~/unity_acceptance.py' --unity /path/to/Unity6/Unity --project ../accept-play --output ../native-play --platform PlayMode
python3 'Audit~/unity_acceptance.py' --unity /path/to/Unity6/Unity --project ../accept-android --output ../native-android --platform Android
python3 'Audit~/unity_acceptance.py' --unity /path/to/Unity6/Unity --project ../accept-ios --output ../native-ios --platform iOS --settings /private/signing.json
```

On Windows, pass the Unity.exe path in quotes. iOS testing requires a suitable macOS/Xcode/signing/device
environment. `--test-framework` selects a compatible Test Framework package; the default is 1.1.33.
Archive the resolved Unity project manifest/lockfile and editor version with the evidence.

The Editor suite includes serialized container normalization, SerializeReference asset disk reload,
Undo/Redo and two Play Mode sessions with Domain Reload disabled.
The Player suite includes hierarchy/query/alias correctness, JSON/redirect resolution and allocation
measurement with a positive allocation control. Mobile results must identify IL2CPP and 64-bit pointers.
Missing editors, failed commands, empty/skipped/failed XML, missing required cases and wrong mobile
backend/platform cannot count as passing evidence.

These tests do NOT replace manual Inspector/CSV/code-generation interaction acceptance, a game build,
or device CPU/managed/native/peak-memory profiling. Preserve those records in the performance review.
The original and previous candidate must use the same target, inputs and build configuration.
Give special attention to empty/small collections, asymmetry, misses, short-circuit cases, batch growth,
loading/freezing frequency and the actual query/mutation workload.

## Evidence gate

Use the exact source ZIP tested by CI, or an unmodified checkout with the same file bytes.
The digest covers production, samples, tests and package identity; CRLF rewriting changes it.
Changing a tested source file requires new evidence. Audit/documentation files are not runtime identity.

A maintainer, not an automatic benchmark threshold, supplies a device-review JSON. This is an evidence
index and explicit decision record, not a cryptographic attestation or a substitute for reviewing logs.
Start with an unapproved record such as:

```json
{
  "source_digest": "copy from this candidate's summary.json",
  "status": "pending",
  "reviewer": "",
  "target_reports": [],
  "rationale": ""
}
```

Set status to approved only after linking actual target reports, reviewing all CPU/memory and integration
results and recording every accepted regression or its resolution. Do not fill a fictional reviewer.

```sh
python3 'Audit~/release_gate.py' --managed ../gameplaytags-audit/summary.json --native ../native-2021/native.json --native ../native-unity6/native.json --native ../native-play/native.json --native ../native-android/native.json --native ../native-ios/native.json --performance-review ../device-review.json --output ../release-gate.json
```

Exit 2 and `release_approved: false` mean evidence is missing, stale or unapproved. Exit 0 only combines
the supplied records; the script never merges a PR, changes permissions, creates a tag or publishes.
A test fixture exercising the gate's success branch is not real release approval.

## Current boundary

This delivery does not contain a real Unity/Android/iOS passing report. Its native preflight found no
usable Unity Editor in the execution environment. Version 2.0.0-pre.2 and draft PR status intentionally
retain this distinction. The remaining native/device gates must be executed before a stable release.

## 中文摘要

本轮解决了查询冻结的多余分配、加载时预留容量丢失、连续小批次扩容和非法 UTF-16 名称问题；
中英文示例及实际程序集边界加入编译验证。没有添加运行时缓存、对象池或新依赖。
`Freeze()` 的输入验证、源图隔离和工作量限制保留，不能用减少检查来伪造优化。

原生验收脚本只创建新的隔离 Unity 项目；缺少 Editor、设备结果、有效 XML 或性能审核时，
发布检查必须失败。托管编译、原生测试源码的替身编译、真实 Unity 执行是三个不同结论。
本交付仍为预发布候选，不能把 CI 成功或验收脚本齐全描述成已经完成移动端发布验收。
