![Workflow: connect to SonarQube, run an agentic AI fix loop, and create draft pull requests](docs/assets/sonarqube-copilot-fix-workflow.svg)

# SonarQube Copilot Fix Action

A GitHub Action that fetches selected SonarQube issues, groups them by rule, and asks GitHub Copilot CLI to fix each group in an isolated branch. Each successful group produces its own pull request.

Use this action for supervised remediation of known SonarQube issues.

## Requirements

- An Ubuntu GitHub Actions runner. GitHub-hosted runners are supported; self-hosted runners must provide Bash, cURL, Git, and GitHub CLI.
- A checkout with full Git history and persisted credentials disabled.
- `contents: write` and `pull-requests: write` workflow permissions.
- A clean working tree when the action starts.
- Project build and test tools installed before this action if Copilot needs to use them.

The repository or organization must also allow GitHub Actions to create pull requests when `GH_TOKEN` uses the built-in job token.

## Quick start

```yaml
name: Fix SonarQube issues

on:
  workflow_dispatch:

permissions:
  contents: write
  pull-requests: write

jobs:
  fix:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v7
        with:
          fetch-depth: 0
          persist-credentials: false

      # Install your project's build and test tools here.

      - name: Fix SonarQube issues
        uses: lAnubisl/SonarQubeIssuesAIResolutionAction@v1.0.0
        with:
          sonar_host_url: ${{ vars.SONAR_PROJECT_URL }}
          sonar_project_key: ${{ vars.SONAR_PROJECT_KEY }}
          sonar_branch: main
          max_issues: 20
          copilot_allowed_tools: shell(dotnet:*)
          copilot_extra_instructions: Run dotnet test and fix any failures caused by your changes.
        env:
          SONAR_TOKEN: ${{ secrets.SONAR_TOKEN }}
          COPILOT_GITHUB_TOKEN: ${{ secrets.COPILOT_GITHUB_TOKEN }}
          GH_TOKEN: ${{ github.token }}
```

See the complete [GitHub-hosted Copilot example](docs/examples/fix-sonarqube-issues.yml).

## Credentials

Pass credentials through the action step's `env` block, never as action inputs or literal values in the workflow.

| Environment variable | Purpose | When required | Setup guide |
| --- | --- | --- | --- |
| `SONAR_TOKEN` | Read SonarQube issues and rule details | Always | [Configure `SONAR_TOKEN`](docs/sonar-token.md) |
| `GH_TOKEN` | Push branches and create pull requests | Always | [Configure `GH_TOKEN`](docs/gh-token.md) |
| `COPILOT_GITHUB_TOKEN` | Authenticate GitHub-hosted Copilot models | When no custom provider is configured | [Configure `COPILOT_GITHUB_TOKEN`](docs/copilot-github-token.md) |
| `COPILOT_PROVIDER_API_KEY` | Authenticate a custom model provider | Required for `azure` and `anthropic`; optional for `openai` | Follow your model provider's secret-management guidance |

`SONAR_TOKEN` and `GH_TOKEN` are required in every mode. For model access, choose one of these modes:

1. Set `COPILOT_GITHUB_TOKEN` to use a GitHub-hosted Copilot model.
2. Set `copilot_provider_base_url` and `copilot_model` to use a custom provider. Set `COPILOT_PROVIDER_API_KEY` when the provider requires authentication; `COPILOT_GITHUB_TOKEN` is not needed.

All supplied credential values are masked in the action log.

## Custom model provider

The following example uses an Azure Foundry OpenAI-compatible v1 endpoint. The deployment must support streaming and tool calling.

```yaml
- name: Fix SonarQube issues with Azure Foundry
  uses: lAnubisl/SonarQubeIssuesAIResolutionAction@v1.0.0
  with:
    sonar_host_url: ${{ vars.SONAR_PROJECT_URL }}
    sonar_project_key: ${{ vars.SONAR_PROJECT_KEY }}
    copilot_provider_type: openai
    copilot_provider_base_url: https://<resource-name>.services.ai.azure.com/openai/v1
    copilot_model: <deployment-name>
  env:
    SONAR_TOKEN: ${{ secrets.SONAR_TOKEN }}
    COPILOT_PROVIDER_API_KEY: ${{ secrets.COPILOT_PROVIDER_API_KEY }}
    GH_TOKEN: ${{ github.token }}
```

