using System;
using UnityEngine;

namespace GameplayTags
{
    [Serializable]
    public class GameObjectGameplayTagContainer : MonoBehaviour
    {
        private readonly GameplayTagCountContainer m_GameplayTagContainer = new GameplayTagCountContainer();

        // Runtime state is available before Awake, including on inactive GameObjects.
        public GameplayTagCountContainer GameplayTagContainer => m_GameplayTagContainer;
        public GameplayTagContainer m_PersistentTags = new GameplayTagContainer();

        private void Awake() => m_GameplayTagContainer.AddTags(m_PersistentTags);

        public static implicit operator GameplayTagCountContainer(GameObjectGameplayTagContainer container) =>
            container.GameplayTagContainer;
    }

    public static class GameplayTagContainerBindsHelper
    {
        public static GameplayTagContainerBinds Bind(this GameObject gameObject)
        {
            GameObjectGameplayTagContainer component = gameObject.GetComponent<GameObjectGameplayTagContainer>();
            if (component == null)
                component = gameObject.AddComponent<GameObjectGameplayTagContainer>();

            return new GameplayTagContainerBinds(component.GameplayTagContainer);
        }
    }
}
