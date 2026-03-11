// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere.Logging;

internal class IdFileLoggerFactory<TOption> : IdFileLogger<TOption>
    where TOption : IdFileLoggerOptions
{
    public IdFileLoggerFactory(UnitCore core, LogUnit logUnit, ILogService logService, TOption options)
        : base(core, logUnit, logService, options)
    {
    }
}
