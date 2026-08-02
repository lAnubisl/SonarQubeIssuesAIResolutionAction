# Configure `SONAR_TOKEN`

`SONAR_TOKEN` authenticates the read-only SonarQube Web API requests made by this action. The action uses Bearer authentication to search project issues through `/api/issues/search` and, when `include_rule_details` is enabled, retrieve rule descriptions through `/api/rules/show`.

## Required access

The account that creates the token must be able to view the target SonarQube project:

- For a private project, grant the account **Browse Project** permission.
- For a public project, authenticated users can normally browse its issues without an explicit project grant.

The action does not change issues or submit an analysis, so **Administer Issues** and **Execute Analysis** are not required. It reads source snippets from the GitHub Actions checkout rather than from the SonarQube source-code API, so **See Source Code** is not required either.

Use a dedicated automation account with access only to the projects this action processes when your SonarQube deployment and identity-management policy allow it. A user token inherits the permissions of the account that generated it.

## SonarQube Server: create a user token

1. Sign in to the same SonarQube Server instance configured as `sonar_host_url`.
2. Open the account menu in the upper-right corner and select **My Account**.
3. Select the **Security** tab.
4. Enter a descriptive token name, such as `GitHub SonarQube fix action`.
5. For **Type**, select **User Token**. Do not select **Project Analysis Token** or **Global Analysis Token**.
6. Choose an expiration that follows your organization's credential policy.
7. Select **Generate** and copy the value immediately. SonarQube will not display it again.

See SonarSource's documentation on [managing SonarQube Server tokens](https://docs.sonarsource.com/sonarqube-server/user-guide/managing-tokens) and [authenticating to the Web API](https://docs.sonarsource.com/sonarqube-server/extension-guide/web-api).

## SonarQube Cloud: create a personal access token

1. Sign in to the correct SonarQube Cloud instance:
   - `https://sonarcloud.io` for the EU instance; or
   - `https://sonarqube.us` for the US instance.
2. Open the account menu in the upper-right corner and select **My Account** > **Security**.
3. Enter a descriptive token name, such as `GitHub SonarQube fix action`.
4. Select **Generate** and copy the value immediately. SonarQube Cloud will not display it again.

For a private project, the account must belong to the relevant organization and have **Browse Project** permission. Create the token on the same Cloud instance used by `sonar_host_url`; tokens are not interchangeable between the EU and US instances.

SonarQube Cloud Scoped Organization Tokens currently grant only **Execute Analysis** permission, so they are not suitable for this action's issue-search requests. See SonarSource's documentation on [managing SonarQube Cloud personal access tokens](https://docs.sonarsource.com/sonarqube-cloud/managing-your-account/managing-tokens) and [Web API authentication](https://docs.sonarsource.com/sonarqube-cloud/advanced-setup/web-api).

### Set `sonar_organization`

For SonarQube Cloud, set `sonar_organization` to the organization key that owns the project. This is required for complete rule details: the action passes the value as the `organization` parameter to `/api/rules/show`, which lets SonarQube Cloud resolve organization-scoped rule information.

```yaml
with:
  sonar_host_url: https://sonarcloud.io
  sonar_project_key: your_project_key
  sonar_organization: your_organization_key
```

Use the organization key, not its display name. You can find it in the SonarQube Cloud organization URL:

```text
https://sonarcloud.io/organizations/<organization-key>
```

If `sonar_organization` is omitted, issue search may still succeed, but rule descriptions can be missing. The action then continues without the unavailable rule details.

## Store the token as a repository secret

1. Open the consuming repository on GitHub.
2. Go to **Settings** > **Secrets and variables** > **Actions**.
3. Select **New repository secret**.
4. Set the name to `SONAR_TOKEN`.
5. Paste the SonarQube token as the secret value and select **Add secret**.

## Pass the secret to the action

Set `SONAR_TOKEN` in the `env` block of the action step. The host URL and project identifiers are action inputs, not secrets:

```yaml
- name: Fix SonarQube issues
  uses: lAnubisl/SonarQubeIssuesAIResolutionAction@v1.0.0
  with:
    sonar_host_url: https://sonarcloud.io
    sonar_project_key: your_project_key
    sonar_organization: your_organization_key
  env:
    SONAR_TOKEN: ${{ secrets.SONAR_TOKEN }}
    COPILOT_GITHUB_TOKEN: ${{ secrets.COPILOT_GITHUB_TOKEN }}
    GH_TOKEN: ${{ github.token }}
```

For SonarQube Server, use the full externally reachable instance URL and omit `sonar_organization`:

```yaml
with:
  sonar_host_url: https://sonarqube.example.com
  sonar_project_key: your_project_key
env:
  SONAR_TOKEN: ${{ secrets.SONAR_TOKEN }}
  COPILOT_GITHUB_TOKEN: ${{ secrets.COPILOT_GITHUB_TOKEN }}
  GH_TOKEN: ${{ github.token }}
```

The secret name on the right may be different, but the environment variable on the left must be `SONAR_TOKEN`:

```yaml
SONAR_TOKEN: ${{ secrets.MY_SONARQUBE_API_TOKEN }}
```

Do not put the token directly in the workflow file, an Actions variable, an action input, or repository source code.

## Rotation and lifecycle

- For SonarQube Server, set an expiration and rotate the token before it expires.
- Update the existing GitHub Actions secret after rotation; the workflow file does not need to change.
- Revoke the old token from **My Account** > **Security** after confirming that a workflow succeeds with the replacement.
- A user token stops working if its owner is deleted, deactivated, or loses access to the project.
- SonarQube Cloud automatically removes personal access tokens that have been inactive for 60 days.
