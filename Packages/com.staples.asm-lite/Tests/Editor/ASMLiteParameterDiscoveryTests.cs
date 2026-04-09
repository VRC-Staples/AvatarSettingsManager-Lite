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
        private VRCExpressionParameters _exprParams;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("TestAvatar");
            _avDesc = _go.AddComponent<VRCAvatarDescriptor>();
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
    }
}
