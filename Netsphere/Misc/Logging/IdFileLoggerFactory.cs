// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere.Logging;

internal class IdFileLoggerFactory<TOption> : IdFileLogger<TOption>
    where TOption : IdFileLoggerOptions
{
    public IdFileLoggerFactory(ExecutionGroup parent, LogUnit logUnit, ILogService logService, TOption options)
        : base(parent, logUnit, logService, options)
    {
    }
}
