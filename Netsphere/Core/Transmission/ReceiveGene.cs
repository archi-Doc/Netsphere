// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Netsphere.Core;

[ValueLinkObject(Restricted = true)]
internal partial class ReceiveGene
{// using (transmission.lockObject.EnterScope())
    [Link(Primary = true, Type = ChainType.SlidingList, Name = "DataPositionList")]
    public ReceiveGene(ReceiveTransmission receiveTransmission)
    {
        this.ReceiveTransmission = receiveTransmission;
    }

    #region FieldAndProperty

    public ReceiveTransmission ReceiveTransmission { get; }

    public DataControl DataControl { get; private set; }

    public BytePool.RentMemory Packet { get; private set; }

    public bool IsReceived => this.DataControl != DataControl.Initial;

    #endregion

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetRecv(DataControl dataControl, BytePool.RentMemory toBeShared)
    {
        if (!this.IsReceived)
        {
            this.DataControl = dataControl;
            this.Packet = toBeShared.IncrementAndShare();
        }
    }

    public void Dispose()
    {
        this.DataControl = DataControl.Initial;
        this.Packet = this.Packet.Return();
    }
}
