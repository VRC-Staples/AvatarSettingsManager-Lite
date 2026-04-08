# CI Audit

**Date:** 2026-04-08
**Status:** Resolved

## Failure 1 — secret-scan.yml: Invalid gitleaks SHA

**Workflow:** `.github/workflows/secret-scan.yml`
**Run ID:** 24160418899
**Error:** `Unable to resolve action 'gitleaks/gitleaks-action@cb7149b9b57195b609c63e8518d2c37cfc2e1596'`

**Root cause:** The pinned SHA `cb7149b9b57195b609c63e8518d2c37cfc2e1596` for
`gitleaks/gitleaks-action` (tagged v2.3.9) does not exist in the upstream
repository. This caused the workflow step to fail at action resolution before
any scan work was performed.

**Fix applied:** Replaced with the verified SHA for v2.3.9:
`ff98106e4c7b2bc287b24eaf42907196329070c7`.

---

## Failure 2 — unity-test.yml: Dependabot lacks secret access

**Workflow:** `.github/workflows/unity-test.yml`
**Run ID:** 24109940965
**Error:** `UNITY_LICENSE is missing.`

**Root cause:** The workflow path filter includes `.github/workflows/unity-test.yml`.
When Dependabot opens a PR to bump `actions/checkout`, it modifies that file,
triggering the unity-test workflow. Dependabot PRs have no access to repository
secrets (`UNITY_LICENSE`, `UNITY_EMAIL`, `UNITY_PASSWORD`), causing an immediate
hard failure before any Unity work runs.

**Fix applied:** Added `if: github.actor != 'dependabot[bot]'` to the `test` job.
Dependabot-triggered runs now skip the job cleanly rather than failing. Unity tests
still run on all human-authored PRs and pushes.

---

## Actions/checkout Version Note

Dependabot PR #6 proposes bumping `actions/checkout` from v4.3.1 to v6.0.2
(SHA `de0fac2e4500dabe0009e67214ff5f5447ce83dd`). This is a legitimate version
bump — `actions/checkout@v6.0.2` is the current latest release. The PR can be
merged once the Dependabot skip condition above is in place on `dev`/`main`.
