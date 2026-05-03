using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
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
                "AvatarLimbScaling_Arms",
                "Unrelated/Keep",
                "VRCOSC/Media/Play",
                "VRCOSC/Media/Volume",
            };

            CollectionAssert.AreEqual(
                Array.Empty<string>(),
                ASMLite.Editor.ASMLiteParameterBackupPresetResolver.ResolvePresetExcludedNames(
                    ASMLite.Editor.ASMLiteParameterBackupPresetResolver.NoneExcludedPresetId,
                    visibleOptions));
            CollectionAssert.AreEqual(
                new[] { "AvatarLimbScaling_Arms" },
                ASMLite.Editor.ASMLiteParameterBackupPresetResolver.ResolvePresetExcludedNames(
                    ASMLite.Editor.ASMLiteParameterBackupPresetResolver.SingleArmsPresetId,
                    visibleOptions));
            CollectionAssert.AreEqual(
                new[] { "VRCOSC/Media/Play", "VRCOSC/Media/Volume" },
                ASMLite.Editor.ASMLiteParameterBackupPresetResolver.ResolvePresetExcludedNames(
                    ASMLite.Editor.ASMLiteParameterBackupPresetResolver.NestedMediaPresetId,
                    visibleOptions));
        }

        [Test]
        public void ExactVisibleNames_NormalizeBeforeCompare_AndSortForSnapshotStorage()
        {
            string[] visibleOptions =
            {
                "AvatarLimbScaling_Arms",
                "VRCOSC/Media/Volume",
                "VRCOSC/Media/Play",
            };

            string[] resolved = ASMLite.Editor.ASMLiteParameterBackupPresetResolver.ResolveExactExcludedNames(
                new[] { " VRCOSC\\Media//Volume ", "AvatarLimbScaling_Arms ", "VRCOSC/ Media / Play" },
                visibleOptions);

            CollectionAssert.AreEqual(
                new[] { "AvatarLimbScaling_Arms", "VRCOSC/Media/Play", "VRCOSC/Media/Volume" },
                resolved,
                "Exact-name backup exclusions should compare after trimming, slash normalization, segment trimming, dedupe, and ordinal sorting.");
        }

        [Test]
        public void UnknownPresetId_FailsReadably()
        {
            bool ok = ASMLite.Editor.ASMLiteParameterBackupPresetResolver.TryResolvePresetExcludedNames(
                "sideways",
                new[] { "AvatarLimbScaling_Arms" },
                out var excluded,
                out string errorMessage);

            Assert.IsFalse(ok);
            CollectionAssert.AreEqual(Array.Empty<string>(), excluded);
            StringAssert.Contains("Unknown parameter backup preset ID 'sideways'", errorMessage);
            StringAssert.Contains(ASMLite.Editor.ASMLiteParameterBackupPresetResolver.NoneExcludedPresetId, errorMessage);
            StringAssert.Contains(ASMLite.Editor.ASMLiteParameterBackupPresetResolver.SingleArmsPresetId, errorMessage);
            StringAssert.Contains(ASMLite.Editor.ASMLiteParameterBackupPresetResolver.NestedMediaPresetId, errorMessage);
        }

        [Test]
        public void PresetMissingFromVisibleOptions_FailsReadably()
        {
            bool ok = ASMLite.Editor.ASMLiteParameterBackupPresetResolver.TryResolvePresetExcludedNames(
                ASMLite.Editor.ASMLiteParameterBackupPresetResolver.NestedMediaPresetId,
                new[] { "AvatarLimbScaling_Arms" },
                out var excluded,
                out string errorMessage);

            Assert.IsFalse(ok);
            CollectionAssert.AreEqual(Array.Empty<string>(), excluded);
            StringAssert.Contains("parameter backup preset ID 'nested-media'", errorMessage);
            StringAssert.Contains("VRCOSC/Media/Play", errorMessage);
            StringAssert.Contains("VRCOSC/Media/Volume", errorMessage);
        }

        [Test]
        public void SeededVrcFuryVisibleOptions_CanBeAssertedForPresetResolution()
        {
            var limbRoot = new GameObject("AvatarLimbScaling");
            limbRoot.transform.SetParent(_ctx.AvatarGo.transform, false);
            var arms = new GameObject("Arms");
            arms.transform.SetParent(limbRoot.transform, false);
            var limbVf = arms.AddComponent<VF.Model.VRCFury>();
            limbVf.content = new VF.Model.Feature.Toggle
            {
                useGlobalParam = true,
                globalParam = "AvatarLimbScaling_Arms",
                name = "Avatar Limb Scaling/Arms",
                menuPath = string.Empty,
            };

            var mediaRoot = new GameObject("Media");
            mediaRoot.transform.SetParent(_ctx.AvatarGo.transform, false);
            var mediaVf = mediaRoot.AddComponent<VF.Model.VRCFury>();
            var referencedParams = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            referencedParams.parameters = new[]
            {
                new VRCExpressionParameters.Parameter
                {
                    name = "VRCOSC/Media/Play",
                    valueType = VRCExpressionParameters.ValueType.Bool,
                    defaultValue = 0f,
                    saved = false,
                    networkSynced = false,
                },
                new VRCExpressionParameters.Parameter
                {
                    name = "VRCOSC/Media/Volume",
                    valueType = VRCExpressionParameters.ValueType.Float,
                    defaultValue = 0f,
                    saved = false,
                    networkSynced = false,
                },
            };

            try
            {
                mediaVf.content = new VF.Model.Feature.FullControllerLike
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

                CollectionAssert.Contains(visibleOptions, "AvatarLimbScaling_Arms",
                    "The seeded assigned VRCFury source parameter must be deterministically visible for backup-row automation.");
                CollectionAssert.Contains(visibleOptions, "VRCOSC/Media/Play",
                    "The seeded VRCFury referenced parameter asset must expose its nested Play parameter for backup-row automation.");
                CollectionAssert.Contains(visibleOptions, "VRCOSC/Media/Volume",
                    "The seeded VRCFury referenced parameter asset must expose its nested Volume parameter for backup-row automation.");

                CollectionAssert.AreEqual(
                    new[] { "AvatarLimbScaling_Arms" },
                    ASMLite.Editor.ASMLiteParameterBackupPresetResolver.ResolvePresetExcludedNames(
                        ASMLite.Editor.ASMLiteParameterBackupPresetResolver.SingleArmsPresetId,
                        visibleOptions));
                CollectionAssert.AreEqual(
                    new[] { "VRCOSC/Media/Play", "VRCOSC/Media/Volume" },
                    ASMLite.Editor.ASMLiteParameterBackupPresetResolver.ResolvePresetExcludedNames(
                        ASMLite.Editor.ASMLiteParameterBackupPresetResolver.NestedMediaPresetId,
                        visibleOptions));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(referencedParams);
            }
        }
    }
}
