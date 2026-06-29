# SonarQube Copilot Fix Action

Reusable Docker-based GitHub Action that fetches selected SonarQube issues, builds a deterministic repair prompt, runs GitHub Copilot CLI against the checked-out repository, and opens a draft pull request.

Use this for supervised, workflow-dispatched remediation of known SonarQube issues. Do not use it on untrusted pull request code, forked pull requests, or repositories where AI-generated edits cannot receive human review.

## Design

The reusable unit is a Docker action. The core automation is a .NET 10 C# console app inside the image. GitHub Actions only checks out the consuming repository, passes inputs and isolated secrets, and runs the container. The image includes Git, GitHub CLI, .NET runtime, and the standalone GitHub Copilot CLI.

This project avoids JavaScript and TypeScript for core logic. C# gives typed SonarQube models, explicit process environments, testable prompt generation, and predictable exit codes.

## Token Isolation

Use three separate secrets:

| Secret | Used for | Never used for |
| --- | --- | --- |
| `SONAR_TOKEN` | SonarQube Web API bearer authentication | Copilot CLI, GitHub CLI, git push |
| `COPILOT_CLI_TOKEN` | Copilot CLI child process only | SonarQube, GitHub API, git push |
| `GH_CLI_TOKEN` | GitHub CLI and repository git operations | SonarQube, Copilot CLI |

`GITHUB_TOKEN` is not used by default. It is used only when `allow_github_token_fallback` is `true`, `GH_CLI_TOKEN` is absent, and workflow permissions are sufficient.

All known token values are masked with `::add-mask::`. Child processes receive minimal environment variables; secrets are passed only to the command that needs them.

## Inputs

| Input | Default | Notes |
| --- | --- | --- |
| `sonar_host_url` | required | SonarQube Server or Cloud URL |
| `sonar_project_key` | required | SonarQube project key |
| `sonar_branch` | empty | Sonar branch parameter |
| `sonar_organization` | empty | SonarQube Cloud organization |
| `max_issues` | `10` | Maximum selected issues |
| `issue_statuses` | empty | Comma-separated statuses |
| `severities` | empty | Comma-separated severities |
| `clean_code_attribute_categories` | empty | Modern clean-code category filter where supported |
| `include_rule_details` | `true` | Calls `/api/rules/show` per issue |
| `include_code_snippets` | `true` | Reads snippets from checked-out files |
| `code_snippet_context_lines` | `20` | Lines before and after issue line |
| `copilot_model` | empty | Passed to Copilot CLI with `--model` |
| `copilot_extra_instructions` | empty | Added to the prompt |
| `branch_prefix` | `copilot/sonar-fixes` | Generated branch prefix |
| `base_branch` | detected | Uses `origin/HEAD` or `main` fallback |
| `pull_request_draft` | `true` | Draft PRs by default |
| `dry_run` | `false` | No Copilot, branch, commit, push, or PR |
| `fail_if_no_issues` | `false` | Strict empty-result behavior |
| `allow_github_token_fallback` | `false` | Explicit fallback only |
| `copilot_allow_all_tools` | `false` | Allows all CLI tools without confirmation; otherwise only file writes are pre-approved |

## Example Workflow

```yaml
name: Fix SonarQube Issues With Copilot

on:
  workflow_dispatch:
    inputs:
      max_issues:
        description: Maximum number of SonarQube issues to attempt
        required: false
        default: "10"
      dry_run:
        description: Run without making changes
        required: false
        default: "true"

permissions:
  contents: write
  pull-requests: write

jobs:
  sonar-copilot-fix:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0

      - name: Fix SonarQube issues
        uses: your-org/sonar-copilot-fix-action@v1
        with:
          sonar_host_url: ${{ vars.SONAR_HOST_URL }}
          sonar_project_key: ${{ vars.SONAR_PROJECT_KEY }}
          sonar_branch: ${{ github.ref_name }}
          max_issues: ${{ inputs.max_issues }}
          dry_run: ${{ inputs.dry_run }}
        env:
          SONAR_TOKEN: ${{ secrets.SONAR_TOKEN }}
          COPILOT_CLI_TOKEN: ${{ secrets.COPILOT_CLI_TOKEN }}
          GH_CLI_TOKEN: ${{ secrets.GH_CLI_TOKEN }}
```

