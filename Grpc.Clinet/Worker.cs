using ConsoleTables;
using Grpc.Net.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
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

        public Worker(ILogger<Worker> logger)
        {
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var stopwatch = new Stopwatch();
            var httpHandler = new BenchmarkHttpHandler(stopwatch, new HttpClientHandler());
            var returnCount = 10000;
            var benchMarks = new List<BenchMarks>();
            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
                await Task.Delay(1000, stoppingToken);
                var httpClient = new HttpClient(httpHandler)
                {
                    BaseAddress = new Uri("https://localhost:5004")
                };


                Console.WriteLine("Calling Web API");
                stopwatch.Start();
                var jsonForecasts = await httpClient.GetFromJsonAsync<WeatherForecastViewModel[]>("/weatherforecast?returnCount=" + returnCount);
                benchMarks.Add(new BenchMarks
                {
                    Client = "WebAPI",
                    DataSize = Math.Round((double)(httpHandler.BytesRead!.Value) / 1024, 2),
                    NetworkTime = Math.Round(httpHandler.HeadersReceivedElapsed!.Value.TotalSeconds, 2),
                    DeserializationTime = Math.Round(stopwatch.Elapsed.TotalSeconds - httpHandler.HeadersReceivedElapsed!.Value.TotalSeconds, 2)
                });
                stopwatch.Reset();

                Console.WriteLine("Done Web API");
                var grpcNet3Client = CreateClient("https://localhost:5001", httpHandler);
                Console.WriteLine("Calling Grpc 3");
                stopwatch.Start();
                var grpcForecastsMet3 = (await grpcNet3Client.GetWeatherForecastsAsync(new GetWeatherForecastsRequest { ReturnCount = returnCount })).Forecasts;
                benchMarks.Add(new BenchMarks
                {
                    Client = "Grpc Net 3.1",
                    DataSize = Math.Round((double)(httpHandler.BytesRead!.Value) / 1024, 2),
                    NetworkTime = Math.Round(httpHandler.HeadersReceivedElapsed!.Value.TotalSeconds, 2),
                    DeserializationTime = Math.Round(stopwatch.Elapsed.TotalSeconds - httpHandler.HeadersReceivedElapsed!.Value.TotalSeconds, 2)
                });
                stopwatch.Reset();
                Console.WriteLine("Done Grpc 3");

                var grpcNet5Client = CreateClient("https://localhost:5002", httpHandler);
                Console.WriteLine("Calling Grpc 5");
                stopwatch.Start();
                var grpcForecastsNet5 = (await grpcNet3Client.GetWeatherForecastsAsync(new GetWeatherForecastsRequest { ReturnCount = returnCount })).Forecasts;
                benchMarks.Add(new BenchMarks
                {
                    Client = "Grpc Net 5",
                    DataSize = Math.Round((double)(httpHandler.BytesRead!.Value) / 1024, 2),
                    NetworkTime = Math.Round(httpHandler.HeadersReceivedElapsed!.Value.TotalSeconds, 2),
                    DeserializationTime = Math.Round(stopwatch.Elapsed.TotalSeconds - httpHandler.HeadersReceivedElapsed!.Value.TotalSeconds, 2)
                });
                stopwatch.Reset();
                Console.WriteLine("Done Grpc 5");
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

        private WeatherForecasts.WeatherForecastsClient CreateClient(string address, HttpMessageHandler handler)
        {
            var channel = GrpcChannel.ForAddress(address, new GrpcChannelOptions
            {
                HttpHandler = handler,
                MaxReceiveMessageSize = null
            });

            return new WeatherForecasts.WeatherForecastsClient(channel);
        }
    }

    public class WeatherForecastViewModel
    {
        public DateTime Date { get; set; }
        public int TemperatureC { get; set; }
        public string? Summary { get; set; }
        public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
    }

    public class BenchMarks
    {
        public double DataSize { get; set; }
        public double NetworkTime { get; set; }
        public double DeserializationTime { get; set; }
        public double RenderingTime { get; set; }
        public string Client { get; set; }
    }

    public class BenchmarkHttpHandler : DelegatingHandler
    {
        private readonly Stopwatch _stopwatch;
        private CaptureResponseLengthContent? _content;

        public TimeSpan? HeadersReceivedElapsed { get; private set; }
        public int? BytesRead => _content?.BytesRead;

        public BenchmarkHttpHandler(Stopwatch stopwatch, HttpMessageHandler inner) : base(inner)
        {
            _stopwatch = stopwatch;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HeadersReceivedElapsed = null;

            var response = await base.SendAsync(request, cancellationToken);

            _content = new CaptureResponseLengthContent(response.Content);
            response.Content = _content;

            HeadersReceivedElapsed = _stopwatch.Elapsed;

            return response;
        }

        private class CaptureResponseLengthContent : HttpContent
        {
            private readonly HttpContent _inner;
            private CaptureResponseLengthStream? _innerStream;

            public int? BytesRead => _innerStream?.BytesRead;

            public CaptureResponseLengthContent(HttpContent inner)
            {
                _inner = inner;

                foreach (var header in inner.Headers)
                {
                    Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            {
                throw new NotImplementedException();
            }

            protected override async Task<Stream> CreateContentReadStreamAsync()
            {
                var stream = await _inner.ReadAsStreamAsync();
                _innerStream = new CaptureResponseLengthStream(stream);

                return _innerStream;
            }

            protected override bool TryComputeLength(out long length)
            {
                throw new NotImplementedException();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _inner.Dispose();
                    _innerStream?.Dispose();
                }

                base.Dispose(disposing);
            }
        }

        private class CaptureResponseLengthStream : Stream
        {
            private readonly Stream _inner;
            public int BytesRead { get; private set; }

            public CaptureResponseLengthStream(Stream inner)
            {
                _inner = inner;
            }

            public override bool CanRead => _inner.CanRead;
            public override bool CanSeek => _inner.CanSeek;
            public override bool CanWrite => _inner.CanWrite;
            public override long Length => _inner.Length;
            public override long Position
            {
                get => _inner.Position;
                set => _inner.Position = value;
            }

            public override void Flush() => _inner.Flush();

            public override int Read(byte[] buffer, int offset, int count)
            {
                var readCount = _inner.Read(buffer, offset, count);
                BytesRead += readCount;
                return readCount;
            }

            public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

            public override void SetLength(long value) => _inner.SetLength(value);

            public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

            public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                var readCount = await _inner.ReadAsync(buffer, offset, count, cancellationToken);
                BytesRead += readCount;
                return readCount;
            }

            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                var readCount = await _inner.ReadAsync(buffer, cancellationToken);
                BytesRead += readCount;
                return readCount;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _inner.Dispose();
                }
            }
        }
    }

}
