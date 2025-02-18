// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere.Crypto;
using Netsphere.Packet;

namespace Netsphere;

public class NetBase : UnitBase, IUnitPreparable
{
    public NetBase(UnitContext context, UnitLogger logger)
        : base(context)
    {
        this.UnitLogger = logger;

        this.NetOptions = new();
        this.NetOptions.NodeName = System.Environment.OSVersion.ToString();
        this.NewServerConnectionContext = connection => new ServerConnectionContext(connection);
        this.NewClientConnectionContext = connection => new ClientConnectionContext(connection);
    }

    #region FieldAndProperty

    public UnitLogger UnitLogger { get; }

    public NetOptions NetOptions { get; private set; }

    public bool IsPortNumberSpecified { get; private set; }

    public bool AllowUnsafeConnection { get; set; } = false;

    public ConnectionAgreement DefaultAgreement { get; set; } = ConnectionAgreement.Default;

    public TimeSpan DefaultTransmissionTimeout { get; set; } = NetConstants.DefaultTransmissionTimeout;

    public EncryptionPublicKey NodePublicKey { get; private set; }

    public SeedKey NodeSeedKey { get; private set; } = default!;

    public bool IsValidNodeKey => this.NodeSeedKey is not null;

    internal Func<ServerConnection, ServerConnectionContext> NewServerConnectionContext { get; set; }

    internal Func<ClientConnection, ClientConnectionContext> NewClientConnectionContext { get; set; }

    internal Func<ulong, PacketType, ReadOnlyMemory<byte>, BytePool.RentMemory?>? RespondPacketFunc { get; set; }

    #endregion

    public void Prepare(UnitMessage.Prepare message)
    {
        // Set port number
        if (this.NetOptions.Port < NetConstants.MinPort ||
            this.NetOptions.Port > NetConstants.MaxPort)
        {
            var showWarning = false;
            if (this.NetOptions.Port != 0)
            {
                showWarning = true;
            }

            this.NetOptions.Port = RandomVault.Default.NextInt32(NetConstants.EphemeralPort, NetConstants.MaxPort + 1);
            if (showWarning)
            {
                this.UnitLogger.TryGet<NetBase>(LogLevel.Error)?.Log($"Port number must be between {NetConstants.MinPort} and {NetConstants.MaxPort}");
                this.UnitLogger.TryGet<NetBase>(LogLevel.Error)?.Log($"Port number is set to {this.NetOptions.Port}");
            }
        }
        else
        {
            this.IsPortNumberSpecified = true;
        }

        // Node key
        if (this.NodeSeedKey is null)
        {
            this.NodeSeedKey = SeedKey.NewEncryption();
            this.NodePublicKey = this.NodeSeedKey.GetEncryptionPublicKey();
        }
    }

    public void SetRespondPacketFunc(Func<ulong, PacketType, ReadOnlyMemory<byte>, BytePool.RentMemory?> func)
    {
        this.RespondPacketFunc = func;
    }

    public void SetOptions(NetOptions netsphereOptions)
    {
        this.NetOptions = netsphereOptions;

        if (!string.IsNullOrEmpty(this.NetOptions.NodeSecretKey) &&
            SeedKey.TryParse(this.NetOptions.NodeSecretKey, out var seedKey))
        {
            this.SetNodeSeedKey(seedKey);
        }

        this.NetOptions.NodeSecretKey = string.Empty; // Erase
    }

    public bool SetNodeSeedKey(SeedKey privateKey)
    {
        try
        {
            this.NodeSeedKey = privateKey;
            this.NodePublicKey = privateKey.GetEncryptionPublicKey();
            var st = this.NodeSeedKey.UnsafeToString();
            var sts = this.NodePublicKey.ToString();
            return true;
        }
        catch
        {
            this.NodeSeedKey = default!;
            this.NodePublicKey = default!;
            return false;
        }
    }

    public byte[] SerializeNodePrivateKey()
    {
        return TinyhandSerializer.Serialize(this.NodeSeedKey);
    }
}
