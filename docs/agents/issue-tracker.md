# Issue tracker: tasklog

Issues and specs for this repo live in a tasklog workspace at `.tasklog/`, managed through the `tasklog` MCP tools (`mcp__tasklog__*`). Do not hand-edit files under `.tasklog/` when a tool covers the operation.

## Conventions

- One body of work is an **effort** (`create_effort`), which creates `<effort>/map.md` and `<effort>/spec.md`.
- Implementation issues are **tickets** in an effort (`create_item`), addressed by Item ID `effort/NN`.
- Ticket types: `research`, `prototype`, `grilling`, `task`; omit for an implementation ticket.
- Triage state and category (`bug`, `enhancement`) are tags, set with `triage` (see `triage-labels.md`).
- Dependencies use `blocked_by` on `create_item`.
- Pass `commit: true` to write tools to commit `.tasklog` changes (never pushes).

## When a skill says "publish to the issue tracker"

Call `create_effort` if the body of work is new, then `create_item` for each ticket (specs go in the effort's `spec.md` via `replace_section` / `append_to_section`).

## When a skill says "fetch the relevant ticket"

Call `get_item` with the Item ID (e.g. `billing-v2/03`). Use `list_items` to search.

## Wayfinding operations

- **Map**: `<effort>/map.md` (Destination, Notes, Decisions so far, Not yet specified, Out of scope).
- **Frontier**: `frontier` with the effort name returns ready, claimed and blocked tickets.
- **Claim**: `claim` before any work on a ticket.
- **Resolve**: `resolve` with `answer` and a one-line `gist`, which also appends to the map's Decisions so far.
