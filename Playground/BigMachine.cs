// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using BigMachines;

namespace Playground;

[BigMachineObject(IncludeAllMachines = true)]
[AddMachine<Netsphere.Machines.NtpMachine>]
public partial class BigMachine;
