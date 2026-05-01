using GroundUp.Api;
using GroundUp.Data.Abstractions;
using GroundUp.Data.Postgres;
using GroundUp.Events;
using GroundUp.Sample.Data;
using GroundUp.Sample.Dtos;
using GroundUp.Sample.Repositories;
using GroundUp.Sample.Services;
using GroundUp.Services;
using GroundUp.Services.Settings;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Host=localhost;Port=5432;Database=groundup;Username=groundup;Password=groundup_dev";

// GroundUp framework services
builder.Services.AddGroundUpPostgres<SampleDbContext>(connectionString);
builder.Services.AddGroundUpEvents();
builder.Services.AddGroundUpServices(typeof(Program).Assembly);
builder.Services.AddGroundUpApi();
builder.Services.AddGroundUpSettings();

// Settings seeder
builder.Services.AddScoped<IDataSeeder, DefaultSettingsSeeder>();

// TodoItem — simple pattern (single DTO, base classes handle everything)
builder.Services.AddScoped<IBaseRepository<TodoItemDto>, TodoItemRepository>();
builder.Services.AddScoped<BaseService<TodoItemDto>, TodoItemService>();

// Customer — simple pattern (single DTO, base classes handle everything)
builder.Services.AddScoped<IBaseRepository<CustomerDto>, CustomerRepository>();
builder.Services.AddScoped<BaseService<CustomerDto>, CustomerService>();

// Order — complex pattern (multiple DTOs, custom service/repository methods)
builder.Services.AddScoped<OrderRepository>();
builder.Services.AddScoped<IBaseRepository<OrderListDto>, OrderRepository>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<BaseService<OrderListDto>, OrderService>();

// Project — tenant-scoped pattern (BaseTenantRepository handles isolation)
builder.Services.AddScoped<IBaseRepository<ProjectDto>, ProjectRepository>();
builder.Services.AddScoped<BaseService<ProjectDto>, ProjectService>();

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

public partial class Program { }
