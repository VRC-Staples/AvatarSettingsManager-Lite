---
type: community
cohesion: 0.10
members: 20
---

# Toggle Broker Tests

**Cohesion:** 0.10 - loosely connected
**Members:** 20 nodes

## Members
- [[.SetUp()_8]] - code - Packages\com.staples.asm-lite\Tests\Editor\ASMLiteToggleBrokerTests.cs
- [[.TB01_SanitizePathToken_NormalizesMalformedInput()]] - code - Packages\com.staples.asm-lite\Tests\Editor\ASMLiteToggleBrokerTests.cs
- [[.TB02_BuildDeterministicGlobalName_DedupesCollisionsWithinAvatar()]] - code - Packages\com.staples.asm-lite\Tests\Editor\ASMLiteToggleBrokerTests.cs
- [[.TB03_Discovery_FailsClosedWhenReflectedTypeMissing()]] - code - Packages\com.staples.asm-lite\Tests\Editor\ASMLiteToggleBrokerTests.cs
- [[.TB04_Discovery_ScopesToAsmLiteAvatarAndSkipsNonToggleContent()]] - code - Packages\com.staples.asm-lite\Tests\Editor\ASMLiteToggleBrokerTests.cs
- [[.TB05_Discovery_HandlesBlankGlobalNameBoundaryAndSchemaDrift()]] - code - Packages\com.staples.asm-lite\Tests\Editor\ASMLiteToggleBrokerTests.cs
- [[.TB06_Mutation_UpdatesOnlySerializedToggleBoolAndStringFields()]] - code - Packages\com.staples.asm-lite\Tests\Editor\ASMLiteToggleBrokerTests.cs
- [[.TB07_Mutation_RejectsBlankAssignedGlobalName()]] - code - Packages\com.staples.asm-lite\Tests\Editor\ASMLiteToggleBrokerTests.cs
- [[.TB08_Callback_EnrollsOnlyAsmLiteScopedAvatars_AndRestoresRoundTrip()]] - code - Packages\com.staples.asm-lite\Tests\Editor\ASMLiteToggleBrokerTests.cs
- [[.TB09_Restore_ClearsMalformedPayloadAndFailsClosed()]] - code - Packages\com.staples.asm-lite\Tests\Editor\ASMLiteToggleBrokerTests.cs
- [[.TB10_Restore_HandlesMissingInstanceIdsAsUnresolvedAndCleansUp()]] - code - Packages\com.staples.asm-lite\Tests\Editor\ASMLiteToggleBrokerTests.cs
- [[.TB11_Enrollment_DuplicateInvocationPerSession_RestoresStaleThenReenrolls()]] - code - Packages\com.staples.asm-lite\Tests\Editor\ASMLiteToggleBrokerTests.cs
- [[.TearDown()_8]] - code - Packages\com.staples.asm-lite\Tests\Editor\ASMLiteToggleBrokerTests.cs
- [[ASMLite.Tests.Editor_14]] - code - Packages\com.staples.asm-lite\Tests\Editor\ASMLiteToggleBrokerTests.cs
- [[ASMLiteToggleBrokerTests]] - code - Packages\com.staples.asm-lite\Tests\Editor\ASMLiteToggleBrokerTests.cs
- [[ASMLiteToggleBrokerTests.cs]] - code - Packages\com.staples.asm-lite\Tests\Editor\ASMLiteToggleBrokerTests.cs
- [[BrokenToggle]] - code - Packages\com.staples.asm-lite\Tests\Editor\ASMLiteToggleBrokerTests.cs
- [[NotToggle]] - code - Packages\com.staples.asm-lite\Tests\Editor\ASMLiteToggleBrokerTests.cs
- [[Toggle]] - code - Packages\com.staples.asm-lite\Tests\Editor\ASMLiteToggleBrokerTests.cs
- [[VF.Model.Feature]] - code - Packages\com.staples.asm-lite\Tests\Editor\ASMLiteToggleBrokerTests.cs

## Live Query (requires Dataview plugin)

```dataview
TABLE source_file, type FROM #community/Toggle_Broker_Tests
SORT file.name ASC
```
