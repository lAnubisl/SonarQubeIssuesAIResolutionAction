# Configure `COPILOT_GITHUB_TOKEN`

`COPILOT_GITHUB_TOKEN` authenticates GitHub Copilot CLI when this action uses a GitHub-hosted Copilot model. Store it as a GitHub Actions secret in the repository that runs the action, then pass that secret to the action through `env`.

## Prerequisites

The GitHub account that creates the token must:

- have access to GitHub Copilot;
- be allowed to use Copilot CLI by any applicable organization or enterprise policy; and
- own the token personally. The required permission is not available on organization-owned tokens.

## 1. Create a fine-grained personal access token

1. Open [GitHub's new fine-grained personal access token page](https://github.com/settings/personal-access-tokens/new).
2. Enter a descriptive name, such as `SonarQube Copilot action`.
3. Set an expiration date that follows your organization's credential policy.
4. For **Resource owner**, select your personal account, not an organization.
5. For **Repository access**, select only the repository that will run the action when possible.
6. Under **Permissions**, open **Account permissions**, add **Copilot Requests**, and set it to **Read-only**.
7. Generate the token and copy it immediately. Fine-grained tokens start with `github_pat_`.

Do not use a classic personal access token beginning with `ghp_`; Copilot CLI does not support classic tokens. See GitHub's [Copilot CLI authentication documentation](https://docs.github.com/en/copilot/how-tos/copilot-cli/set-up-copilot-cli/authenticate-copilot-cli#supported-token-types) for the current requirements.

## 2. Store the token as a repository secret

1. Open the consuming repository on GitHub.
2. Go to **Settings** > **Secrets and variables** > **Actions**.
3. Select **New repository secret**.
4. Set the name to `COPILOT_GITHUB_TOKEN`.
5. Paste the fine-grained token as the secret value and select **Add secret**.

## 3. Pass the secret to the action

Set `COPILOT_GITHUB_TOKEN` in the `env` block of the action step:

```yaml
- name: Fix SonarQube issues
  uses: lAnubisl/SonarQubeIssuesAIResolutionAction@v1.0.0
  with:
    sonar_host_url: ${{ vars.SONAR_PROJECT_URL }}
    sonar_project_key: ${{ vars.SONAR_PROJECT_KEY }}
  env:
    SONAR_TOKEN: ${{ secrets.SONAR_TOKEN }}
    COPILOT_GITHUB_TOKEN: ${{ secrets.COPILOT_GITHUB_TOKEN }}
    GH_TOKEN: ${{ github.token }}
```

The secret name on the right may be different, but the environment variable on the left must be `COPILOT_GITHUB_TOKEN`:

```yaml
COPILOT_GITHUB_TOKEN: ${{ secrets.MY_COPILOT_PAT }}
```

Do not put the token directly in the workflow file, an Actions variable, an action input, or repository source code.
