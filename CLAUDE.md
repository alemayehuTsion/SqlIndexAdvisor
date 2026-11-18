# SQL Index Advisor

Project overview, layout and roadmap: @README.md

## Rules

- The app has no executor: it only writes reviewed .sql files. Never add code that runs DDL against a database.
- The AI may explain and rank candidates but cannot invent changes; code-side validation decides.
- All data is synthetic.
- No secrets in the repo: API keys and connection strings come from user-secrets or environment variables.
- Use full absolute paths in shell commands.

## Code style

- Favour clean code over commented code: make code explain itself through clear names and small,
  well-named methods. Reach for a comment only when the code cannot carry the meaning.
- No comments that just repeat what the code already says. Comment only the why: intent, constraints,
  non-obvious decisions, or workarounds.

## Decisions made

- Display name "SQL Index Advisor"; code name `SqlIndexAdvisor`.
- Target framework `net10.0` (current LTS), SDK pinned in `global.json`. The solution uses the `.slnx`format.