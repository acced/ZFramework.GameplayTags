using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace GameplayTags.Tests
{
    /// <summary>
    /// Opt-in Unity integration tests. Run in an isolated test project because the
    /// registry is process-global. These are not performance measurements.
    /// </summary>
    [NonParallelizable]
    public sealed class ReleaseContractTests
    {
        private GameplayTagSettings m_Settings;
        private GameplayTagSettings m_PreviousSettings;
        private bool m_WasInitialized;

        [SetUp]
        public void SetUp()
        {
            m_WasInitialized = GameplayTagManager.IsInitialized;
            m_PreviousSettings = m_WasInitialized ? GameplayTagManager.Settings : null;
            m_Settings = ScriptableObject.CreateInstance<GameplayTagSettings>();
            var definitions = new List<GameplayTagDefinition>();
            foreach (string name in new[] { "A", "A!", "A.B", "A.C", "B", "State.Burning", "State.Frozen" })
                definitions.Add(new GameplayTagDefinition(name, "", "", false, true));
            for (int i = 0; i < 4096; i++)
                definitions.Add(new GameplayTagDefinition(NumberName(i), "", "", false, true));
            m_Settings.ReplaceAll(definitions, new List<GameplayTagRedirect>(), new List<GameplayTagSource>());
            m_Settings.SetEditorOptions(false, true, false, "Game", "GameplayTags", "Assets/Unused.gen.cs");
            GameplayTagManager.Initialize(m_Settings, true);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (m_WasInitialized) GameplayTagManager.Initialize(m_PreviousSettings, true);
                else GameplayTagManager.ResetForTests();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(m_Settings);
            }
        }

        private static string NumberName(int value) => "T" + value.ToString("D5");
        private static GameplayTag Tag(string name) => GameplayTagManager.RequestTag(name);
        private static GameplayTagContainer Set(params string[] names)
        {
            var result = new GameplayTagContainer(names.Length);
            foreach (string name in names) result.AddTag(Tag(name));
            return result;
        }
        private static string[] Names(GameplayTagContainer value)
        {
            var names = new List<string>();
            foreach (GameplayTag tag in value) names.Add(tag.Name);
            return names.ToArray();
        }
        private static void AssertNames(GameplayTagContainer value, params string[] expected)
        {
            CollectionAssert.AreEqual(expected, Names(value));
            Assert.That(value.Count, Is.EqualTo(expected.Length));
            for (int i = 0; i < expected.Length; i++) Assert.That(value[i].Name, Is.EqualTo(expected[i]));
        }

        [Test]
        public void HierarchyDoesNotBecomeAnExplicitMember()
        {
            var a = Set("State.Burning");
            var b = Set("State.Frozen");
            Assert.That(a.HasTag(Tag("State")), Is.True);
            Assert.That(a.HasTagExact(Tag("State")), Is.False);
            Assert.That(GameplayTagContainer.IntersectionExact(a, b).IsEmpty, Is.True);
            AssertNames(a, "State.Burning");
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void UnionPreservesOrderAndLeftRepresentatives(int alias)
        {
            var leftTag = new GameplayTag(new string(new[] { 'A', '.', 'B' }));
            var rightTag = new GameplayTag(new string(new[] { 'A', '.', 'B' }));
            var left = Set("A!"); left.AddTag(leftTag);
            var right = Set("B"); right.AddTag(rightTag);
            var target = alias == 1 ? left : alias == 2 ? right : Set("State.Frozen");
            target.SetUnion(left, right);
            AssertNames(target, "A!", "A.B", "B");
            Assert.That(ReferenceEquals(target[1].Name, leftTag.Name), Is.True);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void IntersectionAndDifferenceSupportAliases(int alias)
        {
            var left = Set("A", "A.B", "B");
            var right = Set("A.B", "A.C");
            var target = alias == 1 ? left : alias == 2 ? right : Set("State.Frozen");
            target.SetIntersectionExact(left, right);
            AssertNames(target, "A.B");
            target.ExceptWithExact(Set("A.B"));
            Assert.That(target.IsEmpty, Is.True);
            target.SetIntersectionExact(target, null);
            Assert.That(target.IsEmpty, Is.True);
        }

        [Test]
        public void CopyEmptyAndSingletonReplacePopulatedTargets()
        {
            var target = Set("A", "A.B", "B");
            target.CopyFrom(new GameplayTagContainer());
            Assert.That(target.IsEmpty, Is.True);
            var sourceTag = new GameplayTag(new string(new[] { 'A', '.', 'B' }));
            var source = new GameplayTagContainer(sourceTag);
            target.CopyFrom(source);
            AssertNames(target, "A.B");
            Assert.That(ReferenceEquals(target[0].Name, sourceTag.Name), Is.True);
            target.CopyFrom(target);
            AssertNames(target, "A.B");
            target.CopyFrom(null);
            Assert.That(target.IsEmpty, Is.True);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void LateContainmentFailurePreservesPrefixAndFutureGrowth(int alias)
        {
            var target = new GameplayTagContainer(1024);
            var source = new GameplayTagContainer(1023);
            for (int i = 0; i < 1024; i++) target.AddTag(Tag(NumberName(2 * i)));
            for (int i = 1; i < 1023; i++) source.AddTag(Tag(NumberName(2 * i)));
            source.AddTag(Tag(NumberName(2045)));
            if (alias == 0) target.AppendTags(source);
            else if (alias == 1) target.SetUnion(target, source);
            else target.SetUnion(source, target);
            Assert.That(target.Count, Is.EqualTo(1025));
            Assert.That(target[0].Name, Is.EqualTo(NumberName(0)));
            Assert.That(target[1023].Name, Is.EqualTo(NumberName(2045)));
            Assert.That(target[1024].Name, Is.EqualTo(NumberName(2046)));
            // Verify correctness after the tighter reservation, not a timing claim.
            for (int i = 2047; i < 2080; i++) Assert.That(target.AddTag(Tag(NumberName(i))), Is.True);
            Assert.That(target.Count, Is.EqualTo(1058));
            Assert.That(target.RemoveTag(Tag(NumberName(2045))), Is.True);
            Assert.That(target.AddTag(Tag(NumberName(2045))), Is.True);
            string[] names = Names(target);
            CollectionAssert.AreEqual(names.OrderBy(n => n, StringComparer.Ordinal).ToArray(), names);
        }

        [Test]
        public void JsonRoundTripKeepsUnknownNamesUntilValidatedInsertion()
        {
            const string json = "{\"m_GameplayTags\":[{\"m_Name\":\"Unknown.Child\"},{\"m_Name\":\"A.B\"},{\"m_Name\":\"A.B\"},{\"m_Name\":\"\"}]}";
            var source = JsonUtility.FromJson<GameplayTagContainer>(json);
            AssertNames(source, "A.B", "Unknown.Child");
            var roundTrip = JsonUtility.FromJson<GameplayTagContainer>(JsonUtility.ToJson(source));
            AssertNames(roundTrip, "A.B", "Unknown.Child");
            var copied = new GameplayTagContainer(); copied.CopyFrom(roundTrip);
            AssertNames(copied, "A.B");
            AssertNames(GameplayTagContainer.IntersectionExact(source, roundTrip), "A.B", "Unknown.Child");
            roundTrip.Clear();
            Assert.That(JsonUtility.FromJson<GameplayTagContainer>(JsonUtility.ToJson(roundTrip)).IsEmpty, Is.True);
        }

        [Test]
        public void ReinitializationKeepsExistingNameIdentity()
        {
            var old = Tag("A.B");
            var existing = new GameplayTagContainer(old);
            GameplayTagManager.Initialize(m_Settings, true);
            Assert.That(existing.HasTagExact(old), Is.True);
            Assert.That(old, Is.EqualTo(Tag("A.B")));
            AssertNames(existing, "A.B");
        }

        [Test]
        public void MutableQueryUpdatesImmediately()
        {
            var expression = GameplayTagQueryExpression.AllTagsMatch().AddTag(Tag("A"));
            var query = new GameplayTagQuery(expression);
            var target = Set("A.B");
            Assert.That(query.Matches(target), Is.True);
            expression.AddTag(Tag("B"));
            Assert.That(query.Matches(target), Is.False);
            target.AddTag(Tag("B"));
            Assert.That(query.Matches(target), Is.True);
        }
    }
}
