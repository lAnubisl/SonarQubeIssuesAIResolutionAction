# SonarQube Copilot Fix Action

Reusable Ubuntu composite GitHub Action that fetches selected SonarQube issues, groups them by rule, and handles each rule group in an isolated branch and GitHub Copilot CLI session, producing one draft pull request per successfully fixed rule group.

Use this for supervised, workflow-dispatched remediation of known SonarQube issues. Do not use it on untrusted pull request code, forked pull requests, or repositories where AI-generated edits cannot receive human review.

## Design

The reusable unit is a composite action that runs directly on an Ubuntu runner. The core automation is a .NET 10 C# console app compiled from source with `dotnet run` when the action executes. The action installs its own .NET 10 SDK and pinned standalone GitHub Copilot CLI, while project SDKs installed by preceding workflow steps remain available to Copilot through the runner `PATH`.

This project avoids JavaScript and TypeScript for core logic. C# gives typed SonarQube models, explicit process environments, testable prompt generation, and predictable exit codes.

GitHub-hosted Ubuntu runners are supported. Self-hosted Ubuntu runners are best-effort and must provide Bash, cURL, Git, and GitHub CLI.

## Token Isolation

Use three separate secrets:

| Secret | Used for | Never used for |
| --- | --- | --- |
| `SONAR_TOKEN` | SonarQube Web API bearer authentication | Copilot CLI, GitHub CLI, git push |
| `COPILOT_CLI_TOKEN` | Copilot CLI child process only | SonarQube, GitHub API, git push |
| `GH_CLI_TOKEN` | GitHub CLI and repository git operations | SonarQube, Copilot CLI |

All known token values are masked with `::add-mask::`. Child processes receive minimal environment variables; secrets are passed only to the command that needs them.

## Inputs

| Input | Default | Notes |
| --- | --- | --- |
| `sonar_host_url` | required | SonarQube Server or Cloud URL |
| `sonar_project_key` | required | SonarQube project key |
| `components` | empty | Comma-separated component keys; defaults to `sonar_project_key`. Use `projectKey:path/to/file` for source files |
| `sonar_branch` | empty | Sonar branch parameter |
| `sonar_organization` | empty | SonarQube Cloud organization |
| `max_issues` | `10` | Maximum selected issues |
| `statuses` | `OPEN` | Comma-separated statuses |
| `type` | empty | Issue type: `CODE_SMELL`, `BUG`, or `VULNERABILITY` |
| `severities` | empty | Comma-separated severities |
| `impactSoftwareQualities` | empty | Comma-separated software qualities, such as `RELIABILITY`, `SECURITY`, or `MAINTAINABILITY` |
| `impactSeverities` | empty | Comma-separated impact severities |
| `cleanCodeAttributeCategories` | empty | Modern clean-code category filter where supported |
| `rules` | empty | Comma-separated rule keys, such as `csharpsquid:S1234` |
| `include_rule_details` | `true` | Calls `/api/rules/show` per issue |
| `include_code_snippets` | `true` | Reads snippets from checked-out files |
| `code_snippet_context_lines` | `20` | Lines before and after issue line |
| `copilot_model` | empty | Passed to Copilot CLI with `--model` |
| `copilot_extra_instructions` | empty | Added to the prompt |
| `branch_prefix` | `copilot/sonar-fixes` | Generated branch prefix |
| `base_branch` | detected | Uses `origin/HEAD` or `main` fallback |
| `pull_request_draft` | `true` | Draft PRs by default |
| `fail_if_no_issues` | `false` | Strict empty-result behavior |
| `copilot_allowed_tools` | empty | Comma-separated Copilot permission patterns added alongside file writes, such as `shell(dotnet:*)` |
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

      # Install only the project toolchains Copilot needs. These are examples;
      # omit any setup steps that are not relevant to your repository.
      - uses: actions/setup-dotnet@v5
        with:
          dotnet-version: "8.0.x"

      - uses: actions/setup-python@v6
        with:
          python-version: "3.12"

      - uses: actions/setup-java@v5
        with:
          distribution: temurin
          java-version: "21"

      - name: Fix SonarQube issues
        uses: your-org/sonar-copilot-fix-action@v1
        with:
          sonar_host_url: ${{ vars.SONAR_HOST_URL }}
          sonar_project_key: ${{ vars.SONAR_PROJECT_KEY }}
          sonar_branch: ${{ github.ref_name }}
          max_issues: ${{ inputs.max_issues }}
          type: BUG
          copilot_allowed_tools: "shell(dotnet:*),shell(python:*),shell(java:*)"
        env:
          SONAR_TOKEN: ${{ secrets.SONAR_TOKEN }}
          COPILOT_CLI_TOKEN: ${{ secrets.COPILOT_CLI_TOKEN }}
          GH_CLI_TOKEN: ${{ secrets.GH_CLI_TOKEN }}
