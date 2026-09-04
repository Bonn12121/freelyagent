using System.Runtime.CompilerServices;
using Freely.Agent.Models;
using Freely.Agent.Runtime;

namespace Freely.AI.Providers;

/// <summary>Keeps one conversation runtime while allowing the user to change its backing model.</summary>
public sealed class SwitchableModelProvider(IModelProvider initialProvider) : IModelProvider
{
    private IModelProvider _current = initialProvider;

    public IModelProvider Current => Volatile.Read(ref _current);
    public string Id => Current.Id;
    public string DisplayName => Current.DisplayName;

    public void SwitchTo(IModelProvider provider) => Interlocked.Exchange(ref _current, provider);

    public async IAsyncEnumerable<ModelStreamChunk> StreamAsync(
        AgentRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var provider = Current;
        await foreach (var chunk in provider.StreamAsync(request, cancellationToken).ConfigureAwait(false))
        {
            yield return chunk;
        }
    }
}
