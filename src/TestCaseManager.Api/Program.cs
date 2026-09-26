using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TestCaseManager.Api.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Keep local database files under the API's application directory.
var dataDirectory = Path.Combine(
    builder.Environment.ContentRootPath,
    "App_Data");

Directory.CreateDirectory(dataDirectory);

var databasePath = Path.Combine(
    dataDirectory,
    "testcasemanager.db");

// Build a connection string safely from the database file path.
var connectionString = new SqliteConnectionStringBuilder
{
    DataSource = databasePath,
    ForeignKeys = true
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

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
