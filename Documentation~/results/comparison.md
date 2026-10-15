| Explicit | Depth | Operation | Unit | Baseline ns | Rewrite ns | Baseline / rewrite | Old B/op | New B/op |
| ---: | ---: | --- | --- | ---: | ---: | ---: | ---: | ---: |
| 4 | 3 | HasTagExact/hit | call | 5.525 | 2.910 | 1.90x | 0 | 0 |
| 4 | 3 | HasTagExact/miss | call | 5.990 | 2.071 | 2.89x | 0 | 0 |
| 4 | 3 | HasTagExact/mixed | call | 5.814 | 2.351 | 2.47x | 0 | 0 |
| 4 | 3 | HasTag/parent-hit | call | 6.446 | 2.047 | 3.15x | 0 | 0 |
| 4 | 3 | HasTag/parent-miss | call | 7.383 | 2.081 | 3.55x | 0 | 0 |
| 4 | 3 | IsParentOf/hit | call | 264.364 | 1.175 | 225.03x | 0 | 0 |
| 4 | 3 | IsChildOf/hit | call | 6.039 | 1.219 | 4.95x | 0 | 0 |
| 4 | 3 | RemoveAdd/existing | pair | 105.866 | 57.262 | 1.85x | 0 | 0 |
| 4 | 3 | AddRemove/absent | pair | 118.047 | 60.543 | 1.95x | 0 | 0 |
| 4 | 3 | Count/AddRemove-existing | pair | 140.981 | 29.931 | 4.71x | 0 | 0 |
| 4 | 3 | Enumerate/explicit | traversal | 19.452 | 5.541 | 3.51x | 0 | 0 |
| 4 | 3 | Construct/fill | container | 304.905 | 410.602 | 0.74x | 328 | 720 |
| 16 | 3 | HasTagExact/hit | call | 7.109 | 3.012 | 2.36x | 0 | 0 |
| 16 | 3 | HasTagExact/miss | call | 7.864 | 2.102 | 3.74x | 0 | 0 |
| 16 | 3 | HasTagExact/mixed | call | 8.827 | 2.552 | 3.46x | 0 | 0 |
| 16 | 3 | HasTag/parent-hit | call | 7.646 | 2.118 | 3.61x | 0 | 0 |
| 16 | 3 | HasTag/parent-miss | call | 9.640 | 1.724 | 5.59x | 0 | 0 |
| 16 | 3 | IsParentOf/hit | call | 1259.277 | 1.175 | 1071.95x | 0 | 0 |
| 16 | 3 | IsChildOf/hit | call | 6.519 | 1.265 | 5.15x | 0 | 0 |
| 16 | 3 | RemoveAdd/existing | pair | 294.029 | 52.790 | 5.57x | 0 | 0 |
| 16 | 3 | AddRemove/absent | pair | 274.872 | 51.975 | 5.29x | 0 | 0 |
| 16 | 3 | Count/AddRemove-existing | pair | 180.570 | 34.727 | 5.20x | 0 | 0 |
| 16 | 3 | Enumerate/explicit | traversal | 87.758 | 18.102 | 4.85x | 0 | 0 |
| 16 | 3 | Construct/fill | container | 992.186 | 1681.293 | 0.59x | 904 | 3208 |
| 128 | 3 | HasTagExact/hit | call | 11.392 | 2.936 | 3.88x | 0 | 0 |
| 128 | 3 | HasTagExact/miss | call | 13.462 | 2.183 | 6.17x | 0 | 0 |
| 128 | 3 | HasTagExact/mixed | call | 14.258 | 2.341 | 6.09x | 0 | 0 |
| 128 | 3 | HasTag/parent-hit | call | 13.413 | 1.986 | 6.75x | 0 | 0 |
| 128 | 3 | HasTag/parent-miss | call | 11.757 | 1.893 | 6.21x | 0 | 0 |
| 128 | 3 | IsParentOf/hit | call | 1330.759 | 1.186 | 1122.25x | 0 | 0 |
| 128 | 3 | IsChildOf/hit | call | 7.393 | 1.362 | 5.43x | 0 | 0 |
| 128 | 3 | RemoveAdd/existing | pair | 1465.861 | 44.405 | 33.01x | 0 | 0 |
| 128 | 3 | AddRemove/absent | pair | 1431.407 | 57.308 | 24.98x | 0 | 0 |
| 128 | 3 | Count/AddRemove-existing | pair | 156.570 | 31.420 | 4.98x | 0 | 0 |
| 128 | 3 | Enumerate/explicit | traversal | 653.052 | 146.150 | 4.47x | 0 | 0 |
| 128 | 3 | Construct/fill | container | 11155.703 | 8945.797 | 1.25x | 3456 | 13488 |
| 1024 | 3 | HasTagExact/hit | call | 23.119 | 3.006 | 7.69x | 0 | 0 |
| 1024 | 3 | HasTagExact/miss | call | 21.070 | 2.086 | 10.10x | 0 | 0 |
| 1024 | 3 | HasTagExact/mixed | call | 22.109 | 2.650 | 8.34x | 0 | 0 |
| 1024 | 3 | HasTag/parent-hit | call | 22.792 | 2.013 | 11.32x | 0 | 0 |
| 1024 | 3 | HasTag/parent-miss | call | 19.957 | 1.718 | 11.61x | 0 | 0 |
| 1024 | 3 | IsParentOf/hit | call | 1271.244 | 1.175 | 1082.09x | 0 | 0 |
| 1024 | 3 | IsChildOf/hit | call | 8.545 | 1.219 | 7.01x | 0 | 0 |
| 1024 | 3 | RemoveAdd/existing | pair | 9956.449 | 47.090 | 211.44x | 0 | 0 |
| 1024 | 3 | AddRemove/absent | pair | 10210.422 | 58.309 | 175.11x | 0 | 0 |
| 1024 | 3 | Count/AddRemove-existing | pair | 163.474 | 35.094 | 4.66x | 0 | 0 |
| 1024 | 3 | Enumerate/explicit | traversal | 5789.786 | 1291.994 | 4.48x | 0 | 0 |
| 1024 | 3 | Construct/fill | container | 154364.555 | 45068.164 | 3.43x | 25104 | 106888 |
| 1 | 32 | Depth/HasTagExact | call | 3.660 | 2.580 | 1.42x | 0 | 0 |
| 1 | 32 | Depth/HasAllExact-singleton | call | 6.313 | 4.511 | 1.40x | 0 | 0 |
| 1 | 32 | Depth/Enumerate-explicit | traversal | 5.175 | 1.879 | 2.75x | 0 | 0 |
| 1 | 32 | Depth/RemoveAdd | pair | 709.381 | 465.321 | 1.52x | 0 | 0 |
| 1 | 128 | Depth/HasTagExact | call | 3.970 | 2.581 | 1.54x | 0 | 0 |
| 1 | 128 | Depth/HasAllExact-singleton | call | 6.026 | 4.457 | 1.35x | 0 | 0 |
| 1 | 128 | Depth/Enumerate-explicit | traversal | 4.619 | 1.893 | 2.44x | 0 | 0 |
| 1 | 128 | Depth/RemoveAdd | pair | 3281.730 | 2116.341 | 1.55x | 0 | 0 |

Retained live memory (median of three GC snapshots):

| Explicit | Depth | Baseline B/container | Rewrite B/container | Rewrite / baseline |
| ---: | ---: | ---: | ---: | ---: |
| 4 | 3 | 227.3 | 556.0 | 2.45x |
| 16 | 3 | 463.9 | 1752.0 | 3.78x |
| 128 | 3 | 1688.0 | 6808.0 | 4.03x |
| 1024 | 3 | 12440.0 | 53400.0 | 4.29x |
| 1 | 32 | 290.0 | 936.1 | 3.23x |
| 1 | 128 | 668.2 | 3256.0 | 4.87x |
