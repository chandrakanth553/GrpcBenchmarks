using Grpc.Net.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Net.Http;
using Weather;

namespace Grpc.Clinet
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddHostedService<Worker>(x =>
                    {
                        var stopwatch = new Stopwatch();
                        var httpHandler = new BenchmarkHttpHandler(stopwatch, new HttpClientHandler());
                        var httpClient = new HttpClient(httpHandler)
                        {
                            BaseAddress = new Uri("https://localhost:5004")
                        };
                        WeatherForecasts.WeatherForecastsClient CreateClient(string address, HttpMessageHandler handler)
                        {
                            var channel = GrpcChannel.ForAddress(address, new GrpcChannelOptions
                            {
                                HttpHandler = handler,
                                MaxReceiveMessageSize = null
                            });

                            return new WeatherForecasts.WeatherForecastsClient(channel);
                        }
                        Func<string, Stopwatch, BenchMarks> benchMarks = (string clinetName, Stopwatch _stopwatch) => new BenchMarks
                        {
                            Client = clinetName,
                            DataSize = Math.Round((double)(httpHandler.BytesRead!.Value) / 1024, 2),
                            NetworkTime = Math.Round(httpHandler.HeadersReceivedElapsed!.Value.TotalSeconds, 2),
                            DeserializationTime = Math.Round(_stopwatch.Elapsed.TotalSeconds - httpHandler.HeadersReceivedElapsed!.Value.TotalSeconds, 2)
                        };
                        return new Worker(x.GetService<ILogger<Worker>>(), httpClient, CreateClient("https://localhost:5001", httpHandler), CreateClient("https://localhost:5002", httpHandler), stopwatch, benchMarks);
                    });
                });

    }
}
