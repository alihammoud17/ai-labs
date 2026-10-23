namespace AiLab.Domain.Interfaces;

// Tells a model-provider failure (after its retries) apart from a bug of ours, without the caller knowing the provider SDKs
public interface IProviderErrorClassifier
{
    bool IsProviderFailure(Exception ex);
}
