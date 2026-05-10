using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine.SceneManagement;
using NUnit.Framework;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using ASMLite;
using ASMLite.Editor;

namespace ASMLite.Tests.Editor
{
    [TestFixture]
    [Category("Headless")]
    internal sealed class ASMLiteVisibleAutomationCommandLineTests
    {
        [TestCase(null, AsmLiteVisibleAutomationMode.Editor)]
        [TestCase("", AsmLiteVisibleAutomationMode.Editor)]
        [TestCase("ASMLiteVisibleEditorSmokeTests", AsmLiteVisibleAutomationMode.Editor)]
        [TestCase("editor", AsmLiteVisibleAutomationMode.Editor)]
        [TestCase("playmode", AsmLiteVisibleAutomationMode.PlayMode)]
        [TestCase("ASMLiteVisiblePlayModeSmoke", AsmLiteVisibleAutomationMode.PlayMode)]
        [TestCase("runtime-review", AsmLiteVisibleAutomationMode.PlayMode)]
        [TestCase("VisibleRuntimeHarness", AsmLiteVisibleAutomationMode.PlayMode)]
        [TestCase("launch-unity", AsmLiteVisibleAutomationMode.LaunchUnity)]
        [TestCase("LaunchUnity", AsmLiteVisibleAutomationMode.LaunchUnity)]
        public void ResolveModeSelector_MapsSelectors_ToExpectedVisibleAutomationMode(string selector, AsmLiteVisibleAutomationMode expectedMode)
        {
            Assert.AreEqual(expectedMode, ASMLiteVisibleAutomationCommandLine.ResolveModeSelector(selector));
        }

        [Test]
        public void ParseConfiguration_DefaultsToEditorMode_WhenModeArgIsOmitted()
        {
            var configuration = ASMLiteVisibleAutomationCommandLine.ParseConfiguration(new[]
            {
                "Unity",
                "-executeMethod",
                "ASMLite.Tests.Editor.ASMLiteVisibleAutomationCommandLine.RunFromCommandLine",
                "-asmliteVisibleAutomationResultsPath",
                "artifacts/visible-editor-smoke.xml",
                "-asmliteVisibleAutomationSelector",
                "ASMLiteVisibleEditorSmokeTests",
            });

            Assert.AreEqual((int)AsmLiteVisibleAutomationMode.Editor, configuration.mode);
            Assert.AreEqual(Path.GetFullPath("artifacts/visible-editor-smoke.xml"), configuration.resultsPath);
            Assert.AreEqual("ASMLiteVisibleEditorSmokeTests", configuration.selector);
            Assert.AreEqual((int)AsmLiteVisibleAutomationStage.OpeningWindow, configuration.stage);
            Assert.Greater(configuration.startedUtcTicks, 0L);
        }

        [Test]
        public void ParseConfiguration_DefaultsToClickMeSceneAndOct25DressAvatar_WhenTargetArgsAreOmitted()
        {
            var configuration = ASMLiteVisibleAutomationCommandLine.ParseConfiguration(new[]
            {
                "Unity",
                "-executeMethod",
                "ASMLite.Tests.Editor.ASMLiteVisibleAutomationCommandLine.RunFromCommandLine",
                "-asmliteVisibleAutomationResultsPath",
                "artifacts/visible-editor-smoke.xml",
            });

            Assert.AreEqual("Assets/Click ME.unity", configuration.scenePath);
            Assert.AreEqual("Oct25_Dress", configuration.avatarName);
        }

        [Test]
        public void ParseConfiguration_UsesExplicitStepDelayArgument_WhenProvided()
        {
            var configuration = ASMLiteVisibleAutomationCommandLine.ParseConfiguration(new[]
            {
                "Unity",
                "-executeMethod",
                "ASMLite.Tests.Editor.ASMLiteVisibleAutomationCommandLine.RunFromCommandLine",
                "-asmliteVisibleAutomationResultsPath",
                "artifacts/visible-editor-smoke.xml",
                "-asmliteVisibleAutomationStepDelaySeconds",
                "2.5",
            });

            Assert.IsTrue(configuration.hasStepDelaySeconds);
            Assert.AreEqual(2.5f, configuration.stepDelaySeconds, 0.0001f);
        }

        [Test]
        public void ParseConfiguration_UsesSelectorToInferInitialModeAndStage()
        {
            var configuration = ASMLiteVisibleAutomationCommandLine.ParseConfiguration(new[]
            {
                "Unity.exe",
                "-asmliteVisibleAutomationResultsPath",
                "C:/Temp/visible-playmode.xml",
                "-asmliteVisibleAutomationSelector",
                "playmode",
            });

            Assert.AreEqual(Path.GetFullPath("C:/Temp/visible-playmode.xml"), configuration.resultsPath);
            Assert.AreEqual("playmode", configuration.selector);
            Assert.AreEqual((int)AsmLiteVisibleAutomationMode.PlayMode, configuration.mode);
            Assert.AreEqual((int)AsmLiteVisibleAutomationStage.OpeningWindow, configuration.stage);
            Assert.Greater(configuration.startedUtcTicks, 0L);
        }

        [Test]
        public void ParseConfiguration_PrefersExplicitModeArgumentOverSelectorText()
        {
            var configuration = ASMLiteVisibleAutomationCommandLine.ParseConfiguration(new[]
            {
                "Unity.exe",
                "-asmliteVisibleAutomationResultsPath",
                "C:/Temp/visible-editor.xml",
                "-asmliteVisibleAutomationSelector",
                "playmode",
                "-asmliteVisibleAutomationMode",
                "editor",
            });

            Assert.AreEqual((int)AsmLiteVisibleAutomationMode.Editor, configuration.mode);
            Assert.AreEqual("playmode", configuration.selector);
        }

