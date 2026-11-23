# SQL Index Advisor

[![CI](https://github.com/alemayehuTsion/SqlIndexAdvisor/actions/workflows/ci.yml/badge.svg)](https://github.com/alemayehuTsion/SqlIndexAdvisor/actions/workflows/ci.yml)

An AI-assisted index advisor for SQL Server, built on .NET 10.

It reads database metadata and usage statistics, proposes index improvements using deterministic rules,
asks an AI model to explain and rank them, validates every proposal in code, and requires human approval.
It generates `CREATE INDEX` and rollback scripts but **never executes them**.

All data in this repository is synthetic.

## Layout

| Path | Purpose |
|---|---|
| `src/SqlIndexAdvisor.Core` | Class library: collector, rules engine, validator, AI advisor, script generator |
| `src/SqlIndexAdvisor.Cli` | Console app: approval workflow and audit log |
| `tests/SqlIndexAdvisor.Tests` | xUnit tests |
| `database/` | Synthetic Shop database and a deliberately slow query |

## Roadmap

| # | Milestone | Status |
|---|---|---|
| 0 | Solution, global.json, .gitignore, git init, projects, first test | Done |
| 1 | Synthetic database + slow query | Done |
| 2 | Collector (DMVs, Dapper) | |
| 3 | Rules engine | |
| 4 | Safety validator | |
| 5 | AI advisor (structured JSON output) | |
| 6 | Approval CLI + audit log | |
| 7 | Script generator + before/after measurement | |
| 8 | README, architecture diagram, demo script | |
