# Preserved baselines

B0 Runtime is the unchanged source from acced/ZFramework.GameplayTags commit db37fed890647338a3664918071eb428f3d4514a. Current main 4f03bb16e8e5f9403b7fd67440da078fc3532a8d differs only by the then-unused GameplayTagContainerAlgorithms helper. The B0 adapter emulates new target APIs using old public operations.

B1 Runtime is the exact source at commit `dac77e7`, preserved before B1.1 optimization. B1.1 changes are compiled only from the current root `Runtime`; neither preserved baseline is edited for optimization or to make tests pass.

B11First Runtime is the complete frozen first-screen B1.1 source, retained for the single factory-narrowing repair experiment. Every file matches the Runtime entries in results/full-final/source.before.sha256. This is the source of the preserved adverse first-screen results, not current production Runtime; it contains no P1 representation.
