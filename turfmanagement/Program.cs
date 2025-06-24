using turfmanagement.Connection;

var builder = WebApplication.CreateBuilder(args);

// Register DatabaseConnection for DI
builder.Services.AddSingleton<DatabaseConnection>();

// Register IMemoryCache for caching OTPs
builder.Services.AddMemoryCache();

// Add other services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Turf Management API",
        Version = "v1",
        Description = "API for managing turf bookings and slots"
    });
});
builder.Services.AddHttpClient();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAllOrigins", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Static files middleware (serve static files like HTML, JS, etc.)
app.UseStaticFiles();

app.UseCors("AllowAllOrigins");  // Apply CORS policy here

app.UseAuthorization();

app.MapControllers();

app.Run();
