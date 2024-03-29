﻿using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace dedux
{
    using Dedux;

    class Program
    {
        private const string AppSettingsFileName = "appsettings.json";

        static async Task<int> Main(string[] args)
        {
            Environment.ExitCode = 1;

            Console.WriteLine("File system deduplication utility");
            Console.WriteLine(
                $"DEDUX v.{Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0"} by Kalianov Dmitry (http://mrald.narod.ru). Read README.md for details.");
            Console.WriteLine("Set first argument to specify configuration file (example: dedux.exe c://main.json)");

            var configuration = new DeduxConfiguration();

            ILoggerFactory loggerFactory;

            Console.WriteLine();

            var directoryPath = args.Length > 0 ? Path.GetDirectoryName(args[0]) : Directory.GetCurrentDirectory();
            var jsonFileName = args.Length > 0 ? Path.GetFileName(args[0]) : AppSettingsFileName;

            Console.WriteLine($"App settings file path: {Path.Combine(directoryPath, jsonFileName)}");

            try
            {
                var configurationBuilder = new ConfigurationBuilder()
                    .SetBasePath(directoryPath)
                    .AddJsonFile(jsonFileName, false, false)
                    .AddCommandLine(args);

                var configBuilt = configurationBuilder
                    .Build();

                configBuilt.GetSection("Dedux").Bind(configuration);

                loggerFactory = LoggerFactory.Create((builder) =>
                    {
                        builder.AddConfiguration(configBuilt.GetSection("Logging")).AddSimpleConsole(options =>
                            {
                                options.UseUtcTimestamp = true;
                                options.TimestampFormat = "HH:mm:ss.fff ";
                            });
                    });
            }
            catch (Exception e)
            {
                await Console.Error.WriteLineAsync("Loading settings failed!");
                await Console.Error.WriteLineAsync(e.ToString());
                return 1;
            }

            var service = new DeduxService(loggerFactory.CreateLogger<DeduxService>(), configuration);

            using var cts = configuration.ExecutionTimeout != null
                ? new CancellationTokenSource(configuration.ExecutionTimeout.Value)
                : new CancellationTokenSource();

            using var ss = new SemaphoreSlim(0,1);

            Console.CancelKeyPress += (_, _) =>
            {
                try
                {
                    Console.WriteLine("Cancel sent. Wait for graceful termination.");
                    cts.Cancel();
                    ss.Wait();
                    Console.WriteLine("Service stopped gracefully.");
                }
                catch (Exception)
                {
                }
            };

            try
            {
                await service.RunAsync(cts.Token);
            }
            catch (TaskCanceledException)
            {
                ss.Release();
                return 2;
            }
            catch (Exception e)
            {
                await Console.Error.WriteLineAsync(e.ToString());
                ss.Release();
                return 1;
            }

            ss.Release();
            return 0;
        }
    }
}