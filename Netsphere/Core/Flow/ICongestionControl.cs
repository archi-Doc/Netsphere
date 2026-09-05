// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere.Core;

internal interface ICongestionControl
{
    int NumberInFlight { get; }

    bool IsCongested { get; }

    /// <summary>
    /// Updates congestion state and retransmits packets when needed.
    /// </summary>
    /// <param name="netSender">The packet sender for this processing round.</param>
    /// <param name="elapsedMics">Elapsed time since the previous round, in microseconds.</param>
    /// <param name="elapsedMilliseconds">Elapsed time since the previous round, in milliseconds.</param>
    /// <returns>True to keep the controller registered; false to remove it.</returns>
    bool Process(NetSender netSender, long elapsedMics, double elapsedMilliseconds);

    // void ReportDeliverySuccess();

    // void ReportDeliveryFailure();

    void AddInFlight(SendGene sendGene, int additional);

    void RemoveInFlight(SendGene sendGene, bool ack);

    void LossDetected(SendGene sendGene);

    void AddRtt(int rttMics);
}
