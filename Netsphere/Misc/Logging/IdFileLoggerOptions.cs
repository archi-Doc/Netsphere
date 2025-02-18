// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere.Logging;

public class IdFileLoggerOptions : FileLoggerOptions
{
    public IdFileLoggerOptions()
    {
        this.Formatter.EventIdFormat = "X4";
        this.MaxQueue = 10_000;
    }

    /// <summary>
    /// Gets or sets the upper limit of log stream.
    /// </summary>
    public int MaxStreamCapacity { get; set; } = 10;
}
