using NUnit.Framework;
using ASMLite;
using ASMLite.Editor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using System.Collections.Generic;

namespace ASMLite.Tests.Editor
{
    /// <summary>
    /// A01-A04: GetFinalAvatarParams parameter discovery tests.
    /// Pure in-memory: no AssetDatabase, no integration category.
    /// </summary>
    [TestFixture]
    public class ASMLiteParameterDiscoveryTests
    {
        private GameObject _go;
        private VRCAvatarDescriptor _avDesc;
        private ASMLiteComponent _component;
        private VRCExpressionParameters _exprParams;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("TestAvatar");
            _avDesc = _go.AddComponent<VRCAvatarDescriptor>();
            _component = _go.AddComponent<ASMLiteComponent>();
            _exprParams = null;
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null)
                Object.DestroyImmediate(_go);
            if (_exprParams != null)
                Object.DestroyImmediate(_exprParams);
        }

        // A01: ASMLite_-prefixed params are filtered out; non-prefixed params are returned.
        [Test]
        public void A01_GetFinalAvatarParams_FiltersASMLitePrefixedParams()
        {
            _exprParams = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            _exprParams.parameters = new[]
            {
                new VRCExpressionParameters.Parameter { name = "ASMLite_Save", valueType = VRCExpressionParameters.ValueType.Int },
                new VRCExpressionParameters.Parameter { name = "ASMLite_Ctrl", valueType = VRCExpressionParameters.ValueType.Int },
                new VRCExpressionParameters.Parameter { name = "MyParam",      valueType = VRCExpressionParameters.ValueType.Float },
                new VRCExpressionParameters.Parameter { name = "AnotherParam", valueType = VRCExpressionParameters.ValueType.Bool },
            };
            _avDesc.expressionParameters = _exprParams;

            List<VRCExpressionParameters.Parameter> result = ASMLiteBuilder.GetFinalAvatarParams(_avDesc);

            Assert.AreEqual(2, result.Count, "Should return exactly the 2 non-ASMLite_ params");
            Assert.AreEqual("MyParam",      result[0].name);
            Assert.AreEqual("AnotherParam", result[1].name);
        }

        // A02: Non-ASMLite_ params are returned in their original order.
        [Test]
        public void A02_GetFinalAvatarParams_PreservesNonASMLiteParamsInOrder()
        {
            _exprParams = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            _exprParams.parameters = new[]
            {
                new VRCExpressionParameters.Parameter { name = "Alpha",   valueType = VRCExpressionParameters.ValueType.Float },
                new VRCExpressionParameters.Parameter { name = "Beta",    valueType = VRCExpressionParameters.ValueType.Int },
                new VRCExpressionParameters.Parameter { name = "Gamma",   valueType = VRCExpressionParameters.ValueType.Bool },
            };
            _avDesc.expressionParameters = _exprParams;

            List<VRCExpressionParameters.Parameter> result = ASMLiteBuilder.GetFinalAvatarParams(_avDesc);

            Assert.AreEqual(3, result.Count);
            Assert.AreEqual("Alpha", result[0].name);
            Assert.AreEqual("Beta",  result[1].name);
            Assert.AreEqual("Gamma", result[2].name);
        }

        // A03: Null-name and empty-name entries are skipped; only valid entries are returned.
        [Test]
        public void A03_GetFinalAvatarParams_SkipsNullAndEmptyNameEntries()
        {
            _exprParams = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            _exprParams.parameters = new[]
            {
                new VRCExpressionParameters.Parameter { name = null,       valueType = VRCExpressionParameters.ValueType.Float },
                new VRCExpressionParameters.Parameter { name = "",         valueType = VRCExpressionParameters.ValueType.Int },
                new VRCExpressionParameters.Parameter { name = "ValidOne", valueType = VRCExpressionParameters.ValueType.Bool },
            };
            _avDesc.expressionParameters = _exprParams;

            List<VRCExpressionParameters.Parameter> result = ASMLiteBuilder.GetFinalAvatarParams(_avDesc);

            Assert.AreEqual(1, result.Count, "Should return only the valid entry");
            Assert.AreEqual("ValidOne", result[0].name);
        }

        // A04: When expressionParameters is null, result is an empty list (not null, not exception).
        [Test]
        public void A04_GetFinalAvatarParams_ReturnsEmptyList_WhenExpressionParametersIsNull()
        {
            _avDesc.expressionParameters = null;

            List<VRCExpressionParameters.Parameter> result = null;
            Assert.DoesNotThrow(() => result = ASMLiteBuilder.GetFinalAvatarParams(_avDesc),
                "GetFinalAvatarParams must not throw when expressionParameters is null");
            Assert.IsNotNull(result, "Result must not be null");
            Assert.AreEqual(0, result.Count, "Result must be an empty list");
        }

        // A05: VF-scoped names are consumed as opaque identifiers (no renaming, no prefix stripping).
        [Test]
        public void A05_GetFinalAvatarParams_PreservesVFOpaqueCanonicalNamesWithoutRenaming()
        {
            _exprParams = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            _exprParams.parameters = new[]
            {
                new VRCExpressionParameters.Parameter { name = "VF135_Clothing/Rezz", valueType = VRCExpressionParameters.ValueType.Float },
                new VRCExpressionParameters.Parameter { name = "VF135_Clothing/Hood", valueType = VRCExpressionParameters.ValueType.Bool },
                new VRCExpressionParameters.Parameter { name = "ASMLite_Internal", valueType = VRCExpressionParameters.ValueType.Int },
            };
            _avDesc.expressionParameters = _exprParams;

            List<VRCExpressionParameters.Parameter> result = ASMLiteBuilder.GetFinalAvatarParams(_avDesc);

            Assert.AreEqual(2, result.Count,
                "A05 regression guard: VF-prefixed names must not be silently dropped; only ASMLite_ names should be filtered.");
            Assert.AreEqual("VF135_Clothing/Rezz", result[0].name,
                "A05 regression guard: VF names must remain opaque/canonical and must not be renamed.");
            Assert.AreEqual("VF135_Clothing/Hood", result[1].name,
                "A05 regression guard: VF names must remain opaque/canonical and must not be renamed.");
        }

        // A06: broker-enrolled ASM_VF names are opaque user params and must survive discovery unchanged.
        [Test]
        public void A06_GetFinalAvatarParams_PreservesBrokerAssignedASMVFNames()
        {
            _exprParams = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            _exprParams.parameters = new[]
            {
                new VRCExpressionParameters.Parameter { name = "ASM_VF_Outfit_Hood__Avatar_ASM_Lite", valueType = VRCExpressionParameters.ValueType.Bool },
                new VRCExpressionParameters.Parameter { name = "ASM_VF_Outfit_Hat__Avatar_ASM_Lite", valueType = VRCExpressionParameters.ValueType.Float },
                new VRCExpressionParameters.Parameter { name = "ASMLite_Ctrl", valueType = VRCExpressionParameters.ValueType.Int },
            };
            _avDesc.expressionParameters = _exprParams;

            List<VRCExpressionParameters.Parameter> result = ASMLiteBuilder.GetFinalAvatarParams(_avDesc);

            Assert.AreEqual(2, result.Count,
                "A06 regression guard: ASM_VF broker names are source params and must not be filtered with ASMLite_ internals.");
            Assert.AreEqual("ASM_VF_Outfit_Hood__Avatar_ASM_Lite", result[0].name,
                "A06 regression guard: broker-assigned deterministic name must remain unchanged.");
            Assert.AreEqual("ASM_VF_Outfit_Hat__Avatar_ASM_Lite", result[1].name,
                "A06 regression guard: broker-assigned deterministic name must remain unchanged.");
        }

        // A07: deterministic live params remain discoverable while legacy backup aliases stay excluded from discovery.
        [Test]
        public void A07_GetFinalAvatarParams_UsesLiveDeterministicSources_NotPreservedLegacyBackupAliases()
        {
            _exprParams = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            _exprParams.parameters = new[]
            {
                new VRCExpressionParameters.Parameter { name = "ASM_VF_Menu_Hat__Avatar_ASM_Lite", valueType = VRCExpressionParameters.ValueType.Bool },
                new VRCExpressionParameters.Parameter { name = "FacialBlend", valueType = VRCExpressionParameters.ValueType.Float },
                new VRCExpressionParameters.Parameter { name = "ASMLite_Bak_S1_VF777_Menu/Hat", valueType = VRCExpressionParameters.ValueType.Bool },
                new VRCExpressionParameters.Parameter { name = "ASMLite_Def_ASM_VF_Menu_Hat__Avatar_ASM_Lite", valueType = VRCExpressionParameters.ValueType.Bool },
            };
            _avDesc.expressionParameters = _exprParams;

            List<VRCExpressionParameters.Parameter> result = ASMLiteBuilder.GetFinalAvatarParams(_avDesc);

            Assert.AreEqual(2, result.Count,
                "A07 regression guard: only live non-ASMLite source params should survive discovery when legacy aliases are present.");
            Assert.AreEqual("ASM_VF_Menu_Hat__Avatar_ASM_Lite", result[0].name,
                "A07 regression guard: deterministic source param must remain discoverable as the live source of truth.");
            Assert.AreEqual("FacialBlend", result[1].name,
                "A07 regression guard: non-VF sibling source params must remain discoverable alongside deterministic VF sources.");
            Assert.IsFalse(result.Exists(p => p != null && p.name == "ASMLite_Bak_S1_VF777_Menu/Hat"),
                "A07 regression guard: preserved legacy backup aliases must not be rediscovered as live avatar source params.");
        }

        // A08: exclusions are opt-in; disabled toggle must leave discovery baseline-equivalent even with stale serialized names.
        [Test]
        public void A08_GetFinalAvatarParams_ExclusionsDisabled_IgnoresSerializedNames()
        {
            _exprParams = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            _exprParams.parameters = new[]
            {
                new VRCExpressionParameters.Parameter { name = "Hat", valueType = VRCExpressionParameters.ValueType.Bool },
                new VRCExpressionParameters.Parameter { name = "Jacket", valueType = VRCExpressionParameters.ValueType.Bool },
            };
            _avDesc.expressionParameters = _exprParams;

            _component.useParameterExclusions = false;
            _component.excludedParameterNames = new[] { "Hat", "GhostParam" };

            var report = ASMLiteBuilder.ResolveParameterExclusions(_component, matchedCount: 0);
            List<VRCExpressionParameters.Parameter> result = ASMLiteBuilder.GetFinalAvatarParams(_avDesc, report.CanonicalExcludedNames, out int matchedCount);

            Assert.IsFalse(report.Enabled, "A08 regression guard: exclusion toggle must gate all filtering behavior.");
            Assert.AreEqual(0, report.RequestedCount, "A08 regression guard: disabled exclusions should not build a canonical exclusion set.");
            Assert.AreEqual(0, matchedCount, "A08 regression guard: disabled exclusions should never match discovered params.");
            Assert.AreEqual(2, result.Count, "A08 regression guard: disabled exclusions must preserve baseline discovery output.");
            Assert.AreEqual("Hat", result[0].name);
            Assert.AreEqual("Jacket", result[1].name);
        }

        // A09: enabled exclusions must use exact ordinal matching (case-sensitive, opaque identifiers).
        [Test]
        public void A09_GetFinalAvatarParams_ExclusionsEnabled_UsesExactOrdinalNameMatching()
        {
            _exprParams = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            _exprParams.parameters = new[]
            {
                new VRCExpressionParameters.Parameter { name = "VF135_Clothing/Rezz", valueType = VRCExpressionParameters.ValueType.Float },
                new VRCExpressionParameters.Parameter { name = "VF135_Clothing/Hood", valueType = VRCExpressionParameters.ValueType.Bool },
            };
            _avDesc.expressionParameters = _exprParams;

            _component.useParameterExclusions = true;
            _component.excludedParameterNames = new[]
            {
                "VF135_Clothing/Rezz",
                "vf135_clothing/rezz",
            };

            var report = ASMLiteBuilder.ResolveParameterExclusions(_component, matchedCount: 0);
            List<VRCExpressionParameters.Parameter> result = ASMLiteBuilder.GetFinalAvatarParams(_avDesc, report.CanonicalExcludedNames, out int matchedCount);
            var finalizedReport = ASMLiteBuilder.ResolveParameterExclusions(_component, matchedCount);

            Assert.IsTrue(report.Enabled);
            Assert.AreEqual(2, report.RequestedCount, "A09 regression guard: canonical exclusions should retain distinct case-sensitive names.");
            Assert.AreEqual(1, matchedCount, "A09 regression guard: only exact case-sensitive exclusion names should match.");
            Assert.AreEqual(1, finalizedReport.IgnoredStaleCount, "A09 regression guard: non-matching case variants should be counted as stale exclusions.");
            Assert.AreEqual(1, result.Count, "A09 regression guard: only the exact matching live parameter should be filtered.");
            Assert.AreEqual("VF135_Clothing/Hood", result[0].name);
        }

        // A10: sanitization drops null/empty/whitespace and duplicate exclusions while retaining exact canonical names.
        [Test]
        public void A10_ResolveParameterExclusions_SanitizesMalformedAndDuplicateNames()
        {
            _exprParams = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            _exprParams.parameters = new[]
            {
                new VRCExpressionParameters.Parameter { name = "Hat", valueType = VRCExpressionParameters.ValueType.Bool },
                new VRCExpressionParameters.Parameter { name = "Jacket", valueType = VRCExpressionParameters.ValueType.Bool },
            };
            _avDesc.expressionParameters = _exprParams;

            _component.useParameterExclusions = true;
            _component.excludedParameterNames = new[]
            {
                null,
                string.Empty,
                " ",
                "Hat",
                "Hat",
                " Hat ",
                "GhostParam",
            };

            var report = ASMLiteBuilder.ResolveParameterExclusions(_component, matchedCount: 0);
            List<VRCExpressionParameters.Parameter> result = ASMLiteBuilder.GetFinalAvatarParams(_avDesc, report.CanonicalExcludedNames, out int matchedCount);
            var finalizedReport = ASMLiteBuilder.ResolveParameterExclusions(_component, matchedCount);

            Assert.AreEqual(7, report.RawRequestedCount);
            Assert.AreEqual(2, report.RequestedCount, "A10 regression guard: canonical exclusions should contain only deduplicated non-empty names.");
            Assert.AreEqual(5, report.IgnoredSanitizationCount, "A10 regression guard: null/empty/duplicate names should be attributed to sanitization.");
            Assert.AreEqual(1, matchedCount, "A10 regression guard: only live discovered names should match exclusions.");
            Assert.AreEqual(1, finalizedReport.IgnoredStaleCount, "A10 regression guard: exclusions absent from discovery should be counted as stale.");
            Assert.AreEqual(1, result.Count, "A10 regression guard: exact matched exclusions must be removed from discovery output.");
            Assert.AreEqual("Jacket", result[0].name);
        }

        // A11: when every discovered parameter is excluded, discovery returns an empty list (not null/error).
        [Test]
        public void A11_GetFinalAvatarParams_ExclusionsEnabled_AllParamsExcluded_ReturnsEmptyList()
        {
            _exprParams = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            _exprParams.parameters = new[]
            {
                new VRCExpressionParameters.Parameter { name = "ParamA", valueType = VRCExpressionParameters.ValueType.Int },
                new VRCExpressionParameters.Parameter { name = "ParamB", valueType = VRCExpressionParameters.ValueType.Float },
            };
            _avDesc.expressionParameters = _exprParams;

            _component.useParameterExclusions = true;
            _component.excludedParameterNames = new[] { "ParamA", "ParamB" };

            var report = ASMLiteBuilder.ResolveParameterExclusions(_component, matchedCount: 0);
            List<VRCExpressionParameters.Parameter> result = ASMLiteBuilder.GetFinalAvatarParams(_avDesc, report.CanonicalExcludedNames, out int matchedCount);
            var finalizedReport = ASMLiteBuilder.ResolveParameterExclusions(_component, matchedCount);

            Assert.AreEqual(2, matchedCount);
            Assert.AreEqual(0, finalizedReport.IgnoredStaleCount);
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Count, "A11 regression guard: all-excluded discovery should produce an empty list without throwing.");
        }

        // A12: zero discovered live params keeps enabled exclusions non-fatal and classifies all canonical names as stale.
        [Test]
        public void A12_GetFinalAvatarParams_ExclusionsEnabled_NoDiscoveredParams_ReportsStaleNames()
        {
            _exprParams = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            _exprParams.parameters = new VRCExpressionParameters.Parameter[0];
            _avDesc.expressionParameters = _exprParams;

            _component.useParameterExclusions = true;
            _component.excludedParameterNames = new[] { "GhostA", "GhostB" };

            var report = ASMLiteBuilder.ResolveParameterExclusions(_component, matchedCount: 0);
            List<VRCExpressionParameters.Parameter> result = ASMLiteBuilder.GetFinalAvatarParams(_avDesc, report.CanonicalExcludedNames, out int matchedCount);
            var finalizedReport = ASMLiteBuilder.ResolveParameterExclusions(_component, matchedCount);

            Assert.IsTrue(finalizedReport.Enabled);
            Assert.AreEqual(2, finalizedReport.RequestedCount);
            Assert.AreEqual(0, matchedCount);
            Assert.AreEqual(2, finalizedReport.IgnoredStaleCount, "A12 regression guard: unresolved exclusions should be surfaced as stale when no live params exist.");
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Count);
        }
    }
}
