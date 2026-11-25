using Microsoft.Extensions.AI;

namespace SqlIndexAdvisor.Tests.Advice;

public sealed class FakeChatClient : IChatClient
{
    private readonly Func<CancellationToken, Task<ChatResponse>> _respond;

    private FakeChatClient(Func<CancellationToken, Task<ChatResponse>> respond) => _respond = respond;

    public IReadOnlyList<ChatMessage> ReceivedMessages { get; private set; } = [];

    public ChatOptions? ReceivedOptions { get; private set; }

    public int Calls { get; private set; }

    public static FakeChatClient Replying(string text, string modelId = "fake-model") =>
        new(_ => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)) { ModelId = modelId }));

    public static FakeChatClient Throwing(Exception exception) => new(_ => Task.FromException<ChatResponse>(exception));

    public static FakeChatClient Hanging() =>
        new(async cancellationToken =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("unreachable");
        });

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        Calls++;
        ReceivedMessages = messages.ToList();
        ReceivedOptions = options;
        return _respond(cancellationToken);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The advisor does not stream.");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
