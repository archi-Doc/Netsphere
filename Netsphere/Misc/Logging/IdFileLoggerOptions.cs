// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere.Logging;

public record class IdFileLoggerOptions : FileLoggerOptions
{
    public IdFileLoggerOptions()
    {
        this.Formatter = new SimpleLogFormatterOptions(true) with
        {
            EventIdFormat = "X4",
        };

        this.MaxQueue = 10_000;
    }

    /// <summary>
    /// Gets the upper limit of log stream.
    /// </summary>
    public int MaxStreamCapacity { get; init; } = 10;
}
