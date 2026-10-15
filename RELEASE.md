# Selected release implementation / 定版实现

**Implementation: B1.1 + C1 + C2. Package: `1.1.0-rc.1`.**

The implementation is selected and frozen, not another performance experiment.
It is a release candidate, not a claim that Unity/Mono/IL2CPP certification or
all-workload timing acceptance has completed. No stable tag or automatic merge
is performed by the qualification workflow.

**本次定版使用现有 B1.1 + C1 + C2，不增加算法变体。** 包保持候选版本号，
因为 Unity Editor/Player、Mono、IL2CPP 与实际设备验收尚未完成。便携测试和
打包成功不等于实机性能认证，历史生命周期回退仍记为“原因未确认”。

## Frozen identity

| Component | Selection |
|---|---|
| Runtime implementation | `792ce83f1c2a9aeaadc045c192d70081d866d903` |
| Runtime Git tree | `1e42d9659e26e385b267f877a9e3e493ce0eb916` |
| Editor Git tree | `599172bc264d4aaee31b92f651071d571a9b8e4e` |
| Preserved evidence baseline | `04614e0678500af769005102807afcf1b918ff63` |
| Excluded production candidates | P1, C3a, RMinusC1 |

The release gate rejects changed Runtime/Editor trees, uncommitted or unexpected
source files, changed historical evidence, duplicate Unity GUIDs, and missing
script metadata. It writes per-file source/package SHA-256, the checkout commit,
and the selected implementation identity into the package manifest. Runtime and
Editor bytes are not transformed; packaged documentation links can be rewritten
to the source commit so omitted experiment directories remain accessible online.

## Install the candidate

Download the `release-candidate-<commit>` artifact from a successful qualification
job. Extract the artifact ZIP, then use Unity Package Manager **+ > Add package
from tarball** and select `zframework.gameplaytag-1.1.0-rc.1.tgz`. Alternatively,
extract its `package/` directory and use **Add package from disk** on `package.json`.
Use only one installation of this package; do not also copy its scripts into Assets.

Validate the adjacent `.sha256` file before installation. The package manifest
records the exact checkout SHA (a PR build may use a synthetic merge commit).
The existing [English guide](README.md) and [中文指南](README_CN.md) document settings,
tag registration, queries and editor tools. See [API contracts](Documentation~/RELEASE_API.md)
for the retained semantics of the new reusable-output methods.

实验基线、计时脚本及原始样本不会进入精简安装包，但完整保留在 Git 仓库。
包内附带可选 Unity 测试；只有启用包测试时才会编译为测试程序集，不进入普通玩家构建。

## Qualification commands

From a clean checkout, with Python 3, Git and the installed .NET SDK 8.0.425:

```sh
python3 Tools~/Release/test_build.py
bash Tests~/Roadmap/run.sh
bash Tests~/C12Correctness/run.sh \
  "B1=$PWD/Tests~/Roadmap/Baselines/B1/Runtime" \
  "PR4=$PWD/Tests~/C12Correctness/Baselines/PR4/Runtime" \
  "C1=$PWD/Tests~/C12Correctness/Baselines/C1/Runtime" \
  "current=$PWD/Runtime"
# Compile only: this does not execute Unity serialization or integration tests.
(cd Tools~/Release && dotnet build UnityTests.Compile.csproj -c Release --configfile NuGet.Config)
python3 Tools~/Release/build.py --output /a/new/output/directory
```

The existing correctness workflow retains all historical experimental checks.
The release job runs only after portable correctness succeeds and tests repeatable
packaging. The NUnit reference belongs solely to the compile-check project; it
adds no runtime package dependency. No timing job or profiler is started.

## Real Unity gate (not yet executed)

Use a disposable Unity project, not a production scene: tests replace the global
registry while running. Install this candidate and Unity Test Framework. Add
`"testables": ["zframework.gameplaytag"]` at project-manifest root (not inside
`dependencies`). The included `GameplayTags.Tests` assembly has no Editor API
references and can be exercised in Play Mode and a test Player. Enable test
assemblies in Test Runner as required by the chosen platform.

Before building a Player, create a default settings asset using **Tools >
ZFramework > Gameplay Tags > Create Settings**, because the existing build
validator intentionally requires a settings asset. The test fixture creates its
own temporary registry; it does not save changes to that asset. Restore project
state by discarding the disposable project after validation.

Required release evidence: exact Unity version/platform/backend; import and
compilation logs; Unity Test Runner XML with actual executed test counts;
Inspector selection and prefab/asset serialization check; Mono and IL2CPP Player
results for supported targets; target-device public-operation performance with
allocations and the known adverse cases retained. JSON round-trip tests do not
by themselves cover prefab persistence, custom drawers or all Player platforms.

Keep `-rc.1` and draft status until that gate is reviewed. Promoting to `1.1.0`
requires a new explicit release decision, version update and qualification policy;
this script deliberately cannot silently produce a stable release.

## Performance and known limitations

No claim of universal improvement. C1 improves some measured empty/singleton copy
workflows; C2 avoids unnecessary suffix processing and overreservation in a
specific growth geometry. The historical 1024 lifecycle regression remains
unexplained. C3a was not accepted; removing C1 did not establish a consistent
repair and worsened several small-copy controls. Allocation reduction is not
proof of a GC-pause or frame-time improvement. Tighter capacity does not shrink
already-retained storage and can change future growth frequency.

Full frozen reports: [B1.1](https://github.com/acced/ZFramework.GameplayTags/blob/04614e0678500af769005102807afcf1b918ff63/Documentation~/B11_FINAL_RESULTS.md),
[C1/C2](https://github.com/acced/ZFramework.GameplayTags/blob/04614e0678500af769005102807afcf1b918ff63/Tests~/C12Measurements/OUTCOME.md),
[C3a](https://github.com/acced/ZFramework.GameplayTags/blob/04614e0678500af769005102807afcf1b918ff63/Tests~/C3aMeasurements/OUTCOME.md),
[C1 removal](https://github.com/acced/ZFramework.GameplayTags/blob/04614e0678500af769005102807afcf1b918ff63/Tests~/C1RemovalDiagnostic/OUTCOME.md).

## Platform references

Unity 2021.3 documentation: [tarball installation](https://docs.unity3d.com/2021.3/Documentation/Manual/upm-ui-tarball.html),
[package tests and testables](https://docs.unity3d.com/2021.3/Documentation/Manual/cus-tests.html),
[backend differences](https://docs.unity3d.com/2021.3/Documentation/Manual/overview-of-dot-net-in-unity.html).