```

## Execution

The action requires `SONAR_TOKEN`, `COPILOT_CLI_TOKEN`, and `GH_CLI_TOKEN`. It:

1. Fetches and paginates open SonarQube issues from `/api/issues/search`.
2. Optionally fetches rule details from `/api/rules/show`.
3. Reads local snippets around affected lines.
4. Requires a clean worktree outside `.sonar-copilot`.
5. Switches to the resolved base branch.
6. Groups selected issues by their SonarQube rule key.
7. For each rule group, creates and checks out a branch named `<branch_prefix>/<sonar_project_key>/<rule_key>/<timestamp>`.
8. Generates a prompt containing every selected issue for that rule and starts a fresh Copilot CLI session with a unique session ID.
9. Detects both uncommitted files and commits created after the per-group snapshot, excluding generated prompt files from the changed-file list.
10. Commits any remaining worktree changes, pushes the rule-group branch, and creates a draft PR with `gh pr create`.
11. Switches back to the base branch before starting the next rule group.

If neither files nor `HEAD` changed for a rule group, the action skips its empty commit and PR, switches back to the base branch, and continues with the next group. Build, test, lint, and other validation remain the responsibility of the consuming repository's pull request workflows. When Copilot should run a project tool while preparing the fix, install that tool before this action and grant only its required command pattern with `copilot_allowed_tools`.

Configure those workflows for `pull_request` events such as `opened` and `synchronize`, and enforce their checks with branch protection or rulesets. Use a personal access token or GitHub App installation token for `GH_CLI_TOKEN`.

## Copilot CLI Notes

GitHub Copilot CLI access can differ by subscription and enterprise policy. The action intentionally does not accept arbitrary Copilot command input. It invokes the standalone CLI from the repository workspace with a fixed argument shape:

```text
copilot --prompt <prompt> --no-ask-user [--model <model>] (--allow-tool=write[,<permission-pattern>...] | --allow-all-tools) --deny-tool="shell(git commit)"
```

The command receives `COPILOT_GITHUB_TOKEN`, populated from the `COPILOT_CLI_TOKEN` secret, and disables CLI self-updates. It receives the runner `PATH`, `DOTNET_ROOT`, and `JAVA_HOME` so explicitly installed project tools can run. It never receives `SONAR_TOKEN` or `GH_CLI_TOKEN`. The token must be a supported Copilot CLI token, such as a fine-grained personal access token with the Copilot Requests account permission; classic personal access tokens are not supported.

`copilot_allowed_tools` accepts comma-separated Copilot CLI permission patterns. Prefer narrow entries such as `shell(dotnet test)` or `shell(dotnet:*)`. The existing `copilot_allow_all_tools` input remains available as an explicit unrestricted override. The action always denies Copilot's `shell(git commit)` tool, even with `copilot_allow_all_tools`, and supplies a process-scoped Git `pre-commit` hook as a second guard. The generated prompt also tells Copilot to leave changes uncommitted.

Before Copilot starts, the action writes the complete generated prompt to the job log with a `[copilot prompt]` prefix. While Copilot runs, each stdout and stderr line is forwarded immediately with `[copilot stdout]` or `[copilot stderr]`, so progress and generated output are visible without waiting for the process to finish.

After Copilot finishes, the action uses the stderr captured from that same process as the Copilot session summary.

## Pull Request Body

Each draft PR includes all selected SonarQube issues for its rule, the project, base branch, generated branch, changed files, and isolated Copilot session summary captured from stderr. The GitHub Actions job summary lists every rule-group outcome and created pull request.

## SonarQube Compatibility

The implementation uses bearer authentication and `/api/issues/search`. The action's `components` input is sent as SonarQube's `componentKeys` query parameter and defaults to `sonar_project_key`; filter source files with component keys such as `my-project:src/Example.cs`. The singular `type` input is validated and sent as SonarQube's `types` query parameter. Other search-filter inputs use the SonarQube query parameter names `statuses`, `severities`, `impactSoftwareQualities`, `impactSeverities`, `cleanCodeAttributeCategories`, and `rules`. `statuses` defaults to `OPEN`, so status filtering happens in SonarQube rather than after retrieval. SonarQube Server and SonarQube Cloud can vary by version; unsupported filter combinations produce a clear API error. The client is intentionally small so endpoint parameters can be updated as SonarQube evolves.

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
dotnet test
```

For a local run:

```bash
INPUT_SONAR_HOST_URL="https://sonar.example.com" \
INPUT_SONAR_PROJECT_KEY="my-project" \
GITHUB_WORKSPACE="$PWD" \
SONAR_TOKEN="$SONAR_TOKEN" \
COPILOT_CLI_TOKEN="$COPILOT_CLI_TOKEN" \
GH_CLI_TOKEN="$GH_CLI_TOKEN" \
dotnet run \
  --project src/SonarCopilotFix/SonarCopilotFix.csproj \
  --configuration Release \
  --no-launch-profile
```
