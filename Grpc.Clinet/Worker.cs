using ConsoleTables;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Weather;

namespace Grpc.Clinet
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly HttpClient _httpClient;
        private readonly WeatherForecasts.WeatherForecastsClient _weatherForecastsClientNet3;
        private readonly WeatherForecasts.WeatherForecastsClient _weatherForecastsClientNet5;
        private readonly Stopwatch _stopwatch;
        private readonly Func<string, Stopwatch, BenchMarks> _benchMarks;

        public Worker(ILogger<Worker> logger, HttpClient httpClient,
            WeatherForecasts.WeatherForecastsClient weatherForecastsClientNet3,
            WeatherForecasts.WeatherForecastsClient weatherForecastsClientNet5,
            Stopwatch stopwatch,
            Func<string, Stopwatch, BenchMarks> benchMarks)
        {
            _logger = logger;
            _httpClient = httpClient;
            _weatherForecastsClientNet3 = weatherForecastsClientNet3;
            _weatherForecastsClientNet5 = weatherForecastsClientNet5;
            _stopwatch = stopwatch;
            _benchMarks = benchMarks;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var returnCount = 10000;
            var benchMarks = new List<BenchMarks>();
            while (!stoppingToken.IsCancellationRequested)
            {
                _stopwatch.Start();
                var jsonForecasts = await _httpClient.GetFromJsonAsync<WeatherForecastViewModel[]>("/weatherforecast?returnCount=" + returnCount);
                benchMarks.Add(_benchMarks("WebAPI", _stopwatch));
                _stopwatch.Reset();

                _stopwatch.Start();
                var grpcForecastsMet3 = (await _weatherForecastsClientNet3.GetWeatherForecastsAsync(new GetWeatherForecastsRequest { ReturnCount = returnCount })).Forecasts;
                benchMarks.Add(_benchMarks("Grpc Net 3.1", _stopwatch));
                _stopwatch.Reset();

                _stopwatch.Start();
                var grpcForecastsNet5 = (await _weatherForecastsClientNet5.GetWeatherForecastsAsync(new GetWeatherForecastsRequest { ReturnCount = returnCount })).Forecasts;
                benchMarks.Add(_benchMarks("Grpc Net 5", _stopwatch));
                _stopwatch.Reset();

                Console.WriteLine("");
                Console.WriteLine("");
                Console.WriteLine("");
                var table = new ConsoleTable("Client", "Data Size (kilobytes)", "Network Time (seconds)", "Deserialization Time (seconds)");
                foreach (var bench in benchMarks)
                {
                    table.AddRow(bench.Client, bench.DataSize, bench.NetworkTime, bench.DeserializationTime);
                }
                table.Write(Format.Minimal);
                Console.Read();
            }
        }
    }
}
