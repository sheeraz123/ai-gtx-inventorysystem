using AI.GTX.Harness.Core;

namespace AI.GTX.Harness.Infrastructure;

public sealed class PlaceholderAiProvider : IAiModelProvider
{
    public string Name => "placeholder";
    public Task<ModelResponse> GenerateAsync(ModelRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ModelResponse(false, "No AI request sent. Configure a tested provider adapter in Milestone 2."));
    }
}
public sealed class ModelProviderResolver(IEnumerable<IAiModelProvider> providers) : IModelProviderResolver
{
    private readonly IReadOnlyDictionary<string, IAiModelProvider> registrations = providers.ToDictionary(p => p.Name, StringComparer.Ordinal);
    public IAiModelProvider Resolve(string name) => registrations.TryGetValue(name, out var provider)
        ? provider : throw new InvalidOperationException("Model provider is not registered.");
}
public sealed class ProviderOptions
{
    // Metadata for a future adapter. No credential values are bound or printed in Milestone 1.
    public string CredentialEnvironmentVariable { get; set; } = "GTX_AI_API_KEY";
}
