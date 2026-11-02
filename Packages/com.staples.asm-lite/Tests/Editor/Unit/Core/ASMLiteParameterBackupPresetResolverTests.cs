using System;
using NUnit.Framework;

namespace ASMLite.Tests.Editor
{
    [TestFixture]
    [Category("Headless")]
    public class ASMLiteParameterBackupPresetResolverTests
    {
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
        public void ExactVisibleNames_NormalizeBeforeCompare_AndPreserveFirstSeenOrderForSnapshotStorage()
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
                new[] { "Fixture/ParamC", "Fixture/ParamA", "Fixture/ParamB" },
                resolved,
                "Exact-name backup exclusions should compare after trimming, slash normalization, segment trimming, de-dupe, and preserve first-seen order.");
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

    }
}
