// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Microsoft.Extensions.DependencyInjection;

namespace Netsphere.Service;

internal readonly record struct ObjectTypeAndServiceDescriptor(Type ObjectType, ServiceDescriptor ServiceDescriptor);
