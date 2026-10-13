using UnityEngine;

namespace GameplayTags.Samples
{
    /// <summary>
    /// 在 Gameplay Tag Manager 窗口里建好下面这些标签后挂到任意 GameObject 上即可运行。
    /// </summary>
    public sealed class BasicGameplayTagsExample : MonoBehaviour
    {
        private void Awake()
        {
            GameplayTag alive = GameplayTagManager.RequestTag("Sample.State.Alive");
            GameplayTag poisoned = GameplayTagManager.RequestTag("Sample.State.Debuff.DamageOverTime.Poisoned");
            GameplayTag stunned = GameplayTagManager.RequestTag("Sample.State.Debuff.Control.Stunned");

            var owned = new GameplayTagContainer();
            owned.AddTag(alive);
            owned.AddTag(poisoned);

            // 层级匹配：父级标签能命中它的任意后代。
            GameplayTag debuff = GameplayTagManager.RequestTag("Sample.State.Debuff");
            Debug.Log("Has any debuff: " + owned.HasTag(debuff));

            var query = new GameplayTagQuery(
                GameplayTagQueryExpression.AllExpressionsMatch()
                    .AddExpression(GameplayTagQueryExpression.AllTagsMatch().AddTag(alive))
                    .AddExpression(GameplayTagQueryExpression.NoTagsMatch().AddTag(stunned)),
                "Is alive and not stunned");

            Debug.Log(query.UserDescription + ": " + query.Matches(owned));
            owned.AddTag(stunned);
            Debug.Log("After stun: " + query.Matches(owned));
        }
    }
}
