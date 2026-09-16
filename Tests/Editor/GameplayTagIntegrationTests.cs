using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using GameplayTags.Editor;

namespace GameplayTags.Tests
{
    // Run these in an isolated Unity test project. The managed audit does not execute these tests.
    public sealed class GameplayTagIntegrationTests
    {
        private GameplayTagSettings m_Settings;
        private GameplayTagSettings m_Previous;
        private bool m_WasInitialized;

        [SetUp]
        public void SetUp()
        {
            m_WasInitialized = GameplayTagManager.IsInitialized;
            m_Previous = m_WasInitialized ? GameplayTagManager.Settings : null;
            m_Settings = ScriptableObject.CreateInstance<GameplayTagSettings>();
            m_Settings.ReplaceAll(
                new List<GameplayTagDefinition>
                {
                    new GameplayTagDefinition("State.Debuff.Burning", "", "Default", false, true),
                    new GameplayTagDefinition("State.Alive", "", "Default", false, true)
                },
                new List<GameplayTagRedirect> { new GameplayTagRedirect("Old.Burning", "State.Debuff.Burning") },
                new List<GameplayTagSource> { new GameplayTagSource("Default", "", false) });
            GameplayTagManager.Initialize(m_Settings, true);
        }
        [TearDown]
        public void TearDown()
        {
            // Do not clear the user's event subscribers as part of routine test cleanup.
            if (m_WasInitialized) GameplayTagManager.Initialize(m_Previous, true);
            else GameplayTagManager.ResetForTests();
            UnityEngine.Object.DestroyImmediate(m_Settings);
        }
        [Test]
        public void HierarchyDirectionAndExactness()
        {
            var parent = GameplayTagManager.RequestTag("State.Debuff");
            var child = GameplayTagManager.RequestTag("State.Debuff.Burning");
            Assert.IsTrue(child.MatchesTag(parent));
            Assert.IsFalse(parent.MatchesTag(child));
            var owned = new GameplayTagContainer(child);
            Assert.IsTrue(owned.HasTag(parent));
            Assert.IsFalse(owned.HasTagExact(parent));
        }
        [Test]
        public void NativeJsonRestoresSortedUniqueContainer()
        {
            var owned = JsonUtility.FromJson<GameplayTagContainer>(
                "{\"m_GameplayTags\":[{\"m_Name\":\"State.Debuff.Burning\"},{\"m_Name\":\"State.Alive\"},{\"m_Name\":\"State.Alive\"},{\"m_Name\":\"\"}]}");
            Assert.AreEqual(2, owned.Count);
            Assert.AreEqual("State.Alive", owned[0].Name);
            var copy = JsonUtility.FromJson<GameplayTagContainer>(JsonUtility.ToJson(owned));
            Assert.IsTrue(owned.Equals(copy));
        }
        [Test]
        public void SerializedRedirectResolutionIsAtomic()
        {
            var owned = JsonUtility.FromJson<GameplayTagContainer>(
                "{\"m_GameplayTags\":[{\"m_Name\":\"Old.Burning\"},{\"m_Name\":\"Missing\"}]}");
            string before = JsonUtility.ToJson(owned);
            Assert.Throws<KeyNotFoundException>(() => owned.ResolveRegisteredTags());
            Assert.AreEqual(before, JsonUtility.ToJson(owned));
        }
        [Test]
        public void FrozenQueryIsIsolatedFromSourceEdits()
        {
            var expression = GameplayTagQueryExpression.AllTagsMatch().AddTag(GameplayTagManager.RequestTag("State"));
            var matcher = new GameplayTagQuery(expression).Freeze();
            expression.AddTag(GameplayTagManager.RequestTag("State.Alive"));
            var owned = new GameplayTagContainer(GameplayTagManager.RequestTag("State.Debuff.Burning"));
            Assert.IsTrue(matcher.Matches(owned));
            Assert.IsFalse(new GameplayTagQuery(expression).Freeze().Matches(owned));
        }
        [Test]
        public void InvalidBranchCannotHideBehindNegation()
        {
            var expression = GameplayTagQueryExpression.NoExpressionsMatch()
                .AddExpression(new GameplayTagQueryExpression(GameplayTagQueryExpressionType.Undefined));
            Assert.Throws<InvalidOperationException>(() => new GameplayTagQuery(expression).Freeze());
        }
        [Test]
        public void IntoOperationsPreserveInputAliases()
        {
            var left = new GameplayTagContainer(GameplayTagManager.RequestTag("State.Alive"));
            var right = new GameplayTagContainer(GameplayTagManager.RequestTag("State.Debuff.Burning"));
            left.Capacity = 2;
            GameplayTagContainer.UnionInto(left, right, left);
            Assert.AreEqual(2, left.Count);
            GameplayTagContainer.IntersectionExactInto(left, right, right);
            Assert.AreEqual(1, right.Count);
            Assert.AreEqual("State.Debuff.Burning", right[0].Name);
        }
        [Test]
        public void EditorTransactionRejectsCaseConflictWithoutChangingSettings()
        {
            uint before = m_Settings.ComputeContentHash();
            int revision = m_Settings.Revision;
            Assert.IsFalse(GameplayTagEditorUtility.TryAddTag(m_Settings, "state.Other", out _));
            Assert.AreEqual(before, m_Settings.ComputeContentHash());
            Assert.AreEqual(revision, m_Settings.Revision);
        }
    }
}
