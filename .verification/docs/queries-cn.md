### 用法一：判断角色能不能施法

技能要求“有法杖、能施法”，同时禁止控制状态。规则在技能加载时创建一次，之后反复使用：

```csharp
var castingRule = GameplayTagRequirements.Create(
    GameTags.Control, GameTags.Cast, GameTags.Staff);

var actor = new GameplayTagContainer();
actor.AddTag(GameTags.Cast);
actor.AddTag(GameTags.Staff);
UnityEngine.Debug.Log(castingRule.Matches(actor)); // True。
actor.AddTag(GameTags.Silenced);
UnityEngine.Debug.Log(castingRule.Matches(actor)); // False：沉默属于 Control。
```

`Create(禁止标签, 必需标签, 另一必需标签)` 不需要临时条件容器。条件较多时，使用 `new GameplayTagRequirements(forbiddenTags, requiredTags)`，两个参数是已解析标签的数组或 span。构造时去除重复和被包含的条件，并识别矛盾；最多四个有效条件内联保存。

规则是**不可变的运行时快照**：输入数组不会被保留，配置改变时重新创建规则；匹配时读取的是角色此刻的状态。旧版接受两个容器、跟随容器修改的构造器已移除。规则保存运行时 ID，不直接序列化；存档保存名称，加载后重新创建规则。

简单判断仍可以用 `HasAny(query)`、`HasAll(query)`；精确条件用 `HasAnyExact`、`HasAllExact`。空条件的 `HasAll` 为 true，`HasAny` 为 false。

装备和临时效果分开放，也不需要先分配并集：

```csharp
var equipment = new GameplayTagContainer();
equipment.AddTag(GameTags.Staff);
var effects = new GameplayTagContainer();
effects.AddTag(GameTags.Cast);
bool canCast = castingRule.Matches(equipment, effects); // True。
```

### 多个技能共享条件时，集中判断

把同一组技能规则共享给角色，每个角色持有自己的缓存：

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

规则组每次只查询一次相同条件。缓存发现角色存在状态未变时直接复用结果；状态改变时刷新不同条件，只重算受影响的规则。层数改变但“有／没有”没变，不必重算存在性规则。容器的存在版本是缓存正确性所需的状态，不是参数防御。

不需要缓存时，用 `rules.Evaluate(actor, resultSpan)` 写入调用方提供的结果空间，长度至少为 `rules.Count`。缓存返回的 span 是借用视图，下次评估会覆盖；不要跨线程共享一个缓存。它也支持 `actorCache.Evaluate(equipment, effects)`。不要每帧重新创建规则组或缓存。

