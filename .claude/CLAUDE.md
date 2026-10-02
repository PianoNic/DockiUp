# DockiUp Claude workflow rules

Never push to `main`. Every change goes:

1. **Issue**: open one (or pick existing) with at least one label from `gh label list`.
2. **Branch**: `feature/<issue#>_PascalCase` for new / refactor / docs, `fix/<issue#>_PascalCase` for bugs.
3. **PR**: short imperative title; body is one-line summary + `Closes #<issue>`; at least one label.
4. **Squash-merge + delete branch**, then `git fetch --prune && git reset --hard origin/main`.

## Branch naming

- ✅ `feature/91_VitePressDocs`
- ✅ `fix/92_TitleChipAlignment`

## PR body

One line in commit-subject style, then `Closes #<issue>`. The commits already say what changed, so the PR doesn't need to repeat them.

```
Added the VitePress docs site

Closes #91
```

## Commit subject

Past tense, verb first, short: about 3–7 words, one idea, no body. No "and … and …" lists, no file paths. Squash merges end with `(#PR)`.

- `Added <thing>`
- `Fixed <thing>`
- `Rebuilt <thing>`
- `Removed <thing>`

Examples:

- `Added automatic adoption of compose projects (#85)`
- `Fixed the update chip alignment (#90)`
- `Rebuilt the file editor on CodeMirror`

Release bumps (written by `release.yaml`): `Bumped DockiUp.API.csproj to X for release [skip ci]`.

No AI attribution of any kind: no `Co-Authored-By:` trailers (Claude, Copilot or otherwise) and no "Generated with Claude Code" footers. Squash-merge with `--body ""` so GitHub doesn't append trailers from the PR's commits.

## Labels

Pick from `gh label list`. DockiUp's active set: `feature`, `enhancement`, `bug`, `refactor`, `breaking`. Release Drafter resolves the next version from them (`feature`/`enhancement` → minor, `bug`/`refactor` → patch, `breaking` → major).

## Project

Self-hosted Docker Compose deployments. .NET 10 backend in onion layers (`DockiUp.Domain`, `DockiUp.Application` with Mediator CQRS, `DockiUp.Infrastructure` with EF Core on PostgreSQL, `DockiUp.API`), Angular 21 frontend in `src/DockiUp.Frontend` served by the API from `wwwroot`.

- **Run**: `docker compose -f compose.dev.yml up -d` (database on 5433), `dotnet run --project src/DockiUp.API` (http://localhost:5098), `npm start` in `src/DockiUp.Frontend` (http://localhost:4200).
- **Tests**: `dotnet test src/DockiUp.Tests`; the frontend must build without warnings (`npx ng build`).
- **Docker**: `docker build -f src/DockiUp.API/Dockerfile .`; the image needs the Docker socket mounted.
- **Version**: `<Version>` in `src/DockiUp.API/DockiUp.API.csproj`, shown in the UI via `/api/App/GetAppInfo`. Don't bump it by hand; publishing a release runs `.github/workflows/release.yaml`, which writes the tag into it on main and then builds the image.
- **API client**: `src/DockiUp.Frontend/src/app/api` is generated (`npm run apigen` while the API runs in Development). Never edit it by hand.
- **Migrations**: one per change, `dotnet ef migrations add <Name> --project src/DockiUp.Infrastructure --startup-project src/DockiUp.API`.
- **Hosts and nodes**: every Docker operation goes through `IDockerService`, resolved per host. A new host capability touches four places: `IDockerService`, `DockerService`, `RemoteDockerService` and `NodeAgentHostedService`. Nodes never have a UI of their own.
- **Times**: store and return UTC; the frontend shows them in the browser's locale (`localDate` pipe).
- **Secrets**: never logged; vault values reach compose only through the generated `.dockiup.env`.
- **UI**: Material 3 Expressive. Shared classes live in `src/DockiUp.Frontend/src/_expressive.scss` (`x-page`, `x-card`, `x-list`, `x-group`, …); reuse them instead of page copies. Icons are Material Symbols Rounded (self-hosted). Controls are 40px like Material's buttons, pages don't scroll (lists scroll inside), and confirmations use `ConfirmService`, never the browser's `confirm()`.
- **Docs**: the VitePress site in `docs/` (https://docs.dockiup.pianonic.ch, built by Cloudflare Pages). Keep the README to screenshots, features, quick start and links; keep docs short and step by step, and update the matching page when behaviour changes.
