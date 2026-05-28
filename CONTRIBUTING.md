# Contributing

Contributions are welcome.
Please open an issue or pull request on GitHub.

By participating in this project you agree to abide by the [Code of Conduct](CODE_OF_CONDUCT.md).

## Where to ask / report

- **Bugs, features, and questions** — open an issue.
- **Security vulnerabilities** — do not open a public issue. Follow [SECURITY.md](SECURITY.md).
- **Code of Conduct concerns** — see the reporting section of [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md).
- **Sibling projects** (Tinkwell core, plugin registry, state machines, etc) — open issues on the relevant repository linked from [README.md](README.md) under *Related repositories*.

## Branch model

Trunk-based:

- `master` is always releasable.
- Work on short-lived topic branches off `main`.
  Suggested naming: `feature/<slug>` for new work, `fix/<slug>` for bug fixes, `docs/<slug>` for documentation-only changes.
- Open a pull request back into `main` when the branch is ready.
  There are no long-lived release or integration branches; releases are cut directly from `main` (see *Release process* below).
- Forks are first-class: fork the repository, push your branch to your fork, and open a PR from there.

## Pull request expectations

- **One topic per PR.** Split unrelated changes into separate PRs; it makes review and revert trivial.
- **Descriptive title.** A conventional-commit-style prefix (`fix:`, `feat:`, `docs:`, `refactor:`, `test:`, `chore:`) is encouraged but not required.
- **Tests.** Behaviour changes need tests.
  Bug fixes should include a regression test.
  Pure refactors keep existing tests green.
- **Docs in the same PR.** If a change affects public APIs, `.tw` grammar, CLI behaviour, or configuration keys, update the relevant files under [docs/](docs/) in the same pull request.
  Breaking changes are flagged in [CHANGELOG.md](CHANGELOG.md) under a **Breaking changes** heading; see the *Status* section of [README.md](README.md) for the `0.x` stability posture.
- **Green CI.** The PR build must pass before review.
  CI runs with `TreatWarningsAsErrors` enabled, so new warnings block the merge.
- **Review.** [.github/CODEOWNERS](.github/CODEOWNERS) routes reviews to `@arepetti`.
  Please address review comments by pushing follow-up commits on the same branch; they will be squashed on merge.
- **Squash-merge by default.** Keep the commit on `main` self-contained and easy to revert.

## Release process

Only the maintainer cuts releases.

Tinkwell is in its `0.x` series; breaking changes between minor versions are allowed and are listed under **Breaking changes** in each release's notes in [CHANGELOG.md](CHANGELOG.md).

## Governance

Tinkwell is maintained by a single maintainer today.
See [GOVERNANCE.md](GOVERNANCE.md) for the decision process, dispute resolution, and the conditions under which the project would move to a multi-maintainer or foundation model.
