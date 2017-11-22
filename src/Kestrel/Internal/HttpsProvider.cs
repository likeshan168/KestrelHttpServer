// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Certificates.Generation;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal
{
    public class HttpsProvider : IHttpsProvider
    {
        private static readonly CertificateManager _certificateManager = new CertificateManager();

        private readonly ILogger<HttpsProvider> _logger;

        public HttpsProvider(ILogger<HttpsProvider> logger)
        {
            _logger = logger;
        }

        public void ConfigureHttps(ListenOptions listenOptions, IConfigurationSection certConfig)
        {
            var certInfo = new CertificateConfig(certConfig);
            if (certInfo.Exists)
            {
                // TODO: Other patterns like cert store
                listenOptions.UseHttps(certInfo.Path, certInfo.Password);
            }
            else
            {
                UseDefaultCert(listenOptions);
            }
        }

        private void UseDefaultCert(ListenOptions listenOptions)
        {
            var certificate = _certificateManager.ListCertificates(CertificatePurpose.HTTPS, StoreName.My, StoreLocation.CurrentUser, isValid: true)
                .FirstOrDefault();
            if (certificate != null)
            {
                _logger.LocatedDevelopmentCertificate(certificate);
                listenOptions.UseHttps(certificate);
            }
            else
            {
                _logger.UnableToLocateDevelopmentCertificate();
                throw new InvalidOperationException(KestrelStrings.HttpsUrlProvidedButNoDevelopmentCertificateFound);
            }
        }

        internal class CertificateConfig
        {
            public CertificateConfig(IConfigurationSection configSection)
            {
                ConfigSection = configSection;
            }

            public IConfigurationSection ConfigSection { get; }

            public bool Exists => ConfigSection?.GetChildren().Any() ?? false;

            public string Id => ConfigSection?.Key;

            public string Path => ConfigSection?["Path"];

            public string Password => ConfigSection?["Password"];

        }
    }
}
