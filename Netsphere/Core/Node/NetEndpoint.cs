// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Netsphere;

[TinyhandObject]
public readonly partial record struct NetEndpoint : IEquatable<NetEndpoint>
{
    public NetEndpoint(RelayId relayId, IPEndPoint? endPoint)
    {
        this.EndPoint = endPoint;
        this.RelayId = relayId;
    }

    [Key(0)]
    public readonly RelayId RelayId;

    [Key(1)]
    public readonly IPEndPoint? EndPoint;

    [MemberNotNullWhen(true, nameof(EndPoint))]
    public bool IsValid
        => this.EndPoint is not null;

    /*public NetAddress ToNetAddress()
        => new NetAddress(this.EndPoint.Address, (ushort)this.EndPoint.Port);*/

    public bool IsPrivateOrLocalLoopbackAddress()
        => this.EndPoint is not null &&
        new NetAddress(this.EndPoint.Address, (ushort)this.EndPoint.Port).IsPrivateOrLocalLoopbackAddress();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool EndPointEquals(NetEndpoint endpoint)
    {
        if (this.EndPoint is null)
        {
            return endpoint.EndPoint is null;
        }
        else
        {
            return this.EndPoint.Equals(endpoint.EndPoint);
        }
    }

    public bool Equals(NetEndpoint endPoint)
    {
        if (this.RelayId != endPoint.RelayId)
        {
            return false;
        }

        if (this.EndPoint is null)
        {
            return endPoint.EndPoint is null;
        }

        return this.EndPoint.Equals(endPoint.EndPoint);
    }

    public override int GetHashCode()
        => HashCode.Combine(this.RelayId, this.EndPoint);

    public override string ToString()
        => this.IsValid ? $"{this.RelayId.ToString()}{NetAddress.RelayIdSeparator}{this.EndPoint?.ToString()}" : string.Empty;
}