        [Test]
        public void BuildResultDocument_UsesLegacyVisibleSmokeFixtureName_ForEditorMode()
        {
            var configuration = new AsmLiteVisibleAutomationCommandLineConfiguration
            {
                resultsPath = "artifacts/visible-editor-smoke.xml",
                selector = "ASMLiteVisibleEditorSmokeTests",
                mode = (int)AsmLiteVisibleAutomationMode.Editor,
                startedUtcTicks = DateTime.UtcNow.AddSeconds(-5d).Ticks,
            };

            XmlDocument document = ASMLiteVisibleAutomationCommandLine.BuildResultDocument(
                configuration,
                "Passed",
                null,
                null,
                5.25d,
                new DateTimeOffset(configuration.startedUtcTicks, TimeSpan.Zero),
                new DateTimeOffset(configuration.startedUtcTicks, TimeSpan.Zero).AddSeconds(5.25d));

            XmlElement fixture = document.SelectSingleNode("/test-run/test-suite/test-suite") as XmlElement;
            XmlElement testCase = document.SelectSingleNode("/test-run/test-suite/test-suite/test-case") as XmlElement;

            Assert.IsNotNull(fixture);
            Assert.IsNotNull(testCase);
            Assert.AreEqual("ASMLiteVisibleEditorSmokeTests", fixture.GetAttribute("name"));
            Assert.AreEqual("VisibleWindow_AddPrefab_PrimaryActionExecutesThroughRenderedWindow", testCase.GetAttribute("name"));
            Assert.AreEqual("Passed", testCase.GetAttribute("result"));
            Assert.AreEqual(
                "ASMLite.Tests.Editor.ASMLiteVisibleEditorSmokeTests.VisibleWindow_AddPrefab_PrimaryActionExecutesThroughRenderedWindow",
                testCase.GetAttribute("fullname"));
            Assert.IsNull(document.SelectSingleNode("/test-run/test-suite/test-suite/test-case/failure"));
        }

        [Test]
        public void BuildResultDocument_UsesLaunchUnityCaseName_ForLaunchUnityRuns()
        {
            var configuration = new AsmLiteVisibleAutomationCommandLineConfiguration
            {
                resultsPath = "artifacts/visible-launch-unity.xml",
                selector = "launch-unity",
                mode = (int)AsmLiteVisibleAutomationMode.LaunchUnity,
                startedUtcTicks = DateTime.UtcNow.AddSeconds(-4d).Ticks,
            };

            XmlDocument document = ASMLiteVisibleAutomationCommandLine.BuildResultDocument(
                configuration,
                "Passed",
                null,
                null,
                4d,
                new DateTimeOffset(configuration.startedUtcTicks, TimeSpan.Zero),
                new DateTimeOffset(configuration.startedUtcTicks, TimeSpan.Zero).AddSeconds(4d));

            XmlElement fixture = document.SelectSingleNode("/test-run/test-suite/test-suite") as XmlElement;
            XmlElement testCase = document.SelectSingleNode("/test-run/test-suite/test-suite/test-case") as XmlElement;

            Assert.IsNotNull(fixture);
            Assert.IsNotNull(testCase);
            Assert.AreEqual("ASMLiteVisibleEditorSmokeTests", fixture.GetAttribute("name"));
            Assert.AreEqual("VisibleWindow_LaunchUnity_LoadsClickMe_SelectsOct25Dress_AndWaitsForAcceptance", testCase.GetAttribute("name"));
            Assert.AreEqual(
                "ASMLite.Tests.Editor.ASMLiteVisibleEditorSmokeTests.VisibleWindow_LaunchUnity_LoadsClickMe_SelectsOct25Dress_AndWaitsForAcceptance",
                testCase.GetAttribute("fullname"));
        }

        [Test]
        public void BuildResultDocument_UsesPlayModeFixtureName_ForPlayModeRuns()
        {
            var configuration = new AsmLiteVisibleAutomationCommandLineConfiguration
            {
                resultsPath = "artifacts/visible-editor-smoke.xml",
                selector = "playmode",
                mode = (int)AsmLiteVisibleAutomationMode.PlayMode,
                startedUtcTicks = DateTime.UtcNow.AddSeconds(-8d).Ticks,
            };

            XmlDocument document = ASMLiteVisibleAutomationCommandLine.BuildResultDocument(
                configuration,
                "Failed",
                "PlayMode never became active.",
                "stack-trace",
                8d,
                new DateTimeOffset(configuration.startedUtcTicks, TimeSpan.Zero),
                new DateTimeOffset(configuration.startedUtcTicks, TimeSpan.Zero).AddSeconds(8d));

            XmlElement fixture = document.SelectSingleNode("/test-run/test-suite/test-suite") as XmlElement;
            XmlElement testCase = document.SelectSingleNode("/test-run/test-suite/test-suite/test-case") as XmlElement;
            XmlElement failure = document.SelectSingleNode("/test-run/test-suite/test-suite/test-case/failure") as XmlElement;

            Assert.IsNotNull(fixture);
            Assert.IsNotNull(testCase);
            Assert.IsNotNull(failure);
            Assert.AreEqual("ASMLiteVisiblePlayModeAutomation", fixture.GetAttribute("name"));
            Assert.AreEqual(
                "ASMLite.Tests.Editor.ASMLiteVisiblePlayModeAutomation.VisibleWindow_AddPrefab_EntersPlayMode_AndWaitsForAcceptance",
                testCase.GetAttribute("fullname"));
            StringAssert.Contains("PlayMode never became active.", failure.InnerText);
        }
    }
}
