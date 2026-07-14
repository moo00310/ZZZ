# Claude Code to Codex migration

## Migrated

- Repository guidance and development rules are now in the root `AGENTS.md`.
- Repository-local sandbox defaults are in `.codex/config.toml`.
- Existing project documentation remains the detailed source of truth and is linked from `AGENTS.md`.

## Intentionally not migrated

The files `.claude/settings.json` and `.claude/settings.local.json` contain accumulated, command-specific permission history rather than reusable project instructions. Some entries authorize destructive file deletion, Git history rewriting, pushing, merging, and PR operations. They are not copied into Codex policy.

Codex should request approval according to its active sandbox/policy whenever an operation needs broader authority. The presence of an old Claude permission entry is never authorization for Codex to repeat that operation.

## No source counterpart found

No `CLAUDE.md`, Claude commands, agents, skills, hooks, or MCP server configuration existed in this repository at migration time, so no corresponding Codex assets were needed.

## Cleanup

Keep `.claude/` temporarily if the project may still be opened with Claude Code. Once the Codex workflow is confirmed and Claude Code compatibility is no longer needed, it can be removed in a separate, explicit cleanup change.