See the complete [Azure Foundry example](docs/examples/fix-sonarqube-issues-azure-foundry.yml) and GitHub's [Copilot CLI BYOK documentation](https://docs.github.com/en/copilot/how-tos/copilot-cli/customize-copilot/use-byok-models).

## Inputs

| Input | Default | Description |
| --- | --- | --- |
| `sonar_host_url` | required | SonarQube Server or Cloud base URL |
| `sonar_project_key` | required | SonarQube project key |
| `components` | empty | Comma-separated component keys; defaults to `sonar_project_key`. Use `projectKey:path/to/file` for source files |
| `sonar_branch` | empty | SonarQube branch to query |
| `sonar_organization` | empty | SonarQube Cloud organization key; required for organization-scoped rule details |
| `max_issues` | `10` | Maximum number of issues to select |
| `statuses` | `OPEN` | Comma-separated issue statuses |
| `type` | empty | `CODE_SMELL`, `BUG`, or `VULNERABILITY` |
| `severities` | empty | Comma-separated SonarQube severities |
| `impactSoftwareQualities` | empty | Comma-separated qualities such as `RELIABILITY`, `SECURITY`, or `MAINTAINABILITY` |
| `impactSeverities` | empty | Comma-separated impact severities |
| `cleanCodeAttributeCategories` | empty | Comma-separated clean-code categories where supported |
| `rules` | empty | Comma-separated rule keys, such as `csharpsquid:S1234` |
| `include_rule_details` | `true` | Include SonarQube rule details in the Copilot prompt |
| `include_code_snippets` | `true` | Include snippets from checked-out source files in the prompt |
| `code_snippet_context_lines` | `20` | Number of lines before and after each issue line |
| `copilot_cli_version` | `latest` | Copilot CLI release to install; use a release such as `v1.0.69` to pin it |
| `copilot_model` | empty | Model passed to Copilot CLI; required with a custom provider |
| `copilot_provider_type` | empty | Custom provider type: `openai` (default), `azure`, or `anthropic` |
| `copilot_provider_base_url` | empty | Absolute URL that enables custom-provider mode |
| `copilot_offline` | `false` | Enable Copilot CLI offline mode for a local or private provider |
| `copilot_extra_instructions` | empty | Additional reviewer-approved instructions included in the prompt |
| `branch_prefix` | `copilot/sonar-fixes` | Prefix for generated branches |
| `base_branch` | detected | Pull request base branch; uses `origin/HEAD` or falls back to `main` |
| `pull_request_draft` | `true` | Create draft pull requests |
| `fail_if_no_issues` | `false` | Fail when no matching issues are found |
| `copilot_allowed_tools` | empty | Comma-separated Copilot permission patterns added alongside file writes, such as `shell(dotnet:*)` |
| `copilot_allow_all_tools` | `false` | Allow every Copilot CLI tool without confirmation |

Prefer narrowly scoped `copilot_allowed_tools` patterns. `copilot_allow_all_tools` grants unrestricted tool access, except that the action always blocks Copilot from running `git commit`.

## What to expect

- Matching issues are grouped by SonarQube rule key.
- Each rule group runs in a new Copilot session on its own generated branch.
- A pull request is created only when that session changes files or creates commits.
- The action returns to the base branch before processing the next group.
- Build, test, lint, and other validation are the responsibility of the consuming repository. Configure pull request workflows and branch protection or rulesets accordingly.
- Pull requests created with the built-in `GITHUB_TOKEN` may not automatically start downstream workflows. The [`GH_TOKEN` guide](docs/gh-token.md) describes PAT and GitHub App alternatives.

## License

This project is licensed under the [MIT License](LICENSE).
