# Graph Report - Packages  (2026-04-09)

## Corpus Check
- Corpus is ~46,202 words - fits in a single context window. You may not need a graph.

## Summary
- 385 nodes · 646 edges · 26 communities detected
- Extraction: 94% EXTRACTED · 6% INFERRED · 0% AMBIGUOUS · INFERRED: 41 edges (avg confidence: 0.92)
- Token cost: 0 input · 0 output

## God Nodes (most connected - your core abstractions)
1. `ASMLiteToggleNameBroker` - 29 edges
2. `ASMLiteFXControllerTests` - 23 edges
3. `ASMLiteWindow` - 22 edges
4. `ASMLiteCleanupTests` - 22 edges
5. `ASMLiteBuilder` - 21 edges
6. `ASMLiteBuilderTests` - 21 edges
7. `ASMLiteBuildIntegrationTests` - 21 edges
8. `ASMLiteAssetPathsTests` - 18 edges
9. `ASMLiteMenuTests` - 18 edges
10. `ASMLiteToggleBrokerTests` - 14 edges

## Surprising Connections (you probably didn't know these)
- `Back Arrow Icon` --conceptually_related_to--> `ASM-Lite Icon System`  [INFERRED]
  Packages/com.staples.asm-lite/Icons/BackArrow.png → Packages/com.staples.asm-lite/Icons/Save.png
- `Save Action Icon` --semantically_similar_to--> `Load Action Icon`  [INFERRED] [semantically similar]
  Packages/com.staples.asm-lite/Icons/Save.png → Packages/com.staples.asm-lite/Icons/Load.png
- `Load Action Icon` --semantically_similar_to--> `Reset Action Icon`  [INFERRED] [semantically similar]
  Packages/com.staples.asm-lite/Icons/Load.png → Packages/com.staples.asm-lite/Icons/Reset.png
- `Save Action Icon` --semantically_similar_to--> `Reset Action Icon`  [INFERRED] [semantically similar]
  Packages/com.staples.asm-lite/Icons/Save.png → Packages/com.staples.asm-lite/Icons/Reset.png
- `Blue Gear Icon` --semantically_similar_to--> `Cyan Gear Icon`  [INFERRED] [semantically similar]
  Packages/com.staples.asm-lite/Icons/Gears/BlueGear.png → Packages/com.staples.asm-lite/Icons/Gears/CyanGear.png

## Hyperedges (group relationships)
- **ASM-Lite Core Action Icons** — save_icon, load_icon, reset_icon, backarrow_icon [INFERRED 0.88]
- **All ASM-Lite UI Icons** — save_icon, load_icon, reset_icon, backarrow_icon, slidersicon_icon [INFERRED 0.85]
- **ASM-Lite Gear Icon Color Set** — bluegear_icon, cyangear_icon, greengear_icon, orangegear_icon, pinkgear_icon, purplegear_icon, redgear_icon, yellowgear_icon [INFERRED 0.98]

## Communities

### Community 0 - "Toggle Name Broker"
Cohesion: 0.11
Nodes (5): ASMLite.Editor, ASMLiteToggleBuildRequestedCallback, ASMLiteToggleNameBroker, RestoreEntry, RestorePayload

### Community 1 - "FX Controller Tests"
Cohesion: 0.23
Nodes (2): ASMLite.Tests.Editor, ASMLiteFXControllerTests

### Community 2 - "Cleanup Tests"
Cohesion: 0.19
Nodes (2): ASMLite.Tests.Editor, ASMLiteCleanupTests

### Community 3 - "Core Build Logic"
Cohesion: 0.15
Nodes (2): ASMLite.Editor, ASMLiteBuilder

### Community 4 - "Editor Window"
Cohesion: 0.19
Nodes (2): ASMLite.Editor, ASMLiteWindow

### Community 5 - "Builder Unit Tests"
Cohesion: 0.09
Nodes (2): ASMLite.Tests.Editor, ASMLiteBuilderTests

### Community 6 - "Build Integration Tests"
Cohesion: 0.24
Nodes (2): ASMLite.Tests.Editor, ASMLiteBuildIntegrationTests

### Community 7 - "Runtime Component"
Cohesion: 0.11
Nodes (10): ASMLite, ASMLiteComponent, GetBuildMethod(), ASMLite.Tests.Editor, ASMLiteMigrationTests, VF.Model, VRCFury, IEditorOnly (+2 more)

### Community 8 - "Asset Path Tests"
Cohesion: 0.1
Nodes (2): ASMLite.Tests.Editor, ASMLiteAssetPathsTests

