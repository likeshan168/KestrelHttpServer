// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Protocols;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal
{
    internal class AddressBinder
    {
        public static async Task BindAsync(IServerAddressesFeature addresses,
            KestrelServerOptions serverOptions,
            ILogger logger,
            IHttpsProvider httpsProvider,
            Func<ListenOptions, Task> createBinding)
        {
            var configReader = new ConfigReader(serverOptions.Configuration);
            var listenOptions = serverOptions.ListenOptions;
            var strategy = CreateStrategy(
                listenOptions.ToArray(),
                configReader,
                addresses.Addresses.ToArray(),
                addresses.PreferHostingUrls);

            var context = new AddressBindContext
            {
                Addresses = addresses.Addresses,
                ListenOptions = listenOptions,
                ServerOptions = serverOptions,
                Logger = logger,
                HttpsProvider = httpsProvider ?? UnconfiguredHttpsProvider.Instance,
                ConfigReader = configReader,
                CreateBinding = createBinding
            };

            // reset options. The actual used options and addresses will be populated
            // by the address binding feature
            listenOptions.Clear();
            addresses.Addresses.Clear();

            await strategy.BindAsync(context).ConfigureAwait(false);
        }

        private static IStrategy CreateStrategy(ListenOptions[] listenOptions, ConfigReader configReader, string[] addresses, bool preferAddresses)
        {
            var hasListenOptions = listenOptions.Length > 0;
            var hasConfig = configReader.Endpoints.Any();
            var hasAddresses = addresses.Length > 0;

            if (preferAddresses && hasAddresses)
            {
                if (hasListenOptions)
                {
                    return new OverrideWithAddressesStrategy(addresses);
                }

                return new AddressesStrategy(addresses);
            }
            else if (hasConfig)
            {
                // TODO: Override message
                return new ConfigStrategy();
            }
            else if (hasListenOptions)
            {
                if (hasAddresses)
                {
                    return new OverrideWithEndpointsStrategy(listenOptions, addresses);
                }

                return new EndpointsStrategy(listenOptions);
            }
            else if (hasAddresses)
            {
                // If no endpoints are configured directly using KestrelServerOptions, use those configured via the IServerAddressesFeature.
                return new AddressesStrategy(addresses);
            }
            else
            {
                // "localhost" for both IPv4 and IPv6 can't be represented as an IPEndPoint.
                return new DefaultAddressStrategy();
            }
        }

        /// <summary>
        /// Returns an <see cref="IPEndPoint"/> for the given host an port.
        /// If the host parameter isn't "localhost" or an IP address, use IPAddress.Any.
        /// </summary>
        protected internal static bool TryCreateIPEndPoint(ServerAddress address, out IPEndPoint endpoint)
        {
            if (!IPAddress.TryParse(address.Host, out var ip))
            {
                endpoint = null;
                return false;
            }

            endpoint = new IPEndPoint(ip, address.Port);
            return true;
        }

        internal static async Task BindEndpointAsync(ListenOptions endpoint, AddressBindContext context)
        {
            try
            {
                await context.CreateBinding(endpoint).ConfigureAwait(false);
            }
            catch (AddressInUseException ex)
            {
                throw new IOException(CoreStrings.FormatEndpointAlreadyInUse(endpoint), ex);
            }

            context.ListenOptions.Add(endpoint);
        }

        internal static ListenOptions ParseAddress(string address, out bool https)
        {
            var parsedAddress = ServerAddress.FromUrl(address);
            https = false;

            if (parsedAddress.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
            {
                https = true;
            }
            else if (!parsedAddress.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(CoreStrings.FormatUnsupportedAddressScheme(address));
            }

            if (!string.IsNullOrEmpty(parsedAddress.PathBase))
            {
                throw new InvalidOperationException(CoreStrings.FormatConfigurePathBaseFromMethodCall($"{nameof(IApplicationBuilder)}.UsePathBase()"));
            }

            ListenOptions options = null;
            if (parsedAddress.IsUnixPipe)
            {
                options = new ListenOptions(parsedAddress.UnixPipePath);
            }
            else if (string.Equals(parsedAddress.Host, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                // "localhost" for both IPv4 and IPv6 can't be represented as an IPEndPoint.
                options = new LocalhostListenOptions(parsedAddress.Port);
            }
            else if (TryCreateIPEndPoint(parsedAddress, out var endpoint))
            {
                options = new ListenOptions(endpoint);
            }
            else
            {
                // when address is 'http://hostname:port', 'http://*:port', or 'http://+:port'
                options = new AnyIPListenOptions(parsedAddress.Port);
            }

            return options;
        }

        private interface IStrategy
        {
            Task BindAsync(AddressBindContext context);
        }

        private class DefaultAddressStrategy : IStrategy
        {
            public async Task BindAsync(AddressBindContext context)
            {
                context.Logger.LogDebug(CoreStrings.BindingToDefaultAddress, Constants.DefaultServerAddress);

                await ParseAddress(Constants.DefaultServerAddress, out var https)
                    .BindAsync(context).ConfigureAwait(false);
            }
        }

        private class OverrideWithAddressesStrategy : AddressesStrategy
        {
            public OverrideWithAddressesStrategy(IReadOnlyCollection<string> addresses)
                : base(addresses)
            {
            }

            public override Task BindAsync(AddressBindContext context)
            {
                var joined = string.Join(", ", _addresses);
                context.Logger.LogInformation(CoreStrings.OverridingWithPreferHostingUrls, nameof(IServerAddressesFeature.PreferHostingUrls), joined);

                return base.BindAsync(context);
            }
        }

        private class OverrideWithEndpointsStrategy : EndpointsStrategy
        {
            private readonly string[] _originalAddresses;

            public OverrideWithEndpointsStrategy(IReadOnlyCollection<ListenOptions> endpoints, string[] originalAddresses)
                : base(endpoints)
            {
                _originalAddresses = originalAddresses;
            }

            public override Task BindAsync(AddressBindContext context)
            {
                var joined = string.Join(", ", _originalAddresses);
                context.Logger.LogWarning(CoreStrings.OverridingWithKestrelOptions, joined, "UseKestrel()");

                return base.BindAsync(context);
            }
        }

        private class EndpointsStrategy : IStrategy
        {
            private readonly IReadOnlyCollection<ListenOptions> _endpoints;

            public EndpointsStrategy(IReadOnlyCollection<ListenOptions> endpoints)
            {
                _endpoints = endpoints;
            }

            public virtual async Task BindAsync(AddressBindContext context)
            {
                foreach (var endpoint in _endpoints)
                {
                    await endpoint.BindAsync(context).ConfigureAwait(false);
                }
            }
        }

        private class ConfigStrategy : IStrategy
        {
            public ConfigStrategy()
            {
            }

            public virtual async Task BindAsync(AddressBindContext context)
            {
                foreach (var endpoint in context.ConfigReader.Endpoints)
                {
                    var options = ParseAddress(endpoint.Url, out var https);
                    if (https)
                    {
                        options.KestrelServerOptions = context.ServerOptions;
                        context.HttpsProvider.ConfigureHttps(options, endpoint.CertConfig);
                    }
                    await options.BindAsync(context).ConfigureAwait(false);
                }
            }
        }

        private class AddressesStrategy : IStrategy
        {
            protected readonly IReadOnlyCollection<string> _addresses;

            public AddressesStrategy(IReadOnlyCollection<string> addresses)
            {
                _addresses = addresses;
            }

            public virtual async Task BindAsync(AddressBindContext context)
            {
                foreach (var address in _addresses)
                {
                    var options = ParseAddress(address, out var https);

                    if (https)
                    {
                        options.KestrelServerOptions = context.ServerOptions;
                        context.HttpsProvider.ConfigureHttps(options, null);
                    }

                    await options.BindAsync(context).ConfigureAwait(false);
                }
            }
        }

        private class UnconfiguredHttpsProvider : IHttpsProvider
        {
            public static readonly UnconfiguredHttpsProvider Instance = new UnconfiguredHttpsProvider();

            private UnconfiguredHttpsProvider()
            {
            }

            public void ConfigureHttps(ListenOptions listenOptions, IConfigurationSection certConfig)
            {
                // We have to throw here. If this is called, it's because the user asked for "https" binding but for some
                // reason didn't provide a certificate and didn't use the "HttpsProvider". This means if we no-op,
                // we'll silently downgrade to HTTP, which is bad.
                throw new InvalidOperationException(CoreStrings.UnableToConfigureHttpsBindings);
            }
        }
    }
}
