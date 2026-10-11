using System;

namespace GameplayTags
{
    [Serializable]
    public struct GameplayTagRequirements
    {
        public GameplayTagContainer m_ForbiddenTags;
        public GameplayTagContainer m_RequiredTags;
        public readonly GameplayTagContainer ForbiddenTags => m_ForbiddenTags;
        public readonly GameplayTagContainer RequiredTags => m_RequiredTags;
        public readonly bool IsEmpty =>
            (m_ForbiddenTags == null || m_ForbiddenTags.IsEmpty) &&
            (m_RequiredTags == null || m_RequiredTags.IsEmpty);

        public GameplayTagRequirements(GameplayTagContainer forbiddenTags, GameplayTagContainer requiredTags)
        {
            m_ForbiddenTags = forbiddenTags;
            m_RequiredTags = requiredTags;
        }

        public readonly bool Matches<T>(in T container) where T : IGameplayTagContainer =>
            !container.HasAny(m_ForbiddenTags) && container.HasAll(m_RequiredTags);

        /// <summary>Required tags may be split across both holders; forbidden tags may appear in neither.</summary>
        public readonly bool Matches<T, U>(in T staticContainer, in U dynamicContainer)
            where T : IGameplayTagContainer where U : IGameplayTagContainer =>
            !staticContainer.HasAny(m_ForbiddenTags) &&
            !dynamicContainer.HasAny(m_ForbiddenTags) &&
            GameplayTagContainerUtility.HasAll(staticContainer, dynamicContainer, m_RequiredTags);
    }
}
