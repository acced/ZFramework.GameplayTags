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
        public GameplayTagContainerField m_PersistentTags = new GameplayTagContainerField();

        private void Awake() => m_GameplayTagContainer.AddTags(m_PersistentTags.Value);

        public static implicit operator GameplayTagCountContainer(GameObjectGameplayTagContainer container) =>
            container.GameplayTagContainer;
    }

}
