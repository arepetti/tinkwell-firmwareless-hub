using System.Globalization;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Tinkwell.Runlet.Firmwareless.AssetRegistry.Data;
using Tinkwell.Runlet.Firmwareless.AssetRegistry.Proto;

namespace Tinkwell.Runlet.Firmwareless.AssetRegistry;

public sealed class AssetRegistryGrpcService : AssetRegistryService.AssetRegistryServiceBase
{
    private readonly AssetDatabase _db;
    private readonly ILogger<AssetRegistryGrpcService> _logger;

    public AssetRegistryGrpcService(AssetDatabase db, ILogger<AssetRegistryGrpcService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public override async Task<RegisterAssetReply> RegisterAsset(
        RegisterAssetRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.Id, out var id))
        {
            return new RegisterAssetReply { Success = false, Error = "Invalid asset id (expected GUID)." };
        }

        try
        {
            var existing = await _db.GetAssetAsync(id);
            var createdAt = existing?.CreatedAt ?? DateTimeOffset.UtcNow;

            var firmletUpdated = existing is not null &&
                !string.Equals(existing.FirmletVersion, request.FirmletVersion, StringComparison.Ordinal);

            var asset = new Asset
            {
                Id = id,
                DisplayName = string.IsNullOrEmpty(request.DisplayName) ? null : request.DisplayName,
                VendorId = request.VendorId == 0 ? null : request.VendorId,
                ProductId = request.ProductId == 0 ? null : request.ProductId,
                Variant = request.Variant.IsEmpty ? null : request.Variant.ToByteArray(),
                FirmwareVersion = string.IsNullOrEmpty(request.FirmwareVersion) ? null : request.FirmwareVersion,
                CommunicationMode = ParseCommunicationMode(request.CommunicationMode),
                FirmletName = string.IsNullOrEmpty(request.FirmletName) ? null : request.FirmletName,
                FirmletVersion = string.IsNullOrEmpty(request.FirmletVersion) ? null : request.FirmletVersion,
                FirmletInitialized = firmletUpdated ? false : (existing?.FirmletInitialized ?? false),
                State = existing?.State ?? AssetState.Created,
                CreatedAt = createdAt,
                LastHeartbeat = existing?.LastHeartbeat,
                LastError = existing?.LastError,
            };

            await _db.UpsertAssetAsync(asset);
            return new RegisterAssetReply { Success = true };
        }
        catch (OutOfMemoryException) { Environment.FailFast("Out of memory"); throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RegisterAsset failed for {AssetId}", request.Id);
            return new RegisterAssetReply { Success = false, Error = ex.Message };
        }
    }

    public override async Task<RemoveAssetReply> RemoveAsset(RemoveAssetRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.Id, out var id))
            return new RemoveAssetReply { Success = false };

        var removed = await _db.DeleteAssetAsync(id);
        return new RemoveAssetReply { Success = removed };
    }

    public override async Task<GetAssetReply> GetAsset(GetAssetRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.Id, out var id))
            return new GetAssetReply { Found = false };

        var asset = await _db.GetAssetAsync(id);
        if (asset is null)
            return new GetAssetReply { Found = false };

        return new GetAssetReply { Found = true, Asset = ToAssetInfo(asset) };
    }

    public override async Task<ListAssetsReply> ListAssets(Empty request, ServerCallContext context)
    {
        var assets = await _db.ListAssetsAsync();
        var reply = new ListAssetsReply();
        foreach (var a in assets)
            reply.Assets.Add(ToAssetInfo(a));
        return reply;
    }

    public override async Task<UpdateAssetStateReply> UpdateAssetState(
        UpdateAssetStateRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.Id, out var id))
            return new UpdateAssetStateReply { Success = false };

        if (!System.Enum.TryParse<AssetState>(request.State, ignoreCase: true, out var state))
            return new UpdateAssetStateReply { Success = false };

        await _db.UpdateAssetStateAsync(id, state,
            string.IsNullOrEmpty(request.Error) ? null : request.Error);

        return new UpdateAssetStateReply { Success = true };
    }

    public override async Task<RecordHeartbeatReply> RecordHeartbeat(
        RecordHeartbeatRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.AssetId, out var id))
            return new RecordHeartbeatReply { Success = false };

        var before = await _db.GetAssetAsync(id);
        if (before is null)
            return new RecordHeartbeatReply { Success = false };

        await _db.RecordHeartbeatAsync(id);
        return new RecordHeartbeatReply { Success = true };
    }

    public override async Task<GetAssetPermissionsReply> GetAssetPermissions(
        GetAssetPermissionsRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.AssetId, out var id))
            return new GetAssetPermissionsReply();

        var rows = await _db.GetPermissionsAsync(id);
        var reply = new GetAssetPermissionsReply();

        foreach (var row in rows)
        {
            var type = row.PermissionType;
            var target = row.Target ?? "";

            if (string.Equals(type, "measure", StringComparison.OrdinalIgnoreCase))
            {
                reply.AllowedMeasures.Add(target);
                continue;
            }

            if (string.Equals(type, "service", StringComparison.OrdinalIgnoreCase))
            {
                reply.AllowedServices.Add(target);
                continue;
            }

            reply.Permissions.Add(string.IsNullOrEmpty(target) ? type : $"{type}:{target}");
        }

        return reply;
    }

    public override async Task<GetPendingCommandCountReply> GetPendingCommandCount(
        GetPendingCommandCountRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.AssetId, out var id))
            return new GetPendingCommandCountReply { Count = 0 };

        var count = await _db.GetPendingCommandCountAsync(id);
        return new GetPendingCommandCountReply { Count = count };
    }

    public override async Task<EnqueueCommandReply> EnqueueCommand(
        EnqueueCommandRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.AssetId, out var id))
            return new EnqueueCommandReply { Success = false };

        var asset = await _db.GetAssetAsync(id);
        if (asset is null)
            return new EnqueueCommandReply { Success = false };

        await _db.EnqueueCommandAsync(id, request.CmdType, request.Payload.ToByteArray());
        return new EnqueueCommandReply { Success = true };
    }

    public override async Task<DequeueCommandsReply> DequeueCommands(
        DequeueCommandsRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.AssetId, out var id))
            return new DequeueCommandsReply();

        var max = request.MaxCount > 0 ? request.MaxCount : 10;
        var entries = await _db.DequeueCommandsAsync(id, max);

        var reply = new DequeueCommandsReply();
        foreach (var e in entries)
        {
            reply.Commands.Add(new CommandInfo
            {
                Id = e.Id,
                AssetId = e.AssetId.ToString(),
                CmdType = e.CommandType,
                Payload = Google.Protobuf.ByteString.CopyFrom(e.Payload),
                CreatedAt = FormatTimestamp(e.CreatedAt),
            });
        }

        return reply;
    }

    private static AssetInfo ToAssetInfo(Asset a)
    {
        var info = new AssetInfo
        {
            Id = a.Id.ToString(),
            DisplayName = a.DisplayName ?? "",
            VendorId = a.VendorId ?? 0,
            ProductId = a.ProductId ?? 0,
            FirmwareVersion = a.FirmwareVersion ?? "",
            CommunicationMode = ToProtoCommunicationMode(a.CommunicationMode),
            FirmletName = a.FirmletName ?? "",
            FirmletVersion = a.FirmletVersion ?? "",
            State = a.State.ToString().ToLowerInvariant(),
            CreatedAt = FormatTimestamp(a.CreatedAt),
            LastError = a.LastError ?? "",
        };

        if (a.Variant is { Length: > 0 })
            info.Variant = Google.Protobuf.ByteString.CopyFrom(a.Variant);

        if (a.LastHeartbeat.HasValue)
            info.LastHeartbeat = FormatTimestamp(a.LastHeartbeat.Value);

        return info;
    }

    private static string FormatTimestamp(DateTimeOffset dt) =>
        dt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static CommunicationMode ParseCommunicationMode(string? s) => s?.ToLowerInvariant() switch
    {
        "always-on" => CommunicationMode.AlwaysOn,
        "mailbox" => CommunicationMode.Mailbox,
        _ => CommunicationMode.ServiceOnly,
    };

    private static string ToProtoCommunicationMode(CommunicationMode m) => m switch
    {
        CommunicationMode.AlwaysOn => "always-on",
        CommunicationMode.Mailbox => "mailbox",
        _ => "service-only",
    };
}