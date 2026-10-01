using System;
using NUnit.Framework;
using UnityEngine;

namespace GameplayTags.Runtime.Tests
{
    // These tests are shipped for real Unity/Mono/IL2CPP execution. The managed audit only
    // compiles their API boundaries and does not substitute a facade for native execution.
    public sealed class RuntimeRefactorPlayerTests
    {
        private GameplayTagSettings m_Settings;
        private GameplayTagSettings m_Previous;
        private bool m_HadRegistry;
        private TagRegistry m_Registry;
        private static object s_Sink;
        private static readonly TagSetStorage[] Modes = { TagSetStorage.Sparse, TagSetStorage.Dense };

        [SetUp]
        public void SetUp()
        {
            m_HadRegistry = GameplayTagManager.IsInitialized;
            m_Previous = m_HadRegistry ? GameplayTagManager.Settings : null;
            m_Settings = ScriptableObject.CreateInstance<GameplayTagSettings>();
            JsonUtility.FromJsonOverwrite(
                "{\"m_Sources\":[{\"m_Name\":\"Default\"}],\"m_Tags\":[" +
                "{\"m_Name\":\"A.B.C\",\"m_Source\":\"Default\"}," +
                "{\"m_Name\":\"A.B.D\",\"m_Source\":\"Default\"}," +
                "{\"m_Name\":\"A!x\",\"m_Source\":\"Default\"}," +
                "{\"m_Name\":\"Z\",\"m_Source\":\"Default\"}]}", m_Settings);
            GameplayTagManager.Initialize(m_Settings, true);
            m_Registry = GameplayTagManager.CurrentRegistry;
        }
        [TearDown]
        public void TearDown()
        {
            GameplayTagManager.Initialize(m_HadRegistry ? m_Previous : null, true);
            UnityEngine.Object.DestroyImmediate(m_Settings);
            s_Sink = null;
        }
        private RuntimeTag[] Handles(params string[] names)
        {
            var tags = new RuntimeTag[names.Length];
            for (int i = 0; i < names.Length; i++) tags[i] = m_Registry.Resolve(names[i]);
            return tags;
        }
        [Test]
        public void PlayerBulkLoadingConversionsAndForeignHandles()
        {
            foreach (var mode in Modes)
            {
                var tags = Handles("Z", "A.B.C", "A!x", "A.B.C");
                tags[3] = RuntimeTag.None;
                var source = RuntimeTagSet.FromTags(m_Registry, tags, 0, mode);
                Assert.AreEqual(3, source.Count);
                foreach (var outputMode in Modes)
                {
                    var copy = source.ToStorage(outputMode);
                    Assert.IsTrue(copy.SetEquals(source));
                    copy.Clear();
                    Assert.AreEqual(3, source.Count);
                }
                var foreign = TagRegistry.Create(m_Settings).Resolve("Z");
                Assert.Throws<ArgumentException>(() => RuntimeTagSet.FromTags(m_Registry,
                    new[] { tags[0], foreign }, 0, mode));
                Assert.IsFalse(source.HasTagExact(RuntimeTag.None));
            }
        }
        [Test]
        public void PlayerDirectDifferenceAllStorageAndAliasCombinations()
        {
            foreach (var leftMode in Modes) foreach (var rightMode in Modes) foreach (var outputMode in Modes)
            {
                var left = RuntimeTagSet.FromTags(m_Registry, Handles("Z", "A.B.C", "A!x"), 0, leftMode);
                var right = RuntimeTagSet.FromTags(m_Registry, Handles("A.B.C", "A.B.D"), 0, rightMode);
                var output = new RuntimeTagSet(m_Registry, m_Registry.Count, outputMode);
                RuntimeTagSet.DifferenceExactInto(left, right, output);
                Assert.AreEqual(2, output.Count);
                Assert.IsTrue(output.HasTagExact(m_Registry.Resolve("Z")));
                Assert.IsTrue(output.HasTagExact(m_Registry.Resolve("A!x")));
                var expected = new RuntimeTagSet(output);
                var leftAlias = new RuntimeTagSet(left);
                RuntimeTagSet.DifferenceExactInto(leftAlias, right, leftAlias);
                Assert.IsTrue(leftAlias.SetEquals(expected));
                var rightAlias = new RuntimeTagSet(right);
                RuntimeTagSet.DifferenceExactInto(left, rightAlias, rightAlias);
                Assert.IsTrue(rightAlias.SetEquals(expected));
                RuntimeTagSet.DifferenceExactInto(left, left, output);
                Assert.IsTrue(output.IsEmpty);
                Assert.AreEqual(3, left.Count);
                Assert.AreEqual(2, right.Count);
            }
        }
        [Test]
        public void PlayerDirectDifferencePreparedAllocation()
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            s_Sink = new byte[4096];
            Assert.Greater(GC.GetAllocatedBytesForCurrentThread() - before, 0L,
                "An inactive allocation counter cannot certify zero allocation.");
            foreach (var mode in Modes)
            {
                var left = RuntimeTagSet.FromTags(m_Registry, Handles("A.B.C", "A!x", "Z"), 0, mode);
                var right = RuntimeTagSet.FromTags(m_Registry, Handles("A.B.C"), 0, mode);
                var output = new RuntimeTagSet(m_Registry, m_Registry.Count, mode);
                int checksum = 0;
                for (int pass = 0; pass < 2; pass++)
                {
                    before = GC.GetAllocatedBytesForCurrentThread();
                    for (int i = 0; i < 10000; i++)
                    {
                        RuntimeTagSet.DifferenceExactInto(left, right, output);
                        checksum += output.Count;
                    }
                    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                    if (pass == 1) Assert.AreEqual(0L, allocated);
                }
                Assert.AreEqual(40000, checksum);
            }
        }
    }
}
