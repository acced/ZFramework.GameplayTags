using GameplayTags;
using UnityEngine;
using Tags = GameplayTags.Generated.GameplayTagRegistry.Tags;

// 标签定义保存在 Editor/GameplayTagSettings.asset，通过标签编辑器生成字段。

namespace GameplayTags.Samples
{
    /// <summary>挂到空物体上，进入 Play Mode，在 Console 查看 API 结果。</summary>
    public sealed class TagPlayground : MonoBehaviour
    {
        private void Start()
        {
            DemoMatching();
            DemoRequirements();
            DemoSourcesAndEvents();
            Debug.Log("[Tag Demo] 演示完成。");
        }

        private void DemoMatching()
        {
            var states = new GameplayTagContainer();
            states.AddTag(Tags.Demo__State__Burning);

            Debug.Log($"[Tag Demo] HasTag(Burning) = {states.HasTag(Tags.Demo__State__Burning)}，预期 True");
            Debug.Log($"[Tag Demo] HasTag(State) = {states.HasTag(Tags.Demo__State)}，预期 True（匹配父标签）");
            Debug.Log($"[Tag Demo] HasTagExact(State) = {states.HasTagExact(Tags.Demo__State)}，预期 False（没有直接添加父标签）");

            states.RemoveTag(Tags.Demo__State__Burning);
            Debug.Log($"[Tag Demo] RemoveTag 后 IsEmpty = {states.IsEmpty}，预期 True");
        }

        private void DemoRequirements()
        {
            var actor = new GameplayTagContainer();
            actor.AddTag(Tags.Demo__Ability__Cast);
            actor.AddTag(Tags.Demo__Equipment__Weapon__Staff);

            // 禁止 Control，必须同时具有 Cast 和 Staff。
            var canCast = GameplayTagRequirements.Create(
                Tags.Demo__State__Control,
                Tags.Demo__Ability__Cast,
                Tags.Demo__Equipment__Weapon__Staff);
            Debug.Log($"[Tag Demo] 有能力和法杖，可以施法 = {canCast.Matches(actor)}，预期 True");

            actor.AddTag(Tags.Demo__State__Control__Frozen);
            Debug.Log($"[Tag Demo] 加入 Frozen 后可以施法 = {canCast.Matches(actor)}，预期 False");
            actor.RemoveTag(Tags.Demo__State__Control__Frozen);
            Debug.Log($"[Tag Demo] 移除 Frozen 后可以施法 = {canCast.Matches(actor)}，预期 True");
        }

        private void DemoSourcesAndEvents()
        {
            var actor = new GameplayTagCountContainer();
            actor.RegisterTagEventCallback(Tags.Demo__State__Burning,
                GameplayTagEventType.AnyCountChange, OnBurningCountChanged);

            // 两个来源分别向同一个角色贡献燃烧标签。
            var fireball = new GameplayTagHierarchicalContainer { Parent = actor };
            var groundFire = new GameplayTagHierarchicalContainer { Parent = actor };
            fireball.AddTag(Tags.Demo__State__Burning);
            groundFire.AddTag(Tags.Demo__State__Burning);
            Debug.Log($"[Tag Demo] 两个来源 Burning 数量 = {actor.GetTagCount(Tags.Demo__State__Burning)}，预期 2");

            fireball.Clear();
            Debug.Log($"[Tag Demo] 移除一个来源 Burning 数量 = {actor.GetTagCount(Tags.Demo__State__Burning)}，预期 1");
            Debug.Log($"[Tag Demo] 仍然燃烧 = {actor.HasTag(Tags.Demo__State__Burning)}，预期 True");

            groundFire.Clear();
            Debug.Log($"[Tag Demo] 移除全部来源后 IsEmpty = {actor.IsEmpty}，预期 True");

            actor.RemoveTagEventCallback(Tags.Demo__State__Burning,
                GameplayTagEventType.AnyCountChange, OnBurningCountChanged);
            fireball.Parent = null;
            groundFire.Parent = null;
        }

        private void OnBurningCountChanged(GameplayTag tag, int count)
        {
            Debug.Log($"[Tag Demo] 事件：{tag.Name} 数量变为 {count}（依次为 1、2、1、0）");
        }
    }
}

