// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace Microsoft.AspNetCore.Server.Kestrel.Https
{
    public static class KestrelServerOptionsHttpsExtensions
    {
        public static void ConfigureHttpsDefaults(this KestrelServerOptions serverOptions, Action<HttpsConnectionAdapterOptions> configureOptions)
        {
            serverOptions.AdapterData[nameof(ConfigureHttpsDefaults)] = configureOptions;
        }

        internal static Action<HttpsConnectionAdapterOptions> GetHttpsDefaults(this KestrelServerOptions serverOptions)
        {
            if (serverOptions.AdapterData.TryGetValue(nameof(ConfigureHttpsDefaults), out var action))
            {
                return (Action<HttpsConnectionAdapterOptions>)action;
            }
            return _ => { };
        }

        public static void ConfigureEndpointHttps(this KestrelServerOptions serverOptions, string name, Action<HttpsConnectionAdapterOptions> configureOptions)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentNullException(nameof(name));
            }

            var endpointHttpsConfigurations = serverOptions.GetOrCreateEndpointHttpsConfigurations();
            endpointHttpsConfigurations[name] = configureOptions ?? throw new ArgumentNullException(nameof(configureOptions));
        }

        public static void ConfigureEndpoint(this KestrelServerOptions serverOptions, string name, Action<ListenOptions> configureOptions,
            Action<HttpsConnectionAdapterOptions> configureHttpsOptions)
        {
            serverOptions.ConfigureEndpoint(name, configureOptions);
            serverOptions.ConfigureEndpointHttps(name, configureHttpsOptions);
        }

        internal static IDictionary<string, Action<HttpsConnectionAdapterOptions>> GetOrCreateEndpointHttpsConfigurations(this KestrelServerOptions serverOptions)
        {
            if (!serverOptions.AdapterData.TryGetValue("EndpointHttpsConfigurations", out var obj))
            {
                var configurations = new Dictionary<string, Action<HttpsConnectionAdapterOptions>>(0);
                serverOptions.AdapterData["EndpointHttpsConfigurations"] = configurations;
                return configurations;
            }

            return (IDictionary<string, Action<HttpsConnectionAdapterOptions>>)obj;
        }
    }
}
