using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace GameplayTags
{
    public enum GameplayTagQueryExpressionType
    {
        Undefined = 0,
        AnyTagsMatch = 1,
        AllTagsMatch = 2,
        NoTagsMatch = 3,
        AnyExpressionsMatch = 4,
        AllExpressionsMatch = 5,
        NoExpressionsMatch = 6
    }

    /// <summary>
    /// 对容器求值的标签条件。直接遍历表达式树，没有编译产物，所以改完立刻生效。
    /// </summary>
    [Serializable]
    public sealed class GameplayTagQuery
    {
        [SerializeReference] private GameplayTagQueryExpression m_RootExpression;
        [SerializeField] private string m_UserDescription;

        public GameplayTagQueryExpression RootExpression => m_RootExpression;
        public string UserDescription => m_UserDescription ?? string.Empty;
        public bool IsEmpty => m_RootExpression == null;

        public GameplayTagQuery()
        {
        }

        public GameplayTagQuery(GameplayTagQueryExpression rootExpression)
            : this(rootExpression, string.Empty)
        {
        }

        public GameplayTagQuery(GameplayTagQueryExpression rootExpression, string userDescription)
        {
            m_RootExpression = rootExpression;
            m_UserDescription = userDescription;
        }

        public void SetRoot(GameplayTagQueryExpression rootExpression, string userDescription)
        {
            m_RootExpression = rootExpression;
            m_UserDescription = userDescription ?? string.Empty;
        }

        public void Clear()
        {
            m_RootExpression = null;
            m_UserDescription = string.Empty;
        }

        public bool Matches(GameplayTagContainer tags) =>
            m_RootExpression != null && tags != null && m_RootExpression.Matches(tags, 0);

        public static GameplayTagQuery MakeQuery(GameplayTagQueryExpression expression) =>
            new GameplayTagQuery(expression);

        public static GameplayTagQuery MakeQueryMatchAnyTags(GameplayTagContainer tags) =>
            new GameplayTagQuery(GameplayTagQueryExpression.AnyTagsMatch().AddTags(tags));

        public static GameplayTagQuery MakeQueryMatchAllTags(GameplayTagContainer tags) =>
            new GameplayTagQuery(GameplayTagQueryExpression.AllTagsMatch().AddTags(tags));

        public static GameplayTagQuery MakeQueryMatchNoTags(GameplayTagContainer tags) =>
            new GameplayTagQuery(GameplayTagQueryExpression.NoTagsMatch().AddTags(tags));

        public override string ToString() =>
            string.IsNullOrEmpty(m_UserDescription) ? m_RootExpression?.ToString() ?? "<empty>" : m_UserDescription;
    }

    [Serializable]
    public sealed class GameplayTagQueryExpression
    {
        private const int MaxDepth = 64;

        [SerializeField] private GameplayTagQueryExpressionType m_Type = GameplayTagQueryExpressionType.AllTagsMatch;
        [SerializeField] private GameplayTagContainer m_Tags = new GameplayTagContainer();
        [SerializeReference] private List<GameplayTagQueryExpression> m_Expressions = new List<GameplayTagQueryExpression>();

        public GameplayTagQueryExpressionType Type => m_Type;
        public GameplayTagContainer Tags => m_Tags ??= new GameplayTagContainer();
        public IReadOnlyList<GameplayTagQueryExpression> Expressions => m_Expressions ??= new List<GameplayTagQueryExpression>();

        public bool UsesTags =>
            m_Type is GameplayTagQueryExpressionType.AnyTagsMatch
                or GameplayTagQueryExpressionType.AllTagsMatch
                or GameplayTagQueryExpressionType.NoTagsMatch;

        public bool UsesExpressions =>
            m_Type is GameplayTagQueryExpressionType.AnyExpressionsMatch
                or GameplayTagQueryExpressionType.AllExpressionsMatch
                or GameplayTagQueryExpressionType.NoExpressionsMatch;

        public GameplayTagQueryExpression()
        {
        }

        public GameplayTagQueryExpression(GameplayTagQueryExpressionType type)
        {
            m_Type = type;
        }

        public static GameplayTagQueryExpression AnyTagsMatch() => new(GameplayTagQueryExpressionType.AnyTagsMatch);
        public static GameplayTagQueryExpression AllTagsMatch() => new(GameplayTagQueryExpressionType.AllTagsMatch);
        public static GameplayTagQueryExpression NoTagsMatch() => new(GameplayTagQueryExpressionType.NoTagsMatch);
        public static GameplayTagQueryExpression AnyExpressionsMatch() => new(GameplayTagQueryExpressionType.AnyExpressionsMatch);
        public static GameplayTagQueryExpression AllExpressionsMatch() => new(GameplayTagQueryExpressionType.AllExpressionsMatch);
        public static GameplayTagQueryExpression NoExpressionsMatch() => new(GameplayTagQueryExpressionType.NoExpressionsMatch);

        public GameplayTagQueryExpression SetType(GameplayTagQueryExpressionType type)
        {
            m_Type = type;
            return this;
        }

        public GameplayTagQueryExpression AddTag(GameplayTag tag)
        {
            RequireTagType();
            Tags.AddTag(tag);
            return this;
        }

        public GameplayTagQueryExpression AddTags(GameplayTagContainer tags)
        {
            RequireTagType();
            Tags.AppendTags(tags);
            return this;
        }

        public GameplayTagQueryExpression AddExpression(GameplayTagQueryExpression expression)
        {
            if (!UsesExpressions)
            {
                throw new InvalidOperationException(
                    "Child expressions can only be added to AnyExpressionsMatch, AllExpressionsMatch or NoExpressionsMatch expressions.");
            }
            if (expression == null)
                throw new ArgumentNullException(nameof(expression));
            if (ReferenceEquals(expression, this))
                throw new InvalidOperationException("A Gameplay Tag Query expression cannot contain itself.");

            m_Expressions ??= new List<GameplayTagQueryExpression>();
            if (!m_Expressions.Contains(expression))
                m_Expressions.Add(expression);
            return this;
        }

        public override string ToString()
        {
            var builder = new StringBuilder();
            Describe(builder, 0);
            return builder.ToString();
        }

        /// <summary><paramref name="depth"/> 是防止 SerializeReference 意外成环的兜底，不是业务概念。</summary>
        internal bool Matches(GameplayTagContainer tags, int depth)
        {
            if (depth >= MaxDepth)
                return false;

            return m_Type switch
            {
                GameplayTagQueryExpressionType.AnyTagsMatch => AnyTagMatches(tags),
                GameplayTagQueryExpressionType.AllTagsMatch => AllTagsMatch(tags),
                GameplayTagQueryExpressionType.NoTagsMatch => !AnyTagMatches(tags),
                GameplayTagQueryExpressionType.AnyExpressionsMatch => AnyChildMatches(tags, depth),
                GameplayTagQueryExpressionType.AllExpressionsMatch => AllChildrenMatch(tags, depth),
                GameplayTagQueryExpressionType.NoExpressionsMatch => !AnyChildMatches(tags, depth),
                _ => false
            };
        }

        private bool AnyTagMatches(GameplayTagContainer tags)
        {
            for (int i = 0; i < Tags.Count; i++)
            {
                if (tags.HasTag(Tags[i]))
                    return true;
            }
            return false;
        }

        private bool AllTagsMatch(GameplayTagContainer tags)
        {
            for (int i = 0; i < Tags.Count; i++)
            {
                if (!tags.HasTag(Tags[i]))
                    return false;
            }
            return true;
        }

        private bool AnyChildMatches(GameplayTagContainer tags, int depth)
        {
            for (int i = 0; i < Expressions.Count; i++)
            {
                if (Expressions[i] != null && Expressions[i].Matches(tags, depth + 1))
                    return true;
            }
            return false;
        }

        private bool AllChildrenMatch(GameplayTagContainer tags, int depth)
        {
            for (int i = 0; i < Expressions.Count; i++)
            {
                if (Expressions[i] == null || !Expressions[i].Matches(tags, depth + 1))
                    return false;
            }
            return true;
        }

        private void RequireTagType()
        {
            if (!UsesTags)
            {
                throw new InvalidOperationException(
                    "Tags can only be added to AnyTagsMatch, AllTagsMatch or NoTagsMatch expressions.");
            }
        }

        private void Describe(StringBuilder builder, int depth)
        {
            if (depth >= MaxDepth)
            {
                builder.Append("<invalid>");
                return;
            }

            builder.Append(m_Type).Append('(');
            if (UsesTags)
            {
                for (int i = 0; i < Tags.Count; i++)
                    builder.Append(i > 0 ? ", " : string.Empty).Append(Tags[i].Name);
            }
            else
            {
                for (int i = 0; i < Expressions.Count; i++)
                {
                    builder.Append(i > 0 ? ", " : string.Empty);
                    if (Expressions[i] == null)
                        builder.Append("<null>");
                    else
                        Expressions[i].Describe(builder, depth + 1);
                }
            }
            builder.Append(')');
        }
    }
}
