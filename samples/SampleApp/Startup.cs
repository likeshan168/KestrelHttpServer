// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.Internal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SampleApp
{
    public class Startup
    {
        public void Configure(IApplicationBuilder app, ILoggerFactory loggerFactory)
        {
            var logger = loggerFactory.CreateLogger("Default");

            app.Run(async context =>
            {
                var connectionFeature = context.Connection;
                logger.LogDebug($"Peer: {connectionFeature.RemoteIpAddress?.ToString()}:{connectionFeature.RemotePort}"
                    + $"{Environment.NewLine}"
                    + $"Sock: {connectionFeature.LocalIpAddress?.ToString()}:{connectionFeature.LocalPort}");

                var response = $"hello, world{Environment.NewLine}";
                context.Response.ContentLength = response.Length;
                context.Response.ContentType = "text/plain";
                await context.Response.WriteAsync(response);
            });
        }

        public static Task Main(string[] args)
        {
            TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                Console.WriteLine("Unobserved exception: {0}", e.Exception);
            };

            var hostBuilder = new WebHostBuilder()
                .ConfigureLogging((_, factory) =>
                {
                    factory.AddConsole();
                })
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    var env = hostingContext.HostingEnvironment;
                    config.AddJsonFile("appsettings.json", optional: true)
                          .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true);
                })
                .UseKestrel((context, options) =>
                {
                    ShowConfig(context.Configuration);

                    options.ConfigureEndpointDefaults(opt =>
                    {
                        opt.Protocols = HttpProtocols.Http1;
                    });

                    options.ConfigureEndpoint("NamedEndpoint", opt =>
                    {
                        opt.Protocols = HttpProtocols.Http1;
                    });

                    var basePort = context.Configuration.GetValue<int?>("BASE_PORT") ?? 5000;

                    options.Configuration = context.Configuration.GetSection("Kestrel");

                    // Run callbacks on the transport thread
                    options.ApplicationSchedulingMode = SchedulingMode.Inline;
                    /*
                    options.ConfigureEndpointDefaults(listenOptions =>
                    {
                        listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
                    });

                    options.ConfigureHttpsDefaults(httpsOptions =>
                    {
                        httpsOptions.ClientCertificateMode = ClientCertificateMode.AllowCertificate;
                    });

                    options.ConfigureEndpoint("FromConfig", listenOptions =>
                    {
                        listenOptions.Protocols = HttpProtocols.Http2;
                    });

                    options.ConfigureEndpoint("FromConfig", listenOptions =>
                    {
                        listenOptions.Protocols = HttpProtocols.Http1;
                    },
                    httpsOptions =>
                    {
                        httpsOptions.SslProtocols = SslProtocols.Tls12;
                    });

                    // Overload of ConfigureEndpoint without listenOptions.
                    options.ConfigureEndpointHttps("FromConfig",  httpsOptions =>
                    {
                        httpsOptions.SslProtocols = SslProtocols.Tls12;
                    });
                    */
                    options.Listen(IPAddress.Loopback, basePort, listenOptions =>
                    {
                        // Uncomment the following to enable Nagle's algorithm for this endpoint.
                        //listenOptions.NoDelay = false;

                        listenOptions.UseConnectionLogging();
                    });

                    options.Listen(IPAddress.Loopback, basePort + 1, listenOptions =>
                    {
                        listenOptions.UseHttps("testCert.pfx", "testPassword");
                        listenOptions.UseConnectionLogging();
                    });

                    options.ListenLocalhost(basePort + 2, listenOptions =>
                    {
                        listenOptions.UseHttps("testCert.pfx", "testPassword");
                    });

                    options.ListenAnyIP(basePort + 3);

                    options.UseSystemd();

                    // The following section should be used to demo sockets
                    //options.ListenUnixSocket("/tmp/kestrel-test.sock");
                })
                .UseContentRoot(Directory.GetCurrentDirectory())
                .UseStartup<Startup>();

            if (string.Equals(Process.GetCurrentProcess().Id.ToString(), Environment.GetEnvironmentVariable("LISTEN_PID")))
            {
                // Use libuv if activated by systemd, since that's currently the only transport that supports being passed a socket handle.
                hostBuilder.UseLibuv(options =>
                 {
                     // Uncomment the following line to change the default number of libuv threads for all endpoints.
                     // options.ThreadCount = 4;
                 });
            }
                
            return hostBuilder.Build().RunAsync();
        }

        private static void ShowConfig(IConfiguration config)
        {
            foreach (var pair in config.GetChildren())
            {
                Console.WriteLine($"{pair.Path} - {pair.Value}");
                ShowConfig(pair);
            }
        }
    }
}