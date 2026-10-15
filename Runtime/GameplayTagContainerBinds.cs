using System;

namespace GameplayTags
{
    /// <summary>Owns a group of tag-presence subscriptions. Copies share the same subscriptions.</summary>
    public struct GameplayTagContainerBinds : IDisposable
    {
        private struct BindData
        {
            internal GameplayTag Tag;
            internal OnTagCountChangedDelegate Callback;
        }

        private sealed class BindState
        {
            internal readonly GameplayTagCountContainer Container;
            internal BindData[] Items = Array.Empty<BindData>();
            internal int Count;

            internal BindState(GameplayTagCountContainer container) => Container = container;
        }

        private readonly BindState m_State;

        public GameplayTagContainerBinds(GameplayTagCountContainer container)
        {
            m_State = new BindState(container ?? throw new ArgumentNullException(nameof(container)));
        }

        /// <summary>Subscribes and immediately reports the current presence of the tag.</summary>
        public void Bind(GameplayTag tag, Action<bool> onTagAddedOrRemoved)
        {
            BindState state = m_State ?? throw new InvalidOperationException("Uninitialized binding group.");
            if (onTagAddedOrRemoved == null) throw new ArgumentNullException(nameof(onTagAddedOrRemoved));
            if (!tag.IsValid) throw new ArgumentException("A registered tag is required.", nameof(tag));
            if (state.Count == state.Items.Length)
                Array.Resize(ref state.Items, state.Count == 0 ? 4 : state.Count * 2);

            void OnChanged(GameplayTag _, int count) => onTagAddedOrRemoved(count != 0);

            state.Items[state.Count++] = new BindData { Tag = tag, Callback = OnChanged };
            state.Container.RegisterTagEventCallback(tag, GameplayTagEventType.NewOrRemoved, OnChanged);
            try { onTagAddedOrRemoved(state.Container.GetTagCount(tag) != 0); }
            catch
            {
                // The initial observer may throw or reenter UnbindAll/Bind. Remove
                // only this subscription, leaving any independently created ones.
                state.Container.RemoveTagEventCallback(tag, GameplayTagEventType.NewOrRemoved, OnChanged);
                for (int i = 0; i < state.Count; i++)
                    if (state.Items[i].Callback == (OnTagCountChangedDelegate)OnChanged)
                    {
                        state.Count--;
                        Array.Copy(state.Items, i + 1, state.Items, i, state.Count - i);
                        state.Items[state.Count] = default;
                        break;
                    }
                throw;
            }
        }

        public void UnbindAll()
        {
            BindState state = m_State;
            if (state == null)
                return;

            for (int i = 0; i < state.Count; i++)
            {
                BindData binding = state.Items[i];
                state.Container.RemoveTagEventCallback(binding.Tag, GameplayTagEventType.NewOrRemoved,
                    binding.Callback);
                state.Items[i] = default;
            }

            state.Count = 0;
        }

        public void Dispose() => UnbindAll();
    }
}
