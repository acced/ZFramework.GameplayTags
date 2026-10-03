using System;
using System.Text;
using GameplayTags.Experiments;
using NUnit.Framework;
using UnityEngine;

namespace GameplayTags.Runtime.Tests
{
    /// <summary>Runs against the public API from the GameplayTags Runtime assembly.</summary>
    public sealed class DirectArraySetPlayerTests
    {
        private GameplayTagSettings settings;
        private GameplayTagSettings previous;
        private bool hadRegistry;
        private TagRegistry registry;

        [SetUp]
        public void SetUp()
        {
            hadRegistry = GameplayTagManager.IsInitialized;
            previous = hadRegistry ? GameplayTagManager.Settings : null;
            settings = ScriptableObject.CreateInstance<GameplayTagSettings>();
            var json = new StringBuilder("{\"m_Sources\":[{\"m_Name\":\"Default\"}],\"m_Tags\":[");
            json.Append("{\"m_Name\":\"State.Debuff.Burning\",\"m_Source\":\"Default\"}");
            for (int i = 0; i < 131; i++)
                json.Append(",{\"m_Name\":\"T").Append(i.ToString("D3")).Append("\",\"m_Source\":\"Default\"}");
            json.Append("],\"m_Redirects\":[]}");
            JsonUtility.FromJsonOverwrite(json.ToString(), settings);
            GameplayTagManager.Initialize(settings, true);
            registry = GameplayTagManager.CurrentRegistry;
        }

        [TearDown]
        public void TearDown()
        {
            GameplayTagManager.Initialize(hadRegistry ? previous : null, true);
            UnityEngine.Object.DestroyImmediate(settings);
        }

        private void Same(DirectArraySet set, Func<int, bool> expected)
        {
            int count = 0, previous = -1;
            for (int i = 0; i < registry.Count; i++)
            {
                Assert.AreEqual(expected(i), set.HasTagExact(registry.GetTagAt(i)));
                if (expected(i)) count++;
            }
            Assert.AreEqual(count, set.Count);
            foreach (RuntimeTag tag in set)
            {
                Assert.IsTrue(tag.RuntimeIndex > previous);
                Assert.IsTrue(ReferenceEquals(tag.Registry, registry));
                Assert.IsTrue(expected(tag.RuntimeIndex));
                previous = tag.RuntimeIndex;
            }
            set.AssertInvariants();
        }

        [Test]
        public void BuildersOwnInputAndResetValidatesBeforeMutation()
        {
            var tags = new[] { registry.GetTagAt(64), registry.GetTagAt(15), registry.GetTagAt(64), registry.GetTagAt(16) };
            var original = (RuntimeTag[])tags.Clone();
            foreach (BitmapLayout layout in new[] { BitmapLayout.Micro, BitmapLayout.Dense, BitmapLayout.Auto })
            {
                var set = DirectArraySet.FromUnordered(registry, tags, registry.Count, layout);
                for (int i = 0; i < tags.Length; i++) Assert.AreEqual(original[i], tags[i]);
                Same(set, i => i == 15 || i == 16 || i == 64);
                Assert.Throws<ArgumentException>(() => set.ResetFromSortedUnique(tags));
                Same(set, i => i == 15 || i == 16 || i == 64);
                Assert.Throws<ArgumentException>(() => set.ResetFromSortedUnique(new[] { default(RuntimeTag) }));
                var foreign = TagRegistry.Create(settings);
                Assert.Throws<ArgumentException>(() => set.ResetFromSortedUnique(new[] { foreign.GetTagAt(0) }));
                Same(set, i => i == 15 || i == 16 || i == 64);
                set.ResetFromSortedUnique(new[] { registry.GetTagAt(0), registry.GetTagAt(63), registry.GetTagAt(130) });
                Same(set, i => i == 0 || i == 63 || i == 130);
                set.ResetFromSortedUnique(Array.Empty<RuntimeTag>());
                Same(set, i => false);
            }
        }

