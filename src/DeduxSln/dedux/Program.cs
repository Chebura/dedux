using System;
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
        static async Task<int> Main(string[] args)
        {
            Environment.ExitCode = 1;

            Console.WriteLine("File system deduplication utility");
            Console.WriteLine(
                $"DEDUX v.{Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0"} by Kalianov Dmitry (http://mrald.narod.ru). Read README.md for details.");


            var configuration = new DeduxConfiguration();

            ILoggerFactory loggerFactory;

            try
            {
                var configurationBuilder = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", false, false)
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
                Console.Error.WriteLine("Loading settings failed!");
                Console.Error.WriteLine(e.ToString());
                return 1;
            }

            var service = new DeduxService(loggerFactory.CreateLogger<DeduxService>(), configuration);

            using var cts = configuration.ExecutionTimeout != null
                ? new CancellationTokenSource(configuration.ExecutionTimeout.Value)
                : new CancellationTokenSource();

            using var ss = new SemaphoreSlim(0,1);

            Console.CancelKeyPress += (sender, e) =>
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
                Console.Error.WriteLine(e.ToString());
                ss.Release();
                return 1;
            }

            ss.Release();
            return 0;
        }
    }
}