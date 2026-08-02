# Configure `GH_TOKEN`

`GH_TOKEN` authenticates the Git and GitHub CLI operations performed by this action. The action uses it to push generated branches and create draft pull requests in the consuming repository.

## Recommended: use the built-in job token

GitHub automatically creates a short-lived `GITHUB_TOKEN` for every workflow job. You do not need to create or store this token as a repository secret. Expose it to this action under the required `GH_TOKEN` environment-variable name:

```yaml
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

The workflow permissions are required for the operations performed by the action:

- `contents: write` permits pushing the generated branches; and
- `pull-requests: write` permits creating draft pull requests.

The environment variable on the left must be named `GH_TOKEN`. `${{ github.token }}` and `${{ secrets.GITHUB_TOKEN }}` refer to the same built-in job token, but the context form used above makes it clear that no user-created secret is required.

### Allow pull-request creation

The repository or organization must allow GitHub Actions to create pull requests:

1. Open the consuming repository on GitHub.
2. Go to **Settings** > **Actions** > **General**.
3. Under **Workflow permissions**, enable **Allow GitHub Actions to create and approve pull requests**.
4. Select **Save**.

An organization or enterprise policy may prevent a repository administrator from enabling this setting. Ask the applicable administrator to change the policy, or use an approved fine-grained personal access token or GitHub App instead.

See GitHub's documentation on [controlling `GITHUB_TOKEN` permissions](https://docs.github.com/en/actions/how-tos/security-for-github-actions/security-guides/automatic-token-authentication) and [repository Actions settings](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/enabling-features-for-your-repository/managing-github-actions-settings-for-a-repository).

## Alternative: use a fine-grained personal access token

Use a fine-grained personal access token (PAT) when the built-in job token is restricted or when pull requests created by the action must trigger other workflows without built-in-token approval behavior.

### 1. Create the token

1. Open [GitHub's new fine-grained personal access token page](https://github.com/settings/personal-access-tokens/new).
2. Enter a descriptive name, such as `SonarQube fix action`, and choose an expiration date.
3. For **Resource owner**, select the owner of the consuming repository.
4. Under **Repository access**, select only the consuming repository when possible.
5. Under **Repository permissions**, grant:
   - **Contents: Read and write**; and
   - **Pull requests: Read and write**.
6. Generate the token and copy it immediately.

The token owner must already have sufficient access to the repository. An organization may require approval before a fine-grained PAT can access its resources.

### 2. Store the token as a repository secret

On the consuming repository, go to **Settings** > **Secrets and variables** > **Actions** > **New repository secret**. Name the secret `GH_TOKEN`, paste the token, and select **Add secret**.

### 3. Pass the secret to the action

The workflow `permissions` block scopes the built-in job token; it does not limit a PAT. Keep that block narrowly scoped for any other steps in the job, then replace only the `GH_TOKEN` value in the action step:

```yaml
env:
  SONAR_TOKEN: ${{ secrets.SONAR_TOKEN }}
  COPILOT_GITHUB_TOKEN: ${{ secrets.COPILOT_GITHUB_TOKEN }}
  GH_TOKEN: ${{ secrets.GH_TOKEN }}
```

Do not put the token directly in the workflow file, an Actions variable, an action input, or repository source code.

## Alternative: use a GitHub App installation token

A GitHub App is preferable to a user-owned PAT for centrally managed organization automation. Install an app on the consuming repository with **Contents: Read and write** and **Pull requests: Read and write**, then generate its short-lived installation token earlier in the job:

```yaml
- name: Create GitHub App token
  id: app-token
  uses: actions/create-github-app-token@v3
  with:
    client-id: ${{ vars.APP_CLIENT_ID }}
    private-key: ${{ secrets.APP_PRIVATE_KEY }}

- name: Fix SonarQube issues
  uses: lAnubisl/SonarQubeIssuesAIResolutionAction@v1.0.0
  with:
    sonar_host_url: ${{ vars.SONAR_PROJECT_URL }}
    sonar_project_key: ${{ vars.SONAR_PROJECT_KEY }}
  env:
    SONAR_TOKEN: ${{ secrets.SONAR_TOKEN }}
    COPILOT_GITHUB_TOKEN: ${{ secrets.COPILOT_GITHUB_TOKEN }}
    GH_TOKEN: ${{ steps.app-token.outputs.token }}
```

Store the app's private key as an Actions secret; do not store generated installation tokens because they are short-lived. See GitHub's guide to [making authenticated requests with a GitHub App in Actions](https://docs.github.com/en/apps/creating-github-apps/authenticating-with-a-github-app/making-authenticated-api-requests-with-a-github-app-in-a-github-actions-workflow).

## Choosing a token

| Token source | Recommended when | Stored repository secret |
| --- | --- | --- |
| `${{ github.token }}` | Default for a single-repository workflow | No |
| Fine-grained PAT | Built-in-token policies or workflow-trigger behavior are unsuitable | Yes |
| GitHub App installation token | Organization automation should not depend on a person's account | Only the app private key |

Pull requests created with the built-in `GITHUB_TOKEN` can cause `pull_request` workflows to enter an approval-required state. If downstream CI must start without manual approval, use a fine-grained PAT or GitHub App token. See GitHub's [`GITHUB_TOKEN` event behavior](https://docs.github.com/en/actions/concepts/security/github_token#when-github_token-triggers-workflow-runs).
