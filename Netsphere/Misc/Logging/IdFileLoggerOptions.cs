// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere.Logging;

/// <summary>
/// Configures identifier-based log files and their retention.
/// </summary>
public record class IdFileLoggerOptions : FileLoggerOptions
{
    public IdFileLoggerOptions()
    {
        this.FormatterOptions = new SimpleLogFormatterOptions(true) with
        {
            EventIdFormat = "X4",
        };

        this.MaxQueue = 10_000;
    }

    /// <summary>
    /// Gets the maximum number of log file streams kept open.
    /// </summary>
    public int MaxStreamCapacity { get; init; } = 10;
}
