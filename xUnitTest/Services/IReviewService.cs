// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere;

namespace xUnitTest.NetsphereTest;

[NetService]
public interface IReviewService : INetService
{
    Task<string?> Echo(string text, CancellationToken cancellationToken);

    Task<Memory<byte>> Memory();

    Task<Memory<byte>> EchoMemory(Memory<byte> memory);

    Task<ReceiveStream?> Open(CancellationToken cancellationToken);

    Task<NetResultAndValue<int>> Result();

    void Channel(ref ResponseChannel<int> channel);
}

[NetObject]
[NetServiceFilter<ReviewFilter>]
public class ReviewService : IReviewService
{
    public Task<string?> Echo(string text, CancellationToken cancellationToken) => Task.FromResult<string?>(text);

    public Task<Memory<byte>> Memory() => Task.FromResult(new byte[] { 1, 2, 3 }.AsMemory());

    public Task<Memory<byte>> EchoMemory(Memory<byte> memory) => Task.FromResult(memory);

    public Task<ReceiveStream?> Open(CancellationToken cancellationToken) => Task.FromResult<ReceiveStream?>(null);

    public Task<NetResultAndValue<int>> Result() => Task.FromResult(new NetResultAndValue<int>(42));

    public void Channel(ref ResponseChannel<int> channel) => channel.SetResponse(42);
}

public class ReviewFilter : IServiceFilter
{
    private int calls;

    public async Task Invoke(TransmissionContext context, Func<TransmissionContext, Task> invoker)
    {
        if (Interlocked.Increment(ref this.calls) != 1)
        {
            throw new InvalidOperationException("Filter instance was shared between requests.");
        }

        await Task.Yield();
        await invoker(context).ConfigureAwait(false);
    }
}
