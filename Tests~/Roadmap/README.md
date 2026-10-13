# B1 compatibility checks

Run `bash Tests~/Roadmap/run.sh` with .NET 8 SDK on PATH, or set DOTNET to its executable. The offline NuGet configuration uses installed SDK reference packs. Default variants are B0 and current B1. The runner compares deterministic public-behavior transcripts, then compiles both for .NET Standard 2.1 / C# 9.

Coverage includes randomized add/remove/copy/merge, null/self/target aliases, serialized unknown-name policy, ordered output/hash/index, redirects, mutable queries, warning callbacks that observe/mutate/throw, and registry-event initialization ordering. B0's larger assertion count includes adapter reflection checks, not additional semantic cases. The preserved test file contains conditional P1 probes; this B1-only tree skips them because RegistrySnapshot is absent. No P1 source or runtime backend is compiled.

Local B0: 500,279 assertions; B1: 493,287. Shared transcript: C9E889AD8DB04054DBE9E11A0EE8F2652E7F3D3CA7137A9E0C37741FE1EA4F52.

Unity stubs allow portable correctness checks; these do not prove Unity 2021.3 Editor/Player, Mono, IL2CPP or Inspector integration. CI runs correctness and compatibility compilation only, not noisy performance thresholds.
