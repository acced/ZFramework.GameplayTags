using UnityEngine;

namespace GameplayTags
{
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