### Community 9 - "Menu Tests"
Cohesion: 0.23
Nodes (2): ASMLite.Tests.Editor, ASMLiteMenuTests

### Community 10 - "Toggle Broker Tests"
Cohesion: 0.1
Nodes (6): ASMLite.Tests.Editor, ASMLiteToggleBrokerTests, BrokenToggle, NotToggle, Toggle, VF.Model.Feature

### Community 11 - "Component Tests"
Cohesion: 0.13
Nodes (2): ASMLite.Tests.Editor, ASMLiteComponentTests

### Community 12 - "Expression Params Tests"
Cohesion: 0.27
Nodes (2): ASMLite.Tests.Editor, ASMLiteExpressionParamsTests

### Community 13 - "Prefab Creator"
Cohesion: 0.31
Nodes (2): ASMLite.Editor, ASMLitePrefabCreator

### Community 14 - "VRCFury Pipeline Tests"
Cohesion: 0.29
Nodes (2): ASMLite.Tests.Editor, ASMLiteVRCFuryPipelineTests

### Community 15 - "Icon Tests"
Cohesion: 0.35
Nodes (2): ASMLite.Tests.Editor, ASMLiteIconTests

### Community 16 - "Parameter Discovery Tests"
Cohesion: 0.18
Nodes (2): ASMLite.Tests.Editor, ASMLiteParameterDiscoveryTests

### Community 17 - "Test Fixtures"
Cohesion: 0.2
Nodes (3): ASMLite.Tests.Editor, AsmLiteTestContext, ASMLiteTestFixtures

### Community 18 - "Icon System Assets"
Cohesion: 0.42
Nodes (9): ASM-Lite Action Icons Set, ASM-Lite Icon System, ASM-Lite VPM Package, Back Arrow Icon, ASM-Lite Banner Image, Load Action Icon, Reset Action Icon, Save Action Icon (+1 more)

### Community 19 - "Synced Param Inspector"
Cohesion: 0.32
Nodes (3): ASMLite.Editor, ASMLiteSyncedParamInspectorWindow, EditorWindow

### Community 20 - "Gear Icon Color Set"
Cohesion: 1.0
Nodes (8): Blue Gear Icon, Cyan Gear Icon, Green Gear Icon, Orange Gear Icon, Pink Gear Icon, Purple Gear Icon, Red Gear Icon, Yellow Gear Icon

### Community 21 - "Prefab Wiring Tests"
Cohesion: 0.4
Nodes (2): ASMLite.Tests.Editor, ASMLitePrefabWiringTests

### Community 22 - "Component Editor Inspector"
Cohesion: 0.5
Nodes (2): ASMLite.Editor, ASMLiteComponentEditor

### Community 23 - "Prefab Contract Tests"
Cohesion: 0.5
Nodes (2): ASMLite.Tests.Editor, ASMLitePrefabContractTests

### Community 24 - "Asset Paths Constants"
Cohesion: 0.67
Nodes (2): ASMLite.Editor, ASMLiteAssetPaths

### Community 25 - "Assembly Info"
Cohesion: 1.0
Nodes (0): 

## Knowledge Gaps
- **33 isolated node(s):** `ASMLite`, `ASMLite.Editor`, `ASMLiteAssetPaths`, `ASMLite.Editor`, `ASMLite.Editor` (+28 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **Thin community `Assembly Info`** (1 nodes): `AssemblyInfo.cs`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `ASMLiteWindow` connect `Editor Window` to `Synced Param Inspector`?**
  _High betweenness centrality (0.005) - this node is a cross-community bridge._
- **What connects `ASMLite`, `ASMLite.Editor`, `ASMLiteAssetPaths` to the rest of the system?**
  _33 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Toggle Name Broker` be split into smaller, more focused modules?**
  _Cohesion score 0.11 - nodes in this community are weakly interconnected._
- **Should `Builder Unit Tests` be split into smaller, more focused modules?**
  _Cohesion score 0.09 - nodes in this community are weakly interconnected._
- **Should `Runtime Component` be split into smaller, more focused modules?**
  _Cohesion score 0.11 - nodes in this community are weakly interconnected._
- **Should `Asset Path Tests` be split into smaller, more focused modules?**
  _Cohesion score 0.1 - nodes in this community are weakly interconnected._
- **Should `Toggle Broker Tests` be split into smaller, more focused modules?**
  _Cohesion score 0.1 - nodes in this community are weakly interconnected._