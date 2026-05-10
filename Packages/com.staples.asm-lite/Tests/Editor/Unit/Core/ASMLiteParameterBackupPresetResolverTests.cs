using System;
using NUnit.Framework;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace ASMLite.Tests.Editor
{
    [TestFixture]
    public class ASMLiteParameterBackupPresetResolverTests
    {
        private AsmLiteTestContext _ctx;

        [SetUp]
        public void SetUp()
        {
            ASMLite.Editor.ASMLiteToggleNameBroker.ResetLatestEnrollmentStateForTests();
            ASMLite.Editor.ASMLiteToggleNameBroker.ClearPendingRestoreState();
            _ctx = ASMLiteTestFixtures.CreateTestAvatar();
        }

        [TearDown]
        public void TearDown()
        {
            ASMLite.Editor.ASMLiteToggleNameBroker.ClearPendingRestoreState();
            ASMLite.Editor.ASMLiteToggleNameBroker.ResetLatestEnrollmentStateForTests();
            ASMLiteTestFixtures.TearDownTestAvatar(_ctx?.AvatarGo);
            _ctx = null;
        }

        [Test]
        public void StablePresetIds_ResolveAgainstVisibleBackupOptions()
        {
            string[] visibleOptions =
            {
                "Fixture/ParamC",
                "Fixture/ParamA",
                "Fixture/ParamB",
            };

            CollectionAssert.AreEqual(
                Array.Empty<string>(),
                ASMLite.Editor.ASMLiteParameterBackupPresetResolver.ResolvePresetExcludedNames(
                    ASMLite.Editor.ASMLiteParameterBackupPresetResolver.NoneExcludedPresetId,
                    visibleOptions));
            CollectionAssert.AreEqual(
                new[] { "Fixture/ParamA" },
                ASMLite.Editor.ASMLiteParameterBackupPresetResolver.ResolvePresetExcludedNames(
                    ASMLite.Editor.ASMLiteParameterBackupPresetResolver.SingleVisiblePresetId,
                    visibleOptions));
            CollectionAssert.AreEqual(
                new[] { "Fixture/ParamA", "Fixture/ParamB" },
                ASMLite.Editor.ASMLiteParameterBackupPresetResolver.ResolvePresetExcludedNames(
                    ASMLite.Editor.ASMLiteParameterBackupPresetResolver.FirstTwoVisiblePresetId,
                    visibleOptions));
        }

        [Test]
        public void ExactVisibleNames_NormalizeBeforeCompare_AndSortForSnapshotStorage()
        {
            string[] visibleOptions =
            {
                "Fixture/ParamB",
                "Fixture/ParamC",
                "Fixture/ParamA",
            };

            string[] resolved = ASMLite.Editor.ASMLiteParameterBackupPresetResolver.ResolveExactExcludedNames(
                new[] { " Fixture\\ParamC ", "Fixture/ ParamA ", "Fixture//ParamB" },
                visibleOptions);

            CollectionAssert.AreEqual(
                new[] { "Fixture/ParamA", "Fixture/ParamB", "Fixture/ParamC" },
                resolved,
                "Exact-name backup exclusions should compare after trimming, slash normalization, segment trimming, dedupe, and ordinal sorting.");
        }

        [Test]
        public void UnknownPresetId_FailsReadably()
        {
            bool ok = ASMLite.Editor.ASMLiteParameterBackupPresetResolver.TryResolvePresetExcludedNames(
                "sideways",
                new[] { "Fixture/ParamA" },
                out var excluded,
                out string errorMessage);

            Assert.IsFalse(ok);
            CollectionAssert.AreEqual(Array.Empty<string>(), excluded);
            StringAssert.Contains("Unknown parameter backup preset ID 'sideways'", errorMessage);
            StringAssert.Contains(ASMLite.Editor.ASMLiteParameterBackupPresetResolver.NoneExcludedPresetId, errorMessage);
            StringAssert.Contains(ASMLite.Editor.ASMLiteParameterBackupPresetResolver.SingleVisiblePresetId, errorMessage);
            StringAssert.Contains(ASMLite.Editor.ASMLiteParameterBackupPresetResolver.FirstTwoVisiblePresetId, errorMessage);
        }

        [Test]
        public void PresetMissingFromVisibleOptions_FailsReadably()
        {
            bool ok = ASMLite.Editor.ASMLiteParameterBackupPresetResolver.TryResolvePresetExcludedNames(
                ASMLite.Editor.ASMLiteParameterBackupPresetResolver.FirstTwoVisiblePresetId,
                new[] { "Fixture/ParamA" },
                out var excluded,
                out string errorMessage);

            Assert.IsFalse(ok);
            CollectionAssert.AreEqual(Array.Empty<string>(), excluded);
            StringAssert.Contains("parameter backup preset ID 'first-two-visible'", errorMessage);
            StringAssert.Contains("requires at least 2 visible parameter backup option", errorMessage);
        }

        [Test]
        public void SeededVrcFuryVisibleOptions_CanBeAssertedForPresetResolution()
        {
            var sourceRoot = new GameObject("FixtureSource");
            sourceRoot.transform.SetParent(_ctx.AvatarGo.transform, false);
            var sourceVf = sourceRoot.AddComponent<VF.Model.VRCFury>();
            sourceVf.content = new VF.Model.Feature.Toggle
            {
                useGlobalParam = true,
                globalParam = "Fixture/Source/One",
                name = "Fixture Source One",
                menuPath = string.Empty,
            };

            var referencedRoot = new GameObject("FixtureReferenced");
            referencedRoot.transform.SetParent(_ctx.AvatarGo.transform, false);
            var referencedVf = referencedRoot.AddComponent<VF.Model.VRCFury>();
            var referencedParams = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            referencedParams.parameters = new[]
            {
                new VRCExpressionParameters.Parameter
                {
                    name = "Fixture/Referenced/A",
                    valueType = VRCExpressionParameters.ValueType.Bool,
                    defaultValue = 0f,
                    saved = false,
                    networkSynced = false,
                },
                new VRCExpressionParameters.Parameter
                {
                    name = "Fixture/Referenced/B",
                    valueType = VRCExpressionParameters.ValueType.Float,
                    defaultValue = 0f,
                    saved = false,
                    networkSynced = false,
                },
            };

            try
            {
                referencedVf.content = new VF.Model.Feature.FullControllerLike
                {
                    prms = new[]
                    {
                        new VF.Model.Feature.FullControllerLikePrmsEntry
                        {
                            parameters = new VF.Model.Feature.FullControllerLikeParamsRef
                            {
                                objRef = referencedParams,
                                id = string.Empty,
                            },
                        },
                    },
                };

                string[] visibleOptions = ASMLite.Editor.ASMLiteWindow.GetVisibleParameterBackupOptionsForTesting(_ctx.AvDesc);

                CollectionAssert.Contains(visibleOptions, "Fixture/Source/One");
                CollectionAssert.Contains(visibleOptions, "Fixture/Referenced/A");
                CollectionAssert.Contains(visibleOptions, "Fixture/Referenced/B");

                CollectionAssert.AreEqual(
                    new[] { "Fixture/Referenced/A" },
                    ASMLite.Editor.ASMLiteParameterBackupPresetResolver.ResolvePresetExcludedNames(
                        ASMLite.Editor.ASMLiteParameterBackupPresetResolver.SingleVisiblePresetId,
                        visibleOptions));
                CollectionAssert.AreEqual(
                    new[] { "Fixture/Referenced/A", "Fixture/Referenced/B" },
                    ASMLite.Editor.ASMLiteParameterBackupPresetResolver.ResolvePresetExcludedNames(
                        ASMLite.Editor.ASMLiteParameterBackupPresetResolver.FirstTwoVisiblePresetId,
                        visibleOptions));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(referencedParams);
            }
        }
    }
}
