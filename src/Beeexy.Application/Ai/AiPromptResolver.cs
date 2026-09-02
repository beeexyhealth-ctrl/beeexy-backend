namespace Beeexy.Application.Ai;

public sealed record AiResolvedPrompt(
    AiPromptIdentity Identity,
    string SystemInstructions,
    string UserContent);

public interface IAiPromptContract
{
    AiPromptIdentity Identity { get; }

    AiResolvedPrompt Build(string preparedInput);
}

public interface IAiPromptResolver
{
    AiResolvedPrompt Resolve(AiPromptIdentity identity, string preparedInput);
}

public sealed class AiPromptContractNotFoundException : Exception
{
    public AiPromptContractNotFoundException()
        : base("The requested AI prompt contract version is not registered.")
    {
    }
}

public sealed class AiPromptResolver : IAiPromptResolver
{
    private readonly IReadOnlyDictionary<AiPromptIdentity, IAiPromptContract> contracts;

    public AiPromptResolver(IEnumerable<IAiPromptContract> contracts)
    {
        ArgumentNullException.ThrowIfNull(contracts);
        this.contracts = contracts.ToDictionary(contract => contract.Identity);
    }

    public AiResolvedPrompt Resolve(AiPromptIdentity identity, string preparedInput)
    {
        ArgumentNullException.ThrowIfNull(identity);
        AiContractGuard.Content(preparedInput, nameof(preparedInput));
        if (!contracts.TryGetValue(identity, out var contract))
        {
            throw new AiPromptContractNotFoundException();
        }

        var resolved = contract.Build(preparedInput) ??
            throw new InvalidOperationException("The AI prompt contract returned no content.");
        if (resolved.Identity != identity)
        {
            throw new InvalidOperationException(
                "The AI prompt contract returned a different identity.");
        }

        AiContractGuard.Content(resolved.SystemInstructions, nameof(resolved.SystemInstructions));
        AiContractGuard.Content(resolved.UserContent, nameof(resolved.UserContent));
        return resolved;
    }
}
