# Parallel Agent Worktrees

This repo now includes a local worktree orchestration script set under `Tools/ci/` for running multiple agent workflows against one frozen base commit without letting them collide in the same working tree.

Goals:
- one frozen base SHA per wave
- one integration branch per wave
- one worktree branch per agent
- tiny worker commits preserved during integration
- serial mergeback with optional verification after every cherry-pick

## Scripts

- `Tools/ci/worktree-wave-init.sh`
  - freezes a base SHA
  - creates `int/<wave>`
  - creates `wt/<wave>/<agent>` branches + worktree folders sequentially
  - saves wave metadata under `.git/worktree-waves/`
- `Tools/ci/worktree-wave-status.sh`
  - shows branch, dirty state, and pending commit counts per agent
- `Tools/ci/worktree-wave-integrate.sh`
  - cherry-picks each agent branch's commits onto the integration branch
  - keeps the worker's small atomic commits instead of collapsing them
  - can run a verification command after each cherry-pick
- `Tools/ci/worktree-wave-cleanup.sh`
  - removes local worktree directories after the wave finishes
  - can optionally delete worker branches, the integration branch, and registry files

## Default layout

The init script stores worktrees outside the repo by default:

```text
../.worktrees/<repo>/<wave>/<agent>
```

That keeps the main repo clean and avoids needing `.gitignore` changes.

Wave registry stays local-only inside `.git/`:

```text
.git/worktree-waves/<wave>.env
.git/worktree-waves/<wave>.agents.tsv
```

## Recommended workflow

### 1. Freeze a clean base and create worker worktrees

```bash
bash Tools/ci/worktree-wave-init.sh \
  --wave phase05-wave1 \
  --agent catalog:Packages/com.staples.asm-lite/Editor,Packages/com.staples.asm-lite/Tests/Editor \
  --agent runtime:Packages/com.staples.asm-lite/Runtime \
  --agent docs:README.md,Tools/ci/WORKTREE-AGENTS.md
```

Notes:
- worktrees are created sequentially on purpose so `git worktree add` does not contend on `.git/config.lock`
- the init script refuses to freeze a dirty repo unless `--allow-dirty` is passed
- agent path ownership is advisory metadata for humans; it does not hard-block file edits

### 2. Run one agent per worktree

Inside each worktree:

```bash
cd ../.worktrees/AvatarSettingsManager-Lite/phase05-wave1/catalog
git branch --show-current
hermes --worktree
```

Worker rules:
- commit tiny passing slices only
- do not push unless asked
- prefer one writer per file cluster
- avoid shared orchestrator-only files during worker execution

### 3. Check wave progress

```bash
bash Tools/ci/worktree-wave-status.sh --wave phase05-wave1
```

This shows:
- branch presence
- whether each worktree is clean or dirty
- commit count since base
- commit count still pending for integration

### 4. Integrate worker commits serially

Dry run first:

```bash
bash Tools/ci/worktree-wave-integrate.sh --wave phase05-wave1 --dry-run
```

Then integrate for real with verification after each cherry-pick:

```bash
bash Tools/ci/worktree-wave-integrate.sh \
  --wave phase05-wave1 \
  --verify-cmd 'bash Tools/ci/run-editmode-local.sh --test-filter ASMLiteSmokeOverlayHostTests'
```

Why cherry-pick instead of squash:
- preserves the worker's small iterative commits
- makes revert/blame easier
- keeps conflict resolution localized to integration time

### 5. Clean up local worktrees

```bash
bash Tools/ci/worktree-wave-cleanup.sh --wave phase05-wave1
```

Delete worker branches too if the wave is fully merged and no longer needed:

```bash
bash Tools/ci/worktree-wave-cleanup.sh \
  --wave phase05-wave1 \
  --drop-branches \
  --drop-integration \
  --drop-registry
```

## Same-file guidance

Safe:
- separate file clusters per agent
- different files in the same feature slice
- docs or tests parallelized away from the runtime file cluster they do not mutate

Riskier but workable:
- same file, different hunks

If two agents touch the same file:
1. keep both branches based on the same frozen base SHA
2. integrate one branch first
3. integrate the second branch onto the updated integration branch
4. resolve overlaps only in the integration step
5. if conflict resolution mixes two logical fixes, split that cleanup into separate atomic commits instead of one giant catch-all commit

## Suggested branch naming

- integration: `int/<wave>`
- worker: `wt/<wave>/<agent>`

Examples:
- `int/phase05-wave1`
- `wt/phase05-wave1/catalog`
- `wt/phase05-wave1/runtime`

## Suggested git settings

Enable rerere once per machine:

```bash
git config rerere.enabled true
```

That helps when similar conflict shapes repeat across multiple integrations.

## Limits

These scripts intentionally do not:
- auto-spawn agents
- auto-resolve merge conflicts
- force path ownership
- auto-push anything

They are orchestration helpers, not a replacement for review and judgment.
