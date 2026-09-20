using System;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEngine.TestTools;
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
            var parent = GameplayTagManager.CurrentRegistry.Resolve("State.Debuff");
            var child = GameplayTagManager.CurrentRegistry.Resolve("State.Debuff.Burning");
            Assert.IsTrue(child.MatchesTag(parent));
            Assert.IsFalse(parent.MatchesTag(child));
            var owned = new RuntimeTagSet(GameplayTagManager.CurrentRegistry, 4);
            owned.AddTag(child);
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
            var owned = new GameplayTagContainer(GameplayTagManager.RequestTag("State.Debuff.Burning")).ToRuntime();
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
            var left = new GameplayTagContainer(GameplayTagManager.RequestTag("State.Alive")).ToRuntime();
            var right = new GameplayTagContainer(GameplayTagManager.RequestTag("State.Debuff.Burning")).ToRuntime();
            left.EnsureCapacity(2);
            RuntimeTagSet.UnionInto(left, right, left);
            Assert.AreEqual(2, left.Count);
            RuntimeTagSet.IntersectionExactInto(left, right, right);
            Assert.AreEqual(1, right.Count);
            Assert.IsTrue(right.HasTagExact(GameplayTagManager.CurrentRegistry.Resolve("State.Debuff.Burning")));
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
        [Test]
        public void NativeUndoRedoRestoresSettings()
        {
            uint original = m_Settings.ComputeContentHash();
            Undo.RecordObject(m_Settings, "Acceptance add tag");
            Assert.IsTrue(GameplayTagEditorUtility.TryAddTag(m_Settings, "State.Extra", out string error), error);
            Undo.FlushUndoRecordObjects();
            uint changed = m_Settings.ComputeContentHash();
            Assert.AreNotEqual(original, changed);
            Undo.PerformUndo();
            Assert.AreEqual(original, m_Settings.ComputeContentHash());
            Undo.PerformRedo();
            Assert.AreEqual(changed, m_Settings.ComputeContentHash());
            Undo.ClearUndo(m_Settings);
        }

        [Test]
        public void SerializeReferenceAssetSurvivesDiskReload()
        {
            string path = "Assets/GameplayTagsQueryAcceptance-" + Guid.NewGuid().ToString("N") + ".asset";
            var asset = ScriptableObject.CreateInstance<GameplayTagQueryTestAsset>();
            try
            {
                asset.Query = new GameplayTagQuery(GameplayTagQueryExpression.AllTagsMatch()
                    .AddTag(GameplayTagManager.RequestTag("State.Debuff")));
                AssetDatabase.CreateAsset(asset, path);
                AssetDatabase.SaveAssets();
                Resources.UnloadAsset(asset);
                asset = AssetDatabase.LoadAssetAtPath<GameplayTagQueryTestAsset>(path);
                Assert.IsNotNull(asset);
                Assert.IsTrue(asset.Query.Freeze().Matches(new GameplayTagContainer(
                    GameplayTagManager.RequestTag("State.Debuff.Burning")).ToRuntime()));
            }
            finally
            {
                AssetDatabase.DeleteAsset(path);
                if (asset != null && !EditorUtility.IsPersistent(asset)) UnityEngine.Object.DestroyImmediate(asset);
            }
        }
    }

    public sealed class GameplayTagLifecycleTests
    {
        private bool m_OptionsEnabled;
        private EnterPlayModeOptions m_Options;
        private Action m_Callback;
        private int m_CallbackCount;

        [SetUp]
        public void SetUp()
        {
            m_OptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            m_Options = EditorSettings.enterPlayModeOptions;
            Assert.IsTrue(File.Exists("GAMEPLAYTAGS_ACCEPTANCE_PROJECT"),
                "Run this fixture in the isolated project made by Audit~/unity_acceptance.py.");
            m_CallbackCount = 0;
            m_Callback = () => m_CallbackCount++;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            GameplayTagManager.RegistryChanged -= m_Callback;
            if (Application.isPlaying) yield return new ExitPlayMode();
            EditorSettings.enterPlayModeOptionsEnabled = m_OptionsEnabled;
            EditorSettings.enterPlayModeOptions = m_Options;
        }

        [UnityTest]
        public IEnumerator StaticStateResetsWithDomainReloadDisabled()
        {
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            GameplayTagManager.RegistryChanged += m_Callback;
            yield return new EnterPlayMode(false);
            Assert.IsTrue(GameplayTagManager.IsInitialized);
            Assert.AreEqual(0, m_CallbackCount, "An old editor subscriber survived a new play session.");
            GameplayTagManager.RegistryChanged += m_Callback;
            yield return new ExitPlayMode();
            int callsAfterExit = m_CallbackCount;
            yield return new EnterPlayMode(false);
            Assert.IsTrue(GameplayTagManager.IsInitialized);
            Assert.AreEqual(callsAfterExit, m_CallbackCount, "A subscriber survived the second play session.");
            yield return new ExitPlayMode();
        }
    }

    public static class AcceptanceProjectSetup
    {
        public static void Prepare()
        {
            if (!File.Exists("GAMEPLAYTAGS_ACCEPTANCE_PROJECT"))
                throw new InvalidOperationException("Refusing to modify an unmarked Unity project.");
            var settings = GameplayTagEditorUtility.GetSettings(true);
            foreach (string name in new[]{"Acceptance.Alive", "Acceptance.Debuff.Burning", "Acceptance.Debuff.Stunned"})
            {
                if (!settings.TryGetTagDefinition(name, out _) &&
                    !GameplayTagEditorUtility.TryAddTag(settings, name, out string error))
                    throw new InvalidOperationException(error);
            }
            GameplayTagEditorUtility.SaveAndReinitialize(settings);
            GameplayTagCodeGenerator.Generate(settings);
            Debug.Log("GAMEPLAYTAGS_UNITY_VERSION=" + Application.unityVersion);
        }
    }
}
