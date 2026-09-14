using System;
using System.Diagnostics;
using UnityEngine;

namespace GameplayTags
{
    /// <summary>
    /// 点分层级标签，例如 <c>"State.Debuff.Burning"</c>。
    ///
    /// 只序列化名称本身：层级关系完全由名称的点分结构给出，匹配时不查注册表、不分配内存。
    /// 注册校验发生在 <see cref="GameplayTagManager.RequestTag(string,bool)"/> 和
    /// <see cref="GameplayTagContainer.AddTag(GameplayTag)"/> 这些入口处。
    /// </summary>
    [Serializable]
    [DebuggerDisplay("{Name,nq}")]
    public struct GameplayTag : IEquatable<GameplayTag>, IComparable<GameplayTag>
    {
        [SerializeField] private string m_Name;

        public static GameplayTag None { get { return default(GameplayTag); } }

        public string Name { get { return m_Name ?? string.Empty; } }
        public string LeafName { get { return GameplayTagName.GetLeaf(m_Name); } }
        public bool IsValid { get { return !string.IsNullOrEmpty(m_Name); } }

        internal GameplayTag(string name)
        {
            m_Name = string.IsNullOrEmpty(name) ? null : name;
        }

        /// <summary>本标签是否等于 <paramref name="tagToCheck"/> 或落在它下面。</summary>
        public bool MatchesTag(GameplayTag tagToCheck) => GameplayTagName.IsEqualOrChildOf(m_Name, tagToCheck.m_Name);

        public bool MatchesTagExact(GameplayTag tagToCheck) => Equals(tagToCheck);

        /// <summary>两个标签从根开始有多少层级片段完全一致。</summary>
        public int MatchesTagDepth(GameplayTag other) => GameplayTagName.GetMatchDepth(m_Name, other.m_Name);

        public GameplayTag GetDirectParent() => new GameplayTag(GameplayTagName.GetParent(m_Name));

        // CompareOrdinal 在 Mono 上是 icall，比 string.Equals(..., StringComparison.Ordinal) 快近十倍。
        public bool Equals(GameplayTag other) => string.CompareOrdinal(m_Name, other.m_Name) == 0;
        public override bool Equals(object obj) => obj is GameplayTag && Equals((GameplayTag)obj);
        public override int GetHashCode() => m_Name == null ? 0 : m_Name.GetHashCode();
        public int CompareTo(GameplayTag other) => string.CompareOrdinal(m_Name, other.m_Name);
        public override string ToString() => Name;

        public static bool TryParse(string name, out GameplayTag tag) => GameplayTagManager.TryRequestTag(name, out tag);
        public static GameplayTag Parse(string name) => GameplayTagManager.RequestTag(name, true);

        public static bool operator ==(GameplayTag lhs, GameplayTag rhs) => lhs.Equals(rhs);
        public static bool operator !=(GameplayTag lhs, GameplayTag rhs) => !lhs.Equals(rhs);
    }

    /// <summary>点分标签名的校验与解析工具。</summary>
    public static class GameplayTagName
    {
        private const string InvalidCharacters = ",;[](){}\"'\\/:";

        public static bool TryNormalize(string value, out string normalized, out string error)
        {
            normalized = value == null ? string.Empty : value.Trim();
            if (normalized.Length == 0)
            {
                error = "Gameplay Tag cannot be empty.";
                return false;
            }

            if (normalized[0] == '.' || normalized[normalized.Length - 1] == '.')
            {
                error = "Gameplay Tag cannot start or end with '.'.";
                return false;
            }

            for (int i = 0; i < normalized.Length; i++)
            {
                char character = normalized[i];
                if (character == '.')
                {
                    if (normalized[i - 1] == '.')
                    {
                        error = "Gameplay Tag cannot contain an empty hierarchy segment.";
                        return false;
                    }
                    continue;
                }

                if (char.IsWhiteSpace(character))
                {
                    error = "Gameplay Tag cannot contain whitespace.";
                    return false;
                }

                if (InvalidCharacters.IndexOf(character) >= 0)
                {
                    error = "Gameplay Tag contains the invalid character '" + character + "'.";
                    return false;
                }
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
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            int separator = value.LastIndexOf('.');
            return separator < 0 ? value : value.Substring(separator + 1);
        }

        public static int GetDepth(string value)
        {
            if (string.IsNullOrEmpty(value))
                return 0;

            int depth = 1;
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] == '.')
                    depth++;
            }
            return depth;
        }

        /// <summary>
        /// <paramref name="value"/> 是否等于 <paramref name="parent"/> 或落在它下面。
        /// 边界必须压在 <c>'.'</c> 上，所以 <c>"Ability.AttackSpeed"</c> 不属于 <c>"Ability.Attack"</c>。
        /// </summary>
        public static bool IsEqualOrChildOf(string value, string parent)
        {
            if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(parent) || value.Length < parent.Length)
                return false;
            if (!HasPrefix(value, parent))
                return false;

            return value.Length == parent.Length || value[parent.Length] == '.';
        }

        /// <summary>
        /// 序数前缀判定。Mono 上 <c>string.StartsWith(prefix, StringComparison.Ordinal)</c> 要 ~200ns，
        /// 手写字符循环只要 ~70ns，而这是标签匹配最热的一条路径。
        /// </summary>
        internal static bool HasPrefix(string value, string prefix)
        {
            if (value.Length < prefix.Length)
                return false;

            for (int i = 0; i < prefix.Length; i++)
            {
                if (value[i] != prefix[i])
                    return false;
            }
            return true;
        }

        /// <summary>两个标签名从根开始有多少层级片段完全一致。</summary>
        public static int GetMatchDepth(string lhs, string rhs)
        {
            if (string.IsNullOrEmpty(lhs) || string.IsNullOrEmpty(rhs))
                return 0;

            int limit = Math.Min(lhs.Length, rhs.Length);
            int cursor = 0;
            int segmentStart = 0;
            int depth = 0;
            while (cursor < limit && lhs[cursor] == rhs[cursor])
            {
                if (lhs[cursor] == '.')
                {
                    depth++;
                    segmentStart = cursor + 1;
                }
                cursor++;
            }

            // 尾段只有在两边都恰好收束于此（字符串结束或遇到分隔符）时才算一层。
            bool lhsClosed = cursor == lhs.Length || lhs[cursor] == '.';
            bool rhsClosed = cursor == rhs.Length || rhs[cursor] == '.';
            if (lhsClosed && rhsClosed && cursor > segmentStart)
                depth++;

            return depth;
        }
    }
}
