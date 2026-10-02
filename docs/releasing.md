# Releasing

Releases are cut from GitHub; nothing is bumped by hand.

1. Every PR carries one label. [Release Drafter](https://github.com/PianoNic/DockiUp/blob/main/.github/release-drafter.yml) keeps a draft release up to date on each push to `main` and picks the next version from those labels:

   | Label | Bump |
   |---|---|
   | `breaking` | major |
   | `feature`, `enhancement` | minor |
   | `bug`, `refactor` | patch |

   Branches and PR titles are labelled automatically (`feature/…`, `fix/…`, "Add …", "Fix …", "Refactor …").

2. Publishing the draft runs [`release.yaml`](https://github.com/PianoNic/DockiUp/blob/main/.github/workflows/release.yaml):
   - **sync-version** writes the tag into `<Version>` of `src/DockiUp.API/DockiUp.API.csproj` and commits `Bumped DockiUp.API.csproj to X for release [skip ci]` to `main`.
   - **docker** checks out that commit and pushes a multi-arch image (`linux/amd64`, `linux/arm64`) to Docker Hub and GHCR, tagged `X.Y.Z`, `X.Y`, `X` and `latest` (not for pre-releases).

The version shown in the side rail and returned by `/api/App/GetAppInfo` is that assembly version, so the
running image always reports the release it was built from.

## Repository setup

| Secret | Used for |
|---|---|
| `DOCKERHUB_USERNAME` | Docker Hub account the image is pushed to |
| `DOCKERHUB_TOKEN` | Docker Hub access token with read and write access |

GHCR and the release drafts use the built-in `GITHUB_TOKEN`; nothing else needs configuring. After the first
release, set the GHCR package's visibility to public so the image can be pulled without signing in.