## Dry Run

Dry run requires only `SONAR_TOKEN`. It fetches issues, writes `.sonar-copilot/issues-prompt.md`, emits action outputs, and writes the job summary. It does not run Copilot CLI, create a branch, commit, push, or create a pull request.

## Normal Execution

Normal mode requires `SONAR_TOKEN`, `COPILOT_CLI_TOKEN`, and `GH_CLI_TOKEN` unless explicit `GITHUB_TOKEN` fallback is enabled. The action:

1. Fetches and paginates SonarQube issues from `/api/issues/search`.
2. Optionally fetches rule details from `/api/rules/show`.
3. Reads local snippets around affected lines.
4. Generates `.sonar-copilot/issues-prompt.md`.
5. Requires a clean worktree outside `.sonar-copilot`.
6. Runs Copilot CLI with only Copilot token environment variables.
7. Detects changed repository files, excluding generated prompt files.
8. Creates a branch named `<branch_prefix>/<sonar_project_key>/<timestamp>`.
9. Commits, pushes, and creates a draft PR with `gh pr create`.

If no files changed, the action exits successfully without an empty commit or PR. Build, test, lint, and other validation remain the responsibility of the consuming repository's pull request workflows, where the required toolchain and services can be configured normally.

Configure those workflows for `pull_request` events such as `opened` and `synchronize`, and enforce their checks with branch protection or rulesets. Prefer a personal access token or GitHub App installation token for `GH_CLI_TOKEN`; pull request workflows initiated through the repository `GITHUB_TOKEN` may require a maintainer to approve the workflow runs before validation starts.

## Copilot CLI Notes

GitHub Copilot CLI access can differ by subscription and enterprise policy. The action intentionally does not accept arbitrary Copilot command input. It invokes the standalone CLI from the repository workspace with a fixed argument shape:

```text
copilot --prompt <prompt> --no-ask-user [--model <model>] (--allow-tool=write | --allow-all-tools)
```

The command receives `COPILOT_GITHUB_TOKEN`, populated from the `COPILOT_CLI_TOKEN` secret, and disables CLI self-updates. It never receives `SONAR_TOKEN`, `GH_CLI_TOKEN`, or `GITHUB_TOKEN`. The token must be a supported Copilot CLI token, such as a fine-grained personal access token with the Copilot Requests account permission; classic personal access tokens are not supported.

Before Copilot starts, the action writes the complete generated prompt to the job log with a `[copilot prompt]` prefix. While Copilot runs, each stdout and stderr line is forwarded immediately with `[copilot stdout]` or `[copilot stderr]`, so progress and generated output are visible without waiting for the process to finish.

## Pull Request Body

The draft PR includes the SonarQube project, branch, base branch, generated branch, selected issue links, changed files, the current session report returned by the Copilot CLI `/usage` command (including sent, cached, written, received, and reasoning tokens and AI Credits consumed), a note that validation is delegated to PR checks, a Copilot generation note, and a human-review requirement. The same unmodified `/usage` report is included in the GitHub Actions job summary.

## SonarQube Compatibility

The implementation uses bearer authentication and `/api/issues/search`, with project, branch, organization, status, severity, and clean-code category filters. SonarQube Server and SonarQube Cloud can vary by version; unsupported filter combinations produce a clear API error. The client is intentionally small so endpoint parameters can be updated as SonarQube evolves.

## Security

Recommended workflow permissions:

```yaml
permissions:
  contents: write
  pull-requests: write
```

Run this action from `workflow_dispatch` or another trusted event. Do not expose secrets to forked pull requests. Draft PRs are the default because AI-generated changes require human review before merge.

## Build And Test Locally

```bash
dotnet build
dotnet run --project tests/SonarCopilotFix.Tests/SonarCopilotFix.Tests.csproj
docker build -t sonar-copilot-fix-action .
```

For a local dry run:

```bash
docker run --rm \
  -v "$PWD:/github/workspace" \
  -w /github/workspace \
  -e INPUT_SONAR_HOST_URL="https://sonar.example.com" \
  -e INPUT_SONAR_PROJECT_KEY="my-project" \
  -e INPUT_DRY_RUN=true \
  -e SONAR_TOKEN="$SONAR_TOKEN" \
  sonar-copilot-fix-action
```
