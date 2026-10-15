# Bulk-operation paired measurements

Standalone .NET 8, same workload, one public API call per operation; remove_add_after_copy reports one RemoveTag+AddTag pair. This does not reproduce Unity Editor or an unavailable Alex implementation.

Median of independent process samples. Raw samples, fixture checksums and source hashes are alongside this table. Fresh-result allocation is included; reused outputs are preallocated and warmed.

| Variant | Case | Before µs | After µs | Before / after | Before B/op | After B/op |
|---|---|---:|---:|---:|---:|---:|
| after | `copy_alternating_disjoint/n1024` | 3.415 | 0.956 | 3.57× | 0 | 0 |
| after | `copy_both_empty/n0` | 0.022 | 0.008 | 2.66× | 0 | 0 |
| after | `copy_count_normalize/depth128/n1` | 1.068 | 0.538 | 1.99× | 0 | 0 |
| after | `copy_count_normalize/n1024` | 31.278 | 23.329 | 1.34× | 0 | 0 |
| after | `copy_reuse/depth128/n2` | 0.426 | 0.099 | 4.30× | 0 | 0 |
| after | `copy_reuse/n1024` | 3.437 | 0.849 | 4.05× | 0 | 0 |
| after | `copy_reuse/n128` | 0.531 | 0.116 | 4.58× | 0 | 0 |
| after | `copy_reuse/n16` | 0.127 | 0.039 | 3.25× | 0 | 0 |
| after | `copy_reuse_8x_capacity/n1024` | 3.553 | 0.951 | 3.74× | 0 | 0 |
| after | `copy_self/n1024` | 0.005 | 0.005 | 0.95× | 0 | 0 |
| after | `find_exact_after_copy/1x_capacity/n1024` | 0.005 | 0.005 | 1.02× | 0 | 0 |
| after | `find_exact_after_copy/8x_capacity/n1024` | 0.005 | 0.006 | 0.97× | 0 | 0 |
| after | `intersection_count_left/n1024/overlap50` | 27.783 | 18.336 | 1.52× | 53568 | 26784 |
| after | `intersection_new/deep_miss_left/n2` | 0.077 | 0.084 | 0.92× | 288 | 296 |
| after | `intersection_new/deep_miss_right/n2` | 0.071 | 0.080 | 0.88× | 288 | 296 |
| after | `intersection_new/depth128/n2/overlap50` | 2.361 | 0.756 | 3.12× | 6240 | 3264 |
| after | `intersection_new/n1024/overlap0` | 2.223 | 3.171 | 0.70× | 80 | 88 |
| after | `intersection_new/n1024/overlap100` | 50.565 | 7.168 | 7.05× | 106888 | 53408 |
| after | `intersection_new/n1024/overlap50` | 28.105 | 18.536 | 1.52× | 53568 | 26784 |
| after | `intersection_new/n128/overlap50` | 3.220 | 2.557 | 1.26× | 6760 | 3488 |
| after | `intersection_new/n16/overlap50` | 0.475 | 0.378 | 1.26× | 1536 | 960 |
| after | `intersection_new_both_empty/n0` | 0.028 | 0.035 | 0.80× | 80 | 88 |
| after | `intersection_new_empty_right/n1024` | 0.027 | 0.032 | 0.84× | 80 | 88 |
| after | `intersection_new_same_instance/n1024` | 49.634 | 3.223 | 15.40× | 106888 | 53408 |
| after | `intersection_reuse/depth128/n2/overlap50` | 1.079 | 0.538 | 2.00× | 0 | 0 |
| after | `intersection_reuse/n1024/overlap0` | 2.291 | 2.914 | 0.79× | 0 | 0 |
| after | `intersection_reuse/n1024/overlap100` | 32.382 | 4.825 | 6.71× | 0 | 0 |
| after | `intersection_reuse/n1024/overlap50` | 17.468 | 17.273 | 1.01× | 0 | 0 |
| after | `intersection_reuse/n128/overlap50` | 2.106 | 2.158 | 0.98× | 0 | 0 |
| after | `intersection_reuse/n16/overlap50` | 0.313 | 0.286 | 1.09× | 0 | 0 |
| after | `remove_add_after_copy/1x_capacity/n1024` | 0.054 | 0.050 | 1.07× | 0 | 0 |
| after | `remove_add_after_copy/8x_capacity/n1024` | 0.050 | 0.052 | 0.97× | 0 | 0 |
| after | `union_count_left/n1024/overlap50` | 79.975 | 14.341 | 5.58× | 115104 | 61624 |
| after | `union_new/depth128/n2/overlap50` | 1.180 | 1.269 | 0.93× | 7320 | 6336 |
| after | `union_new/n1024/overlap0` | 52.543 | 31.314 | 1.68× | 155848 | 159976 |
| after | `union_new/n1024/overlap100` | 14.187 | 6.791 | 2.09× | 57496 | 53408 |
| after | `union_new/n1024/overlap50` | 26.516 | 16.123 | 1.64× | 57496 | 61624 |
| after | `union_new/n128/overlap50` | 4.071 | 2.690 | 1.51× | 7320 | 7864 |
| after | `union_new/n16/overlap50` | 0.537 | 0.473 | 1.14× | 1944 | 1912 |
| after | `union_new_both_empty/n0` | 0.049 | 0.034 | 1.44× | 80 | 88 |
| after | `union_new_empty_left/n1024` | 49.822 | 7.854 | 6.34× | 106888 | 53408 |
| after | `union_new_empty_right/n1024` | 4.866 | 6.094 | 0.80× | 57496 | 53408 |
| after | `union_new_same_instance/n1024` | 13.842 | 3.331 | 4.15× | 57496 | 53408 |
