using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameplayTags
{
    /// <summary>
    /// Unity serialization boundary. Stores explicit names; Value holds resolved runtime state.
    /// Empty and unregistered names are skipped on load. Runtime queries do not resolve names.
    /// </summary>
    [Serializable]
    public sealed class GameplayTagContainerField : ISerializationCallbackReceiver
    {
        // Keep the legacy field name so inline scene/prefab data survives the type change.
        [SerializeField] private List<string> m_SerializedExplicitTags = new List<string>();
        [NonSerialized] private readonly GameplayTagContainer m_Value = new GameplayTagContainer();

        public GameplayTagContainer Value => m_Value;
        public void OnBeforeSerialize() => m_Value.CopyExplicitTagNamesTo(m_SerializedExplicitTags);
        public void OnAfterDeserialize() => m_Value.SetExplicitTagsFromNames(m_SerializedExplicitTags);
        public static implicit operator GameplayTagContainer(GameplayTagContainerField field) => field.Value;
    }
}
