// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere.Crypto;

namespace Netsphere.Version;

public partial record ServerOptions
{
    [SimpleOption("Port", Description = "Port number associated with the address")]
    public int Port { get; set; } = 55555;

    // [SimpleOption(NetConstants.NodePrivateKeyName, Description = "Node secret key for connection", GetEnvironmentVariable = true)]
    // public string NodePrivateKeyString { get; set; } = string.Empty;

    [SimpleOption(NetConstants.RemotePublicKeyName, Description = "Public signature key for remote operation", GetEnvironmentVariable = true)]
    public string RemotePublicKey { get; set; } = string.Empty;

    [SimpleOption("VersionIdentifier", Description = "Version identifier", GetEnvironmentVariable = true)]
    public int VersionIdentifier { get; set; }

    public bool Check(ILogger logger)
    {
        var result = true;

        if (!SignaturePublicKey.TryParse(this.RemotePublicKey, out var remotePublicKey, out _))
        {
            logger.TryGet(LogLevel.Fatal)?.Log($"Specify the remote public key (-{NetConstants.RemotePublicKeyName}) for authentication of remote operations.");
            result = false;
        }

        this.remotePublicKey = remotePublicKey;

        return result;
    }

    internal SignaturePublicKey remotePublicKey { get; private set; }
}
