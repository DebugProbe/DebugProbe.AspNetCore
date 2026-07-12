using DebugProbe.AspNetCore.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddDebugProbe(options =>
{
    options.MaxEntries = builder.Configuration.GetValue<int>("DebugProbe:MaxEntries");
    options.AllowUiInProduction = builder.Configuration.GetValue<bool>("DebugProbe:AllowUiInProduction");
    options.ServerUrl = builder.Configuration["DebugProbe:ServerUrl"];
    options.ApplicationId = builder.Configuration["DebugProbe:ApplicationId"];
    options.ApplicationName = builder.Configuration["DebugProbe:ApplicationName"];
    options.InstanceId = builder.Configuration["DebugProbe:InstanceId"];
});


var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseDebugProbe();

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapGet("/delay/{milliseconds}", async (int milliseconds) =>
{
    await Task.Delay(milliseconds);
    return Results.Ok(new { delayedMs = milliseconds });
});

app.Run();
