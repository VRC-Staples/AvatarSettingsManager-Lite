# GitHub issue tracker

ASM-Lite issues and specs live in GitHub Issues in `VRC-Staples/AvatarSettingsManager-Lite`.
Use the `gh` CLI from this repository.
For cross-project work, follow the [workspace issue-tracker guidance](../../../AGENTS.md#issue-tracker).

## Issue operations

Use these commands for the corresponding skill operations.
Replace placeholders with the issue number, title, body, or label.
For multiline bodies, use a heredoc or `--body-file`.

- Publish: `gh issue create --title "..." --body "..."`
- Fetch: `gh issue view <number> --json number,title,body,labels,comments`
- List: `gh issue list --state open --limit 100 --json number,title,body,labels,assignees`
- Comment: `gh issue comment <number> --body "..."`
- Add a label: `gh issue edit <number> --add-label "..."`
- Remove a label: `gh issue edit <number> --remove-label "..."`
- Claim: `gh issue edit <number> --add-assignee @me`
- Resolve: `gh issue close <number> --comment "..."`

If the list reaches its limit, retrieve the remaining issues before treating the results as complete.
Use the label mapping in `triage-labels.md`.
Existing authorization requirements in `../../AGENTS.md` still apply.

## Pull requests

PRs as a request surface: no.

GitHub shares issue and pull request numbers.
If the resource type is unclear, try `gh pr view <number>`, then `gh issue view <number>`.

## Work maps and dependencies

A work map is an issue that groups related tickets.
Use `wayfinder:map` for the map.
Use `wayfinder:<type>` for children, where the type is `research`, `prototype`, `grilling`, or `task`.

Link children through GitHub sub-issues when available.
Otherwise, add children to a task list in the map.
Put `Part of #<parent>` at the top of each child body.

Use GitHub issue dependencies for blockers:

`gh api --method POST repos/VRC-Staples/AvatarSettingsManager-Lite/issues/<child>/dependencies/blocked_by -F issue_id=<blocker-db-id>`

Retrieve the blocker database ID with:

`gh api repos/VRC-Staples/AvatarSettingsManager-Lite/issues/<blocker-number> --jq .id`

The database ID differs from the issue number and `node_id`.
If dependencies are unavailable, put `Blocked by: #<number>` at the top of the child body.

Select the first open, unassigned child in map order whose blockers are all closed.
To claim the child, assign it to yourself.
To resolve the child, comment with the answer, close it, and add an answer summary and link to the map.
