using GroundUp.Api;
using GroundUp.Data.Abstractions;
using GroundUp.Data.Postgres;
using GroundUp.Events;
using GroundUp.Sample.Data;
using GroundUp.Sample.Dtos;
using GroundUp.Sample.Repositories;
using GroundUp.Sample.Services;
using GroundUp.Services;

var builder = WebApplication.CreateBuilder(args);

// Connection string from configuration or default for local dev
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Host=localhost;Port=5432;Database=groundup;Username=groundup;Password=groundup_dev";

// GroundUp framework services
builder.Services.AddGroundUpPostgres<SampleDbContext>(connectionString);
builder.Services.AddGroundUpEvents();
builder.Services.AddGroundUpServices(typeof(Program).Assembly);
builder.Services.AddGroundUpApi();

// Register sample app services
builder.Services.AddScoped<IBaseRepository<TodoItemDto>, TodoItemRepository>();
builder.Services.AddScoped<BaseService<TodoItemDto>, TodoItemService>();

// ASP.NET Core services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Health checks
builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString, name: "postgres");

var app = builder.Build();

// Middleware pipeline
app.UseGroundUpMiddleware();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
