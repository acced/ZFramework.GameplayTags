using System.Diagnostics;

namespace GameplayTags
{
    internal sealed class GameplayTagContainerDebugView
    {
        [DebuggerDisplay("{Display,nq}")]
        internal readonly struct Tag
        {
            private readonly IGameplayTagContainer m_Container;
            private readonly GameplayTag m_Tag;

            private string Display
            {
                get
                {
                    if (m_Container is IGameplayTagCountContainer counts)
                        return $"{m_Tag.Name} (Explicit: {counts.GetExplicitTagCount(m_Tag)}, Total: {counts.GetTagCount(m_Tag)})";
                    return m_Container.HasTagExact(m_Tag) ? $"{m_Tag.Name} (Explicit)" : m_Tag.Name;
                }
            }

            internal Tag(IGameplayTagContainer container, GameplayTag tag)
            {
                m_Container = container;
                m_Tag = tag;
            }
        }

        private readonly IGameplayTagContainer m_Container;

        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public Tag[] Tags
        {
            get
            {
                var result = new Tag[m_Container.TagCount];
                int i = 0;
                foreach (GameplayTag tag in m_Container.GetTags())
                    result[i++] = new Tag(m_Container, tag);
                return result;
            }
        }

        public GameplayTagContainerDebugView(IGameplayTagContainer container) => m_Container = container;
    }
}
