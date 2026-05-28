using Grpc.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Tinkwell.Runlet.Firmwareless.AssetRegistry;
using Tinkwell.Runlet.Firmwareless.AssetRegistry.Configuration;
using Tinkwell.Runlet.Firmwareless.AssetRegistry.Data;

var builder = WebApplication.CreateBuilder(args);

var options = new AssetRegistryOptions
{
    DataDir = builder.Configuration["AssetRegistry:DataDir"]
              ?? Environment.GetEnvironmentVariable("TW_ASSET_REGISTRY_DATA_DIR")
              ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                  "tinkwell", "asset-registry"),
};

builder.Services.AddSingleton(options);

Directory.CreateDirectory(options.DataDir);
var dbPath = Path.Combine(options.DataDir, "assets.db");
var db = new AssetDatabase(dbPath);
builder.Services.AddSingleton(db);

builder.Services.AddGrpc();

var app = builder.Build();
app.MapGrpcService<AssetRegistryGrpcService>();
await app.RunAsync();
