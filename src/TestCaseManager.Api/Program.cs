using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TestCaseManager.Api.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Keep local database files under the API's application directory.
var dataDirectory = builder.Configuration["Storage:Directory"] ?? Path.Combine(
    builder.Environment.ContentRootPath, "App_Data");

Directory.CreateDirectory(dataDirectory);

var databasePath = Path.Combine(
    dataDirectory,
    "testcasemanager.db");

// Build a connection string safely from the database file path.
var connectionString = new SqliteConnectionStringBuilder
{
    DataSource = databasePath,
    ForeignKeys = true,
    Pooling = false
}.ToString();

// Makes AppDbContext available through dependency injection.
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(connectionString));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// The local Angular proxy uses HTTP on loopback. Keep HTTPS for non-development hosting.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler();
    app.UseHttpsRedirection();
}

app.UseAuthorization();

app.MapControllers();

app.Run();

// Makes the entry point available to HTTP integration tests.
public partial class Program { }
