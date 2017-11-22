// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Extensions.Configuration;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal
{
    public interface IHttpsProvider
    {
        void ConfigureHttps(ListenOptions listenOptions, IConfigurationSection certConfig);
    }
}
