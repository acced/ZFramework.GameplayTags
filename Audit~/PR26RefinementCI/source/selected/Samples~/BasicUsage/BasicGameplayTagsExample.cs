using GameplayTags;
using UnityEngine;

public sealed class BasicGameplayTagsExample : MonoBehaviour
{
    [SerializeField] private GameplayTagContainer initialTags = new GameplayTagContainer();
    [SerializeField] private GameplayTagQuery activationQuery = new GameplayTagQuery();
    private RuntimeTagSet m_Owned;
    private FrozenGameplayTagQuery m_Activation;

    private void Awake()
    {
        TagRegistry registry = GameplayTagManager.CurrentRegistry;
        m_Owned = initialTags.ToRuntime(registry, 32);
        m_Activation = activationQuery.Freeze(registry);
    }
    public bool CanActivate() => m_Activation.Matches(m_Owned);
    public void AddState(RuntimeTag tag) => m_Owned.AddTag(tag);
    public void RemoveState(RuntimeTag tag) => m_Owned.RemoveTag(tag);
}
