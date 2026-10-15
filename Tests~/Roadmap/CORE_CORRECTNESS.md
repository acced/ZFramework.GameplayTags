# Final core correctness receipt

B0: 981,930 assertions. Exact B1: 978,409. Final core: 1,002,001.

All three produce shared transcript `6EA12A348CF620CDC4FC89C09BEC0C8933A7B22A8542A2214F0CB4CE011A948F` and compile with .NET Standard 2.1 / C#9. Current-only capacity checks preserve contained append/union and exact B1 intersection reservation. All warning, callback mutation/throw, initialization, aliases, representative string identity, unknown-name and skew semantic tests remain.

This receipt summarizes the successful local run; CI independently reruns the source tests. Portable Unity stubs do not establish actual Unity runtime acceptance.
