using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Tinkwell.Firmwareless.Hosting.Ipc.Proto;
using Tinkwell.Runlet.Firmwareless.CoAP.Configuration;
using MeasuresClient = Tinkwell.Runlet.Measures.Grpc.Measures.MeasuresClient;
using Tinkwell.Runlet.Measures.Grpc;

namespace Tinkwell.Runlet.Firmwareless.CoAP;

/// <summary>
/// Bridges device sensor readings to the Tinkwell measures system. Each
/// reading is written as a namespaced measure: <c>firmwareless/{assetId}/{sensorName}</c>.
/// Measures are auto-registered on first write.
/// </summary>
public sealed class MeasureBridge : IDisposable
{
    private readonly MeasuresClient? _client;
    private readonly GrpcChannel? _channel;
    private readonly ILogger<MeasureBridge> _logger;
    private readonly HashSet<string> _registered = new(StringComparer.Ordinal);

    public MeasureBridge(CoapRunletOptions options, ILogger<MeasureBridge> logger)
    {
        _logger = logger;

        if (!string.IsNullOrEmpty(options.MeasuresAddress))
        {
            _channel = GrpcChannel.ForAddress(options.MeasuresAddress);
            _client = new MeasuresClient(_channel);
        }
    }

    public bool IsEnabled => _client is not null;

    public async Task WriteTelemetryAsync(string assetId, DeviceTelemetry telemetry, CancellationToken ct)
    {
        if (_client is null)
            return;

        foreach (var reading in telemetry.Readings)
        {
            var name = $"firmwareless/{assetId}/{reading.Name}";
            await EnsureRegisteredAsync(name, reading.Name, ct);

            try
            {
                await _client.UpdateAsync(new UpdateMeasureRequest
                {
                    Name = name,
                    Value = new MeasureValueProto
                    {
                        Type = "float",
                        NumericValue = reading.Value,
                    },
                }, cancellationToken: ct);
            }
            catch (OutOfMemoryException) { Environment.FailFast("Out of memory"); throw; }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to update measure {Name}", name);
            }
        }
    }

    private async Task EnsureRegisteredAsync(string name, string sensorName, CancellationToken ct)
    {
        if (_registered.Contains(name))
            return;

        try
        {
            await _client!.RegisterAsync(new RegisterMeasureRequest
            {
                Definition = new MeasureDefinitionProto
                {
                    Name = name,
                    Type = "float",
                },
                Metadata = new MeasureMetadataProto
                {
                    Description = $"Firmwareless device sensor: {sensorName}",
                    Category = "firmwareless",
                },
            }, cancellationToken: ct);

            _registered.Add(name);
        }
        catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.AlreadyExists)
        {
            _registered.Add(name);
        }
        catch (OutOfMemoryException) { Environment.FailFast("Out of memory"); throw; }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to register measure {Name}; will write anyway", name);
            _registered.Add(name);
        }
    }

    public void Dispose() => _channel?.Dispose();
}