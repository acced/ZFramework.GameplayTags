using System;
using NUnit.Framework;
using UnityEngine;

namespace GameplayTags.Runtime.Tests
{
    public sealed class GameplayTagPlayerTests
    {
        private GameplayTagSettings m_Settings;
        private GameplayTagSettings m_Previous;
        private bool m_HadRegistry;
        private static object s_Sink;

        [SetUp]
        public void SetUp()
        {
            m_HadRegistry = GameplayTagManager.IsInitialized;
            m_Previous = m_HadRegistry ? GameplayTagManager.Settings : null;
            m_Settings = ScriptableObject.CreateInstance<GameplayTagSettings>();
            JsonUtility.FromJsonOverwrite(
                "{\"m_Sources\":[{\"m_Name\":\"Default\"}],\"m_Tags\":[" +
                "{\"m_Name\":\"Acceptance.Alive\",\"m_Source\":\"Default\"}," +
                "{\"m_Name\":\"Acceptance.Debuff.Burning\",\"m_Source\":\"Default\"}," +
                "{\"m_Name\":\"Acceptance.Debuff.Stunned\",\"m_Source\":\"Default\"}]," +
                "\"m_Redirects\":[{\"m_OldName\":\"Old.Burning\",\"m_NewName\":\"Acceptance.Debuff.Burning\"}]}",
                m_Settings);
            GameplayTagManager.Initialize(m_Settings, true);
        }

        [TearDown]
        public void TearDown()
        {
            GameplayTagManager.Initialize(m_HadRegistry ? m_Previous : null, true);
            UnityEngine.Object.DestroyImmediate(m_Settings);
            s_Sink = null;
        }

        [Test]
        public void PlayerHierarchyFrozenQueryAndAliases()
        {
            var child = GameplayTagManager.RequestTag("Acceptance.Debuff.Burning");
            var parent = GameplayTagManager.RequestTag("Acceptance.Debuff");
            var alive = GameplayTagManager.RequestTag("Acceptance.Alive");
            var owned = new GameplayTagContainer(16);
            owned.AddTag(child);
            Assert.IsTrue(owned.HasTag(parent));
            Assert.IsFalse(owned.HasTagExact(parent));
            Assert.IsFalse(parent.MatchesTag(child));
            var source = new GameplayTagQuery(GameplayTagQueryExpression.AllTagsMatch().AddTag(parent));
            var frozen = source.Freeze();
            source.RootExpression.AddTag(alive);
            Assert.IsTrue(frozen.Matches(owned));
            Assert.IsFalse(source.Freeze().Matches(owned));
            var other = new GameplayTagContainer(alive);
            GameplayTagContainer.UnionInto(owned, other, owned);
            GameplayTagContainer.IntersectionExactInto(owned, other, other);
            Assert.AreEqual(1, other.Count);
            Assert.IsTrue(other.HasTagExact(alive));
        }

        [Test]
        public void PlayerJsonAndRedirectResolution()
        {
            var owned = JsonUtility.FromJson<GameplayTagContainer>(
                "{\"m_GameplayTags\":[{\"m_Name\":\"Old.Burning\"},{\"m_Name\":\"Old.Burning\"}]}");
            owned.ResolveRegisteredTags();
            Assert.AreEqual(1, owned.Count);
            Assert.IsTrue(owned.HasTagExact(GameplayTagManager.RequestTag("Acceptance.Debuff.Burning")));
            var copy = JsonUtility.FromJson<GameplayTagContainer>(JsonUtility.ToJson(owned));
            Assert.IsTrue(copy.Equals(owned));
        }

        [Test]
        public void PlayerZeroAllocationWithPositiveControl()
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            s_Sink = new byte[4096];
            Assert.Greater(GC.GetAllocatedBytesForCurrentThread() - before, 0L,
                "Allocation counter is unsupported or inactive; a zero reading cannot certify zero allocation.");
            var tag = GameplayTagManager.RequestTag("Acceptance.Alive");
            var other = new GameplayTagContainer(tag);
            var owned = new GameplayTagContainer(16);
            var output = new GameplayTagContainer(16);
            var frozen = new GameplayTagQuery(GameplayTagQueryExpression.AllTagsMatch().AddTag(tag)).Freeze();
            int checksum = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 10000; i++)
                {
                    owned.CopyFrom(other);
                    checksum += owned.HasTag(tag) && owned.HasTagExact(tag) && frozen.Matches(owned) ? 1 : 0;
                    GameplayTagContainer.UnionInto(owned, other, output);
                    GameplayTagContainer.IntersectionExactInto(output, other, output);
                    owned.FilterInto(other, output);
                    owned.FilterExactInto(other, output);
                    owned.RemoveTag(tag);
                    owned.AddTag(tag);
                }
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                if (pass == 1) Assert.AreEqual(0L, allocated, "Core paths allocated after warmup.");
            }
            Assert.AreEqual(20000, checksum);
        }

        [Test]
        public void PlayerIdentity()
        {
#if ENABLE_IL2CPP
            const string backend = "IL2CPP";
#else
            const string backend = "Mono";
#endif
            TestContext.Progress.WriteLine("GAMEPLAYTAGS_PLAYER=" + Application.platform +
                ";unity=" + Application.unityVersion + ";backend=" + backend +
                ";pointerBits=" + (IntPtr.Size * 8) + ";cpu=" + SystemInfo.processorType);
            Assert.IsTrue(GameplayTagManager.IsInitialized);
        }
    }
}
