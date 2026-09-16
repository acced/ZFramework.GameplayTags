using System;
using System.Diagnostics;
using UnityEngine;

namespace GameplayTags
{
    /// <summary>A case-sensitive dot-separated name snapshot. Resolve at a loading boundary.</summary>
    [Serializable, DebuggerDisplay("{Name,nq}")]
    public struct GameplayTag : IEquatable<GameplayTag>, IComparable<GameplayTag>
    {
        [SerializeField] private string m_Name;
        public static GameplayTag None => default;
        public string Name => m_Name ?? string.Empty;
        public string LeafName => GameplayTagName.GetLeaf(m_Name);
        public bool IsValid => !string.IsNullOrEmpty(m_Name);

        internal GameplayTag(string name) { m_Name = string.IsNullOrEmpty(name) ? null : name; }
        public bool MatchesTag(GameplayTag parent) => GameplayTagName.IsEqualOrChildOf(m_Name, parent.m_Name);
        public bool MatchesTagExact(GameplayTag other) => Equals(other);
        public int MatchesTagDepth(GameplayTag other) => GameplayTagName.GetMatchDepth(m_Name, other.m_Name);
        /// <summary>Convenience API: non-root parents require a substring allocation.</summary>
        public GameplayTag GetDirectParent() => new GameplayTag(GameplayTagName.GetParent(m_Name));
        public bool Equals(GameplayTag other) => string.CompareOrdinal(Name, other.Name) == 0;
        public override bool Equals(object obj) => obj is GameplayTag other && Equals(other);
        public override int GetHashCode() => Name.GetHashCode();
        public int CompareTo(GameplayTag other) => string.CompareOrdinal(Name, other.Name);
        public override string ToString() => Name;
        public static bool TryParse(string name, out GameplayTag tag) => GameplayTagManager.TryRequestTag(name, out tag);
        public static GameplayTag Parse(string name) => GameplayTagManager.RequestTag(name);
        public static bool operator ==(GameplayTag lhs, GameplayTag rhs) => lhs.Equals(rhs);
        public static bool operator !=(GameplayTag lhs, GameplayTag rhs) => !lhs.Equals(rhs);
    }

    public static class GameplayTagName
    {
        private const string InvalidCharacters = ",;[](){}\"'\\/:";

        public static bool TryNormalize(string value, out string normalized, out string error)
        {
            normalized = value == null ? string.Empty : value.Trim();
            if (normalized.Length == 0) { error = "Gameplay Tag cannot be empty."; return false; }
            if (normalized[0] == '.' || normalized[normalized.Length - 1] == '.')
            { error = "Gameplay Tag cannot start or end with '.'."; return false; }
            for (int i = 0; i < normalized.Length; i++)
            {
                char c = normalized[i];
                if (c == '.')
                {
                    if (normalized[i - 1] == '.') { error = "Gameplay Tag cannot contain an empty hierarchy segment."; return false; }
                }
                else if (char.IsSurrogate(c))
                {
                    if (!char.IsHighSurrogate(c) || i + 1 == normalized.Length || !char.IsLowSurrogate(normalized[i + 1]))
                    { error = "Gameplay Tag contains an unpaired UTF-16 surrogate."; return false; }
                    i++;
                }
                else if (char.IsWhiteSpace(c) || char.IsControl(c) || InvalidCharacters.IndexOf(c) >= 0)
                { error = "Gameplay Tag contains whitespace, a control character, or reserved punctuation."; return false; }
            }
            error = null;
            return true;
        }

        public static string GetParent(string value)
        {
            int separator = string.IsNullOrEmpty(value) ? -1 : value.LastIndexOf('.');
            return separator <= 0 ? string.Empty : value.Substring(0, separator);
        }
        public static string GetLeaf(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            int separator = value.LastIndexOf('.');
            return separator < 0 ? value : value.Substring(separator + 1);
        }
        public static int GetDepth(string value)
        {
            if (string.IsNullOrEmpty(value)) return 0;
            int depth = 1;
            for (int i = 0; i < value.Length; i++) if (value[i] == '.') depth++;
            return depth;
        }
        public static bool IsEqualOrChildOf(string value, string parent)
        {
            if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(parent) || value.Length < parent.Length) return false;
            return (value.Length == parent.Length || value[parent.Length] == '.') && HasPrefix(value, parent);
        }
        internal static bool HasPrefix(string value, string prefix)
        {
            if (value.Length < prefix.Length) return false;
            for (int i = 0; i < prefix.Length; i++) if (value[i] != prefix[i]) return false;
            return true;
        }
        public static int GetMatchDepth(string lhs, string rhs)
        {
            if (string.IsNullOrEmpty(lhs) || string.IsNullOrEmpty(rhs)) return 0;
            int limit = Math.Min(lhs.Length, rhs.Length), cursor = 0, segmentStart = 0, depth = 0;
            while (cursor < limit && lhs[cursor] == rhs[cursor])
            {
                if (lhs[cursor] == '.') { depth++; segmentStart = cursor + 1; }
                cursor++;
            }
            bool lhsClosed = cursor == lhs.Length || lhs[cursor] == '.';
            bool rhsClosed = cursor == rhs.Length || rhs[cursor] == '.';
            if (lhsClosed && rhsClosed && cursor > segmentStart) depth++;
            return depth;
        }
    }
}
