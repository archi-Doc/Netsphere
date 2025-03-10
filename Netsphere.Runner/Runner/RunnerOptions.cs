// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Net;
using Arc;
using Arc.Unit;
using Netsphere.Crypto;
using SimpleCommandLine;

namespace Netsphere.Runner;

public partial record RunnerOptions
{
    // [SimpleOption("lifespan", Description = "Time in seconds until the runner automatically shuts down (set to -1 for infinite).")]
    // public long Lifespan { get; init; } = 6;

    [SimpleOption(nameof(Port), Description = "Port number associated with the runner", Required = true, ReadFromEnvironment = true)]
    public ushort Port { get; set; } = 49999;

    [SimpleOption(NetConstants.NodeSecretKeyName, Description = "Node secret key for connection", Required = true, ReadFromEnvironment = true)]
    public string NodeSecretKeyString { get; set; } = string.Empty;

    [SimpleOption(NetConstants.RemotePublicKeyName, Description = "Public key for remote operation", Required = true, ReadFromEnvironment = true)]
    public string RemotePublicKeyString { get; set; } = string.Empty;

    public virtual bool Check(ILogger logger)
    {
        var result = true;
        if (!this.RemotePublicKey.IsValid)
        {
            logger.TryGet(LogLevel.Fatal)?.Log($"Specify the remote public key (-{NetConstants.RemotePublicKeyName}) for authentication of remote operations.");
            result = false;
        }

        return result;
    }

    public void Prepare()
    {
        // 1st Argument, 2nd: Environment variable
        /*if (!string.IsNullOrEmpty(this.NodePrivateKeyString) &&
            NodeSecretKey.TryParse(this.NodePrivateKeyString, out var privateKey))
        {
            this.NodeSecretKey = privateKey;
        }

        if (this.NodeSecretKey is null)
        {
            if (BaseHelper.TryParseFromEnvironmentVariable<NodeSecretKey>(NetConstants.NodePublicKeyName, out privateKey))
            {
                this.NodeSecretKey = privateKey;
            }
        }

        this.NodePrivateKeyString = string.Empty;*/

        if (!string.IsNullOrEmpty(this.RemotePublicKeyString) &&
            SignaturePublicKey.TryParse(this.RemotePublicKeyString, out var publicKey, out _))
        {
            this.RemotePublicKey = publicKey;
        }

        if (!this.RemotePublicKey.IsValid)
        {
            if (BaseHelper.TryParseFromEnvironmentVariable<SignaturePublicKey>(NetConstants.RemotePublicKeyName, out publicKey))
            {
                this.RemotePublicKey = publicKey;
            }
        }
    }

    internal SignaturePublicKey RemotePublicKey { get; set; }
}
