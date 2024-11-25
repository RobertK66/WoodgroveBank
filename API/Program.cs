using Orleans.Configuration;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AsOrleansSilo(silo =>
{
    silo.Configure<SiloOptions>(options =>
        options.SiloName = $"api_{Path.GetFileNameWithoutExtension(Path.GetRandomFileName())}");
});

builder.Services.AddOpenApi();

var app = builder.Build();

app.MapDefaultEndpoints();
app.UseStaticFiles();
app.UseRouting();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment()) {
    app.MapOpenApi();
    app.MapScalarApiReference((x) => { x.Servers = []; });
}

app.MapCustomerEndpoints();
app.MapAccountEndpoints();
app.MapSystemEndpoints();

// run the api
app.Run();

// This record is used to display information about Grains hosted in the Orleans cluster.
public record GrainInfo(string Type, string PrimaryKey, string SiloAddress);

// This record is used to display information about silos in the Orleans cluster.
public record SiloInfo(string SiloName, string SiloAddress);