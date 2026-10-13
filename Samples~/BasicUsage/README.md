# Basic Usage sample

`BasicGameplayTagsExample` demonstrates the three things you need in practice: requesting tags,
holding them in a `GameplayTagContainer`, and evaluating a `GameplayTagQuery` against that container.

Standard tags are the single source of truth, so define these in
**Tools > ZFramework > Gameplay Tags > Manager** before entering Play Mode:

- `Sample.State.Alive`
- `Sample.State.Debuff.DamageOverTime.Poisoned`
- `Sample.State.Debuff.Control.Stunned`

`Sample.State.Debuff` does not need its own entry: parent levels are registered implicitly, which is
what makes `owned.HasTag(debuff)` match a container that only holds `...Poisoned`.

Import the sample from Package Manager and put `BasicGameplayTagsExample` on any GameObject.
