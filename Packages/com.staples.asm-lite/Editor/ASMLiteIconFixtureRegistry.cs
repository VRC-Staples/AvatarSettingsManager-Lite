using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ASMLite.Editor
{
    internal enum ASMLiteIconFixtureKind
    {
        Root,
        Slot,
        Action,
    }

    internal readonly struct ASMLiteIconFixture
    {
        internal ASMLiteIconFixture(
            string id,
            string assetPath,
            ASMLiteIconFixtureKind kind,
            int slotNumber = 0,
            string actionName = "")
        {
            Id = id ?? string.Empty;
            AssetPath = assetPath ?? string.Empty;
            Kind = kind;
            SlotNumber = slotNumber;
            ActionName = actionName ?? string.Empty;
        }

        internal string Id { get; }
        internal string AssetPath { get; }
        internal ASMLiteIconFixtureKind Kind { get; }
        internal int SlotNumber { get; }
        internal string ActionName { get; }

        public override string ToString()
        {
            return string.IsNullOrWhiteSpace(AssetPath) ? Id : Id + " -> " + AssetPath;
        }
    }

    /// <summary>
    /// Stable IDs for the package-owned icon assets that visible automation and
    /// snapshot assertions may reference without hard-coding Unity asset paths.
    /// </summary>
    internal static class ASMLiteIconFixtureRegistry
    {
        internal const string RootIconId = "asm-lite-icon/root";
        internal const string Slot01IconId = "asm-lite-icon/slot-01";
        internal const string Slot02IconId = "asm-lite-icon/slot-02";
        internal const string Slot03IconId = "asm-lite-icon/slot-03";
        internal const string Slot04IconId = "asm-lite-icon/slot-04";
        internal const string Slot05IconId = "asm-lite-icon/slot-05";
        internal const string Slot06IconId = "asm-lite-icon/slot-06";
        internal const string Slot07IconId = "asm-lite-icon/slot-07";
        internal const string Slot08IconId = "asm-lite-icon/slot-08";
        internal const string SaveActionIconId = "asm-lite-icon/action-save";
        internal const string LoadActionIconId = "asm-lite-icon/action-load";
        internal const string ClearActionIconId = "asm-lite-icon/action-clear";

        private static readonly ASMLiteIconFixture[] s_fixtures = new[]
        {
            new ASMLiteIconFixture(RootIconId, ASMLiteAssetPaths.IconPresets, ASMLiteIconFixtureKind.Root),
            new ASMLiteIconFixture(Slot01IconId, ASMLiteAssetPaths.GearIconPaths[0], ASMLiteIconFixtureKind.Slot, 1),
            new ASMLiteIconFixture(Slot02IconId, ASMLiteAssetPaths.GearIconPaths[1], ASMLiteIconFixtureKind.Slot, 2),
            new ASMLiteIconFixture(Slot03IconId, ASMLiteAssetPaths.GearIconPaths[2], ASMLiteIconFixtureKind.Slot, 3),
            new ASMLiteIconFixture(Slot04IconId, ASMLiteAssetPaths.GearIconPaths[3], ASMLiteIconFixtureKind.Slot, 4),
            new ASMLiteIconFixture(Slot05IconId, ASMLiteAssetPaths.GearIconPaths[4], ASMLiteIconFixtureKind.Slot, 5),
            new ASMLiteIconFixture(Slot06IconId, ASMLiteAssetPaths.GearIconPaths[5], ASMLiteIconFixtureKind.Slot, 6),
            new ASMLiteIconFixture(Slot07IconId, ASMLiteAssetPaths.GearIconPaths[6], ASMLiteIconFixtureKind.Slot, 7),
            new ASMLiteIconFixture(Slot08IconId, ASMLiteAssetPaths.GearIconPaths[7], ASMLiteIconFixtureKind.Slot, 8),
            new ASMLiteIconFixture(SaveActionIconId, ASMLiteAssetPaths.IconSave, ASMLiteIconFixtureKind.Action, actionName: "save"),
            new ASMLiteIconFixture(LoadActionIconId, ASMLiteAssetPaths.IconLoad, ASMLiteIconFixtureKind.Action, actionName: "load"),
            new ASMLiteIconFixture(ClearActionIconId, ASMLiteAssetPaths.IconReset, ASMLiteIconFixtureKind.Action, actionName: "clear"),
        };

        private static readonly Dictionary<string, ASMLiteIconFixture> s_fixturesById = BuildFixturesById();
        private static readonly Dictionary<string, string> s_fixtureIdsByAssetPath = BuildFixtureIdsByAssetPath();

        internal static ASMLiteIconFixture[] GetAllFixtures()
        {
            return (ASMLiteIconFixture[])s_fixtures.Clone();
        }

        internal static string[] GetKnownIds()
        {
            var ids = new string[s_fixtures.Length];
            for (int index = 0; index < s_fixtures.Length; index++)
                ids[index] = s_fixtures[index].Id;
            return ids;
        }

        internal static bool TryResolveFixture(string fixtureId, out ASMLiteIconFixture fixture)
        {
            return s_fixturesById.TryGetValue(NormalizeFixtureId(fixtureId), out fixture);
        }

        internal static ASMLiteIconFixture ResolveFixture(string fixtureId)
        {
            string normalized = NormalizeFixtureId(fixtureId);
            if (string.IsNullOrEmpty(normalized))
                throw new ArgumentException("ASM-Lite icon fixture ID is required.", nameof(fixtureId));

            if (s_fixturesById.TryGetValue(normalized, out var fixture))
                return fixture;

            throw new ArgumentException(
                $"Unknown ASM-Lite icon fixture ID '{normalized}'. Known IDs: {JoinKnownIds()}.",
                nameof(fixtureId));
        }

        internal static bool TryResolveAssetPath(string fixtureId, out string assetPath)
        {
            if (TryResolveFixture(fixtureId, out var fixture))
            {
                assetPath = fixture.AssetPath;
                return true;
            }

            assetPath = string.Empty;
            return false;
        }

        internal static string ResolveAssetPath(string fixtureId)
        {
            return ResolveFixture(fixtureId).AssetPath;
        }

        internal static bool TryResolveTexture(string fixtureId, out Texture2D texture)
        {
            if (!TryResolveFixture(fixtureId, out var fixture))
            {
                texture = null;
                return false;
            }

            texture = AssetDatabase.LoadAssetAtPath<Texture2D>(fixture.AssetPath);
            return texture != null;
        }

        internal static Texture2D ResolveTexture(string fixtureId)
        {
            var fixture = ResolveFixture(fixtureId);
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(fixture.AssetPath);
            if (texture == null)
            {
                throw new FileNotFoundException(
                    $"ASM-Lite icon fixture '{fixture.Id}' points to missing Texture2D asset '{fixture.AssetPath}'.",
                    fixture.AssetPath);
            }

            return texture;
        }

        internal static bool TryGetFixtureIdForAssetPath(string assetPath, out string fixtureId)
        {
            return s_fixtureIdsByAssetPath.TryGetValue(NormalizeAssetPath(assetPath), out fixtureId);
        }

        internal static bool TryGetFixtureIdForTexture(Texture2D texture, out string fixtureId)
        {
            if (texture == null)
            {
                fixtureId = string.Empty;
                return false;
            }

            return TryGetFixtureIdForAssetPath(AssetDatabase.GetAssetPath(texture), out fixtureId);
        }

        internal static string GetFixtureIdOrEmpty(Texture2D texture)
        {
            return TryGetFixtureIdForTexture(texture, out string fixtureId) ? fixtureId : string.Empty;
        }

        internal static string[] GetFixtureIdsOrEmpty(Texture2D[] textures)
        {
            textures = textures ?? Array.Empty<Texture2D>();
            var fixtureIds = new string[textures.Length];
            for (int index = 0; index < textures.Length; index++)
                fixtureIds[index] = GetFixtureIdOrEmpty(textures[index]);
            return fixtureIds;
        }

        internal static bool TryGetSlotIconId(int slotNumber, out string fixtureId)
        {
            fixtureId = slotNumber >= 1 && slotNumber <= 8
                ? $"asm-lite-icon/slot-{slotNumber:D2}"
                : string.Empty;
            return !string.IsNullOrEmpty(fixtureId) && s_fixturesById.ContainsKey(fixtureId);
        }

        internal static string ResolveSlotIconId(int slotNumber)
        {
            if (TryGetSlotIconId(slotNumber, out string fixtureId))
                return fixtureId;

            throw new ArgumentOutOfRangeException(
                nameof(slotNumber),
                slotNumber,
                "ASM-Lite icon fixture slot number must be between 1 and 8.");
        }

        internal static Texture2D ResolveSlotIcon(int slotNumber)
        {
            return ResolveTexture(ResolveSlotIconId(slotNumber));
        }

        internal static Texture2D ResolveActionIcon(string actionName)
        {
            return ResolveTexture(ResolveActionIconId(actionName));
        }

        internal static string ResolveActionIconId(string actionName)
        {
            switch (NormalizeFixtureId(actionName))
            {
                case "save":
                    return SaveActionIconId;
                case "load":
                    return LoadActionIconId;
                case "clear":
                    return ClearActionIconId;
                default:
                    throw new ArgumentException(
                        $"Unknown ASM-Lite action icon fixture action '{actionName}'. Expected one of: save, load, clear.",
                        nameof(actionName));
            }
        }

        private static Dictionary<string, ASMLiteIconFixture> BuildFixturesById()
        {
            var fixturesById = new Dictionary<string, ASMLiteIconFixture>(StringComparer.Ordinal);
            foreach (var fixture in s_fixtures)
                fixturesById.Add(fixture.Id, fixture);
            return fixturesById;
        }

        private static Dictionary<string, string> BuildFixtureIdsByAssetPath()
        {
            var fixtureIdsByAssetPath = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var fixture in s_fixtures)
                fixtureIdsByAssetPath.Add(NormalizeAssetPath(fixture.AssetPath), fixture.Id);
            return fixtureIdsByAssetPath;
        }

        private static string NormalizeFixtureId(string fixtureId)
        {
            return string.IsNullOrWhiteSpace(fixtureId) ? string.Empty : fixtureId.Trim();
        }

        private static string NormalizeAssetPath(string assetPath)
        {
            return string.IsNullOrWhiteSpace(assetPath) ? string.Empty : assetPath.Trim().Replace('\\', '/');
        }

        private static string JoinKnownIds()
        {
            return string.Join(", ", GetKnownIds());
        }
    }
}
