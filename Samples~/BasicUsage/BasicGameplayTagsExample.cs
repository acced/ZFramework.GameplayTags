using UnityEngine;

namespace GameplayTags.Samples
{
    /// <summary>Create the Sample tags in the manager, then attach this component to a GameObject.</summary>
    public sealed class BasicGameplayTagsExample : MonoBehaviour
    {
        private void Awake()
        {
            GameplayTag alive = GameplayTagManager.RequestTag("Sample.State.Alive");
            GameplayTag poisoned = GameplayTagManager.RequestTag("Sample.State.Debuff.DamageOverTime.Poisoned");
            GameplayTag stunned = GameplayTagManager.RequestTag("Sample.State.Debuff.Control.Stunned");
            var owned = new GameplayTagContainer(8);
            owned.AddTag(alive);
            owned.AddTag(poisoned);
            GameplayTag debuff = GameplayTagManager.RequestTag("Sample.State.Debuff");
            Debug.Log("Has any debuff: " + owned.HasTag(debuff));

            // Loading/configuration boundary: validate and freeze once, not every frame or per unit.
            var source = new GameplayTagQuery(
                GameplayTagQueryExpression.AllExpressionsMatch()
                    .AddExpression(GameplayTagQueryExpression.AllTagsMatch().AddTag(alive))
                    .AddExpression(GameplayTagQueryExpression.NoTagsMatch().AddTag(stunned)),
                "Is alive and not stunned");
            FrozenGameplayTagQuery matcher = source.Freeze();

            // The matcher can be shared by all units using this rule. Each unit owns its own container.
            Debug.Log(matcher.UserDescription + ": " + matcher.Matches(owned));
            owned.AddTag(stunned);
            Debug.Log("After stun: " + matcher.Matches(owned));
        }
    }
}
