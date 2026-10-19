### Recipe 1: decide whether a character can cast

A skill requires a staff and casting ability, and forbids crowd control. Build its rule when the skill loads, then reuse it:

```csharp
var castingRule = GameplayTagRequirements.Create(
    GameTags.Control, GameTags.Cast, GameTags.Staff);

var actor = new GameplayTagContainer();
actor.AddTag(GameTags.Cast);
actor.AddTag(GameTags.Staff);
UnityEngine.Debug.Log(castingRule.Matches(actor)); // True.
actor.AddTag(GameTags.Silenced);
UnityEngine.Debug.Log(castingRule.Matches(actor)); // False: silence belongs to Control.
```

`Create(forbidden, required, additionalRequired)` needs no temporary condition containers. For larger rules, use `new GameplayTagRequirements(forbiddenTags, requiredTags)` with arrays or spans of resolved tags. Construction removes duplicate/implied conditions and identifies contradictions; up to four effective conditions stay inline.

A rule is an **immutable runtime snapshot**: it does not retain your input arrays. Rebuild it when configuration changes; each match still reads the actor's current state. The old constructor taking two live containers is no longer available. Rules hold runtime IDs, so save names/configuration and reconstruct rules after loading rather than serializing the rule itself.

Simple checks remain available as `HasAny(query)` and `HasAll(query)`, or `HasAnyExact`/`HasAllExact` for exact conditions. An empty condition set satisfies `HasAll`, but not `HasAny`.

Equipment and temporary effects can jointly satisfy a rule without allocating a union:

```csharp
var equipment = new GameplayTagContainer();
equipment.AddTag(GameTags.Staff);
var effects = new GameplayTagContainer();
effects.AddTag(GameTags.Cast);
bool canCast = castingRule.Matches(equipment, effects); // True.
```

### Evaluate shared skill conditions together

Share the rule group across actors, and keep a separate cache for each actor:

```csharp
var rules = new GameplayTagRuleSet(new[]
{
    castingRule,
    new GameplayTagRequirements(System.Array.Empty<GameplayTag>(), new[] { GameTags.Burning })
});
var actorCache = rules.CreateCache();
System.ReadOnlySpan<bool> available = actorCache.Evaluate(actor);
bool canUseFirstSkill = available[0];
```

The group queries each distinct condition once. An unchanged presence version lets the cache reuse its results; after a change it refreshes distinct conditions and recomputes affected rules. A stack-count change that leaves presence unchanged need not invalidate presence-only rules. The container version is necessary cache state, not an input-validation layer.

Without a cache, use `rules.Evaluate(actor, resultSpan)` with caller-owned space of at least `rules.Count` elements. The cached result span is a borrowed view overwritten by the next evaluation; do not share a cache concurrently. Two holders also work: `actorCache.Evaluate(equipment, effects)`. Create groups and caches outside per-frame loops.