        [Test]
        public void UnionHandlesEveryStoragePairGrowthAndBothAliases()
        {
            var layouts = new[] { BitmapLayout.Micro, BitmapLayout.Dense };
            foreach (BitmapLayout leftLayout in layouts)
            foreach (BitmapLayout rightLayout in layouts)
            foreach (BitmapLayout resultLayout in layouts)
            {
                var left = new DirectArraySet(registry, 0, leftLayout);
                var right = new DirectArraySet(registry, 0, rightLayout);
                for (int i = 0; i < registry.Count; i++)
                {
                    if (i % 3 == 0) left.AddTag(registry.GetTagAt(i));
                    if (i % 5 == 0) right.AddTag(registry.GetTagAt(i));
                }
                var output = new DirectArraySet(registry, 0, resultLayout);
                DirectArraySet.UnionInto(left, right, output);
                Same(output, i => i % 3 == 0 || i % 5 == 0);
                var leftAlias = new DirectArraySet(left);
                DirectArraySet.UnionInto(leftAlias, right, leftAlias);
                Same(leftAlias, i => i % 3 == 0 || i % 5 == 0);
                var rightAlias = new DirectArraySet(right);
                DirectArraySet.UnionInto(left, rightAlias, rightAlias);
                Same(rightAlias, i => i % 3 == 0 || i % 5 == 0);
                output.RemoveTags(right);
                Same(output, i => i % 3 == 0 && i % 5 != 0);
                output.CopyFrom(left);
                Same(output, i => i % 3 == 0);
                Same(left, i => i % 3 == 0);
                Same(right, i => i % 5 == 0);
            }
        }

        [Test]
        public void PublicEnumerationTransfersToExistingHierarchyAndFrozenQueries()
        {
            RuntimeTag child = registry.Resolve("State.Debuff.Burning");
            RuntimeTag parent = registry.Resolve("State.Debuff");
            var exact = DirectArraySet.FromUnordered(registry, new[] { child, child }, storage: BitmapLayout.Micro);
            Assert.IsTrue(exact.HasTagExact(child));
            Assert.IsFalse(exact.HasTagExact(parent));
            var hierarchy = new RuntimeTagSet(registry, exact.Count);
            foreach (RuntimeTag tag in exact) hierarchy.AddTag(tag);
            Assert.IsTrue(hierarchy.HasTag(parent));
            Assert.IsFalse(hierarchy.HasTagExact(parent));
            var query = new GameplayTagQuery(GameplayTagQueryExpression.AllTagsMatch().AddTag(GameplayTagManager.RequestTag(parent.Name))).Freeze(registry);
            Assert.IsTrue(query.Matches(hierarchy));
            Assert.IsTrue(exact.HasTag(parent));
            Assert.IsTrue(exact.Matches(query));
            Assert.IsFalse(query.Matches(null));
            Assert.IsFalse(exact.Matches(null));
            exact.Clear();
            Assert.IsFalse(exact.Matches(query));
            Assert.AreEqual(1, hierarchy.Count);
            Assert.IsTrue(query.Matches(hierarchy));
            foreach (RuntimeTag tag in hierarchy) exact.AddTag(tag);
            Assert.AreEqual(1, exact.Count);
            Assert.IsTrue(exact.HasTagExact(child));
        }

        [Test]
        public void StorageConversionRemainsIndependentAndEmptyCopyClears()
        {
            var first = registry.GetTagAt(15);
            var last = registry.GetTagAt(registry.Count - 1);
            var source = DirectArraySet.FromSortedUnique(registry, new[] { first, last }, storage: BitmapLayout.Micro);
            var dense = source.CopyAsStorage(BitmapLayout.Dense);
            var micro = dense.CopyAsStorage(BitmapLayout.Micro);
            source.Clear();
            Assert.AreEqual(2, dense.Count);
            Assert.AreEqual(2, micro.Count);
            Assert.IsTrue(micro.HasTagExact(last));
            dense.RemoveTag(first);
            Assert.IsTrue(micro.HasTagExact(first));
            micro.CopyFrom(source);
            Assert.AreEqual(0, micro.Count);
            micro.AssertInvariants();
        }
    }
}
