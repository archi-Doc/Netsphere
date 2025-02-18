// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere.Crypto;

namespace NetsphereTest;

public class TestConnectionContext : ServerConnectionContext
{
    public enum State
    {
        Waiting,
        Running,
        Complete,
    }

    public TestConnectionContext(ServerConnection serverConnection)
        : base(serverConnection)
    {
    }

    public State CurrentState { get; set; }

    public CancellationToken CancellationToken => cts.Token;

    private readonly CancellationTokenSource cts = new();

    public void Terminate()
        => this.cts.Cancel();
}
