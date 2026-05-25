# Contributing

Thank you for your interest in contributing to DebugProbe.AspNetCore.

## Getting Started

Clone the repository and open the solution:
co
```bash
git clone https://github.com/DebugProbe/DebugProbe.AspNetCore.git
```

Open:

```text
DebugProbe.AspNetCore.sln
```

## Running the Project

`DebugProbe.AspNetCore` is the core class library.

Use the sample application for local development and testing:

```text
DebugProbe.SampleApi/
```

Run the sample API project to test DebugProbe functionality and UI behavior during development.

## Branch Naming

Use descriptive lowercase branch names.

Examples:

```text
feat/add-request-filtering
feature/add-request-filtering
fix/handle-empty-response-body
refactor/split-compare-engine
ci/update-github-actions
docs/update-contributing-guide
```

## Commit Message Convention

This repository follows the [Conventional Commits](https://www.conventionalcommits.org/) specification.

Format:

```text
type: short description
```

Examples:

```text
feat: add payload type badges
fix: handle empty compare bodies
refactor: split compare engine
docs: update README screenshots
ci: run automated tests in GitHub Actions
test: add request persistence tests
```

Common commit types:

- `feat` → new feature
- `fix` → bug fix
- `refactor` → internal code restructuring
- `docs` → documentation changes
- `test` → automated tests
- `ci` → CI/CD and GitHub Actions
- `chore` → maintenance and tooling updates

## Pull Requests

Before opening a pull request:

- Ensure the project builds successfully
- Ensure all automated tests pass
- Keep pull requests focused and small
- Provide a clear description of the change
- Link related issues when applicable

Pull request titles should also follow Conventional Commits.

Examples:

```text
feat: add request filtering
fix: persist requests on unhandled exceptions
docs: improve contributing guidelines
```

## Coding Style

- Keep implementations simple and maintainable
- Prefer readable code over unnecessary abstraction
- Follow existing project structure and naming conventions

## Reporting Issues

When opening issues, include:

- Expected behavior
- Actual behavior
- Steps to reproduce
- Screenshots or logs if relevant

## Security

Please do not report security vulnerabilities publicly.

See:

```text
SECURITY.md
```
