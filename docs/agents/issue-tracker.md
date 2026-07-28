# Issue tracker: GitHub

Issues and specifications for this repository live in GitHub Issues at `hoa-d-vu-vgames/VxFiles`. Use the `gh` CLI for issue operations.

## Conventions

- Create an issue with `gh issue create`.
- Read an issue and its discussion with `gh issue view <number> --comments`.
- Apply or remove labels with `gh issue edit`.
- Close completed work with `gh issue close` and a concise completion comment.
- Infer the repository from the `origin` remote when running inside this clone.

## Pull requests as a triage surface

External pull requests are not a request or triage surface. Collaborator pull requests remain outside the issue queue.

## Ticket dependencies

Use GitHub native issue dependencies when available. If unavailable, record `Blocked by: #<number>` in the issue body. A ticket is ready when every blocker is closed.

## Skill operations

- “Publish to the issue tracker” means create a GitHub issue.
- “Fetch the relevant ticket” means read the issue body, labels, and comments.
- Apply `ready-for-agent` to tickets that are fully specified and can be implemented without further user input.
