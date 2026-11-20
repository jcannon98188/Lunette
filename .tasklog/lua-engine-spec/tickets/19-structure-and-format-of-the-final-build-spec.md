---
kind: ticket
title: Structure and format of the final build spec
status: resolved
type: grilling
blocked_by: [12, 13, 14, 15, 16, 17, 18]
created: 2026-11-20
updated: 2026-11-20T03:20:46Z
---

## Question

What is the structure and format of the final build spec document (the effort's `spec.md` or a set of files): section order, how decisions from all resolved tickets are consolidated without restating them inconsistently, how normative rules are marked (MUST/SHOULD, enforcement mechanism per rule), glossary and ADR placement (`docs/agents/domain.md`), how open implementation-time questions and waivers are recorded, and what a reader building the engine needs first?

## Answer

Accepted as proposed in grilling.

1. **Layout**: `spec.md` is a short index with reading order and cross-cutting rules; about 8 chapter files in `.tasklog/lua-engine-spec/spec/` (value model, VM, strings, GC, stdlib/platform, testing/CI, quality discipline).
2. **Authority**: the spec is the authoritative normative text. Each decision is restated once, in the one chapter that owns it; other chapters cross-reference by rule ID. Each rule has a `Source:` link to its ticket; tickets are frozen as the decision record.
3. **Rules**: each has a stable ID (e.g. `STR-007`), an RFC 2119 keyword and an `Enforced by:` line (analyzer LD00x, allocation test, JIT budget check, suite file, CI gate, or explicit `review`).
4. **Glossary/ADRs**: root `GLOSSARY.md` (terms only) and `docs/adr/0001-0005` written now: LuaValue layout, heap frames, pending-state errors, single core assembly, best-effort interning. The spec links to them.
5. **Open questions/waivers**: each chapter ends with `## Open questions` (`OQ-<area>-n`, owner tier, default if undecided). Waivers live only in the reason-coded manifest; the spec points to it and never lists them.
6. **spec.md opening order**: destination and non-goals; milestone plan (test tiers with stdlib build order); solution layout; 1-page architecture map; chapter reading order; rule index table (ID, keyword, gist, enforcement).
7. **Consistency**: appendix traceability matrix (ticket → rule IDs) proving all 18 tickets landed; checks for unique IDs, `Enforced by:` on every rule, and no cross-chapter contradictions. Writing the spec itself belongs to the later effort; no new ticket.
