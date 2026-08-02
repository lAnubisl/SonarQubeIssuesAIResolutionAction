namespace SonarCopilotFix.Interfaces;

public interface ISecretMasker
{
    void MaskKnownSecrets();
}
