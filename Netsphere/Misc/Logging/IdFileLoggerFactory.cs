// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere.Logging;

internal class IdFileLoggerFactory<TOption> : IdFileLogger<TOption>
    where TOption : IdFileLoggerOptions
{
    public IdFileLoggerFactory(UnitCore core, UnitLogger unitLogger, TOption options)
        : base(core, unitLogger, options)
    {
    }
}
