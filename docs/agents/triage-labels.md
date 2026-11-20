# Triage Labels

The `triage` skill uses five canonical roles. In tasklog they are tags on a ticket, set with the `triage` tool (which keeps exactly one state tag and one category tag).

| Role              | Tag               | Meaning                                  |
| ----------------- | ----------------- | ---------------------------------------- |
| `needs-triage`    | `needs-triage`    | Maintainer needs to evaluate this        |
| `needs-info`      | `needs-info`      | Waiting on the reporter for more info    |
| `ready-for-agent` | `ready-for-agent` | Fully specified, ready for an AFK agent  |
| `ready-for-human` | `ready-for-human` | Requires human implementation            |
| `wontfix`         | `wontfix`         | Will not be actioned (closes the ticket) |

Category tags: `bug`, `enhancement`.
