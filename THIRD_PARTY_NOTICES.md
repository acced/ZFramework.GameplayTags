# Third-party notices

The project-wide MIT license remains in `LICENSE`. The portable Harley-Seal / carry-save population-count implementation in `Runtime/RuntimeTagSet.cs` and the corresponding experiment in `Audit~/DenseKernelProbe.cs` are adapted from Wojciech Mula's sse-popcount implementation:

https://github.com/WojciechMula/sse-popcount/blob/master/popcnt-harley-seal.cpp

The original implementation's BSD-2-Clause notice is reproduced below. Keep this notice when redistributing the adapted implementation in source or binary form. This adaptation uses portable C# integer operations, not the paper's AVX2 implementation, and does not inherit the paper's performance results.

```text
Copyright (c) 2008-2016, Wojciech Muła
Copyright (c) 2016, Kim Walisch
Copyright (c) 2016, Dan Luu
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are
met:

1. Redistributions of source code must retain the above copyright
notice, this list of conditions and the following disclaimer.
2. Redistributions in binary form must reproduce the above copyright
notice, this list of conditions and the following disclaimer in the
 documentation and/or other materials provided with the distribution.
THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS
IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED
TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A
PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED
TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR
PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF
LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING
NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```
