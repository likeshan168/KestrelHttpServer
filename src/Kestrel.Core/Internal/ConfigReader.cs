﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal
{
    internal class ConfigReader
    {
        private IConfiguration _configuration;
        private IList<EndpointConfig> _endpoints;

        public ConfigReader(IConfiguration configuration)
        {
            // May be null
            _configuration = configuration;
        }

        public IEnumerable<EndpointConfig> Endpoints
        {
            get
            {
                if (_endpoints == null)
                {
                    ReadEndpoints();
                }

                return _endpoints;
            }
        }

        private void ReadEndpoints()
        {
            _endpoints = new List<EndpointConfig>();

            if (_configuration == null)
            {
                return;
            }

            var endpointsConfig = _configuration.GetSection("Endpoints").GetChildren();
            foreach (var endpointConfig in endpointsConfig)
            {
                /*
                 "EndpointName": {
                    "Url": "https://*:5463",
                    "Certificate": {
                        "Path": "testCert.pfx",
                        "Password": "testPassword"
                    }
                }
                */
                var url = endpointConfig["Url"];
                if (string.IsNullOrEmpty(url))
                {
                    // TODO: Log?
                    continue;
                }

                var endpoint = new EndpointConfig()
                {
                    Name = endpointConfig.Key,
                    Url = url,
                    CertificateSection = endpointConfig.GetSection("Certificate"),
                };
                _endpoints.Add(endpoint);
            }
        }
    }

    internal class EndpointConfig
    {
        public string Name { get; set; }
        public string Url { get; set; }
        public IConfiguration CertificateSection { get; set; }
    }
}
