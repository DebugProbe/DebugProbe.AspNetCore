using DebugProbe.AspNetCore.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddDebugProbe(options =>
{
    options.MaxEntries = 10;
    options.RedactedHeaders = [.. options.RedactedHeaders, "X-Api-Key"];
    options.AllowRedactionPreview = false;
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

app.MapControllers();

app.Run();
