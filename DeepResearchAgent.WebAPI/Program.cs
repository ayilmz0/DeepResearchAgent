using DeepResearchAgent.Engine.Interfaces;
using DeepResearchAgent.Engine.Services;
using DeepResearchAgent.Infrastructure.Persistence;
using DeepResearchAgent.Infrastructure.Persistence.Repositories;
using DeepResearchAgent.Infrastructure.Services.Crawling;
using DeepResearchAgent.Infrastructure.Services.Planning;
using DeepResearchAgent.Infrastructure.Services.Search;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddHttpClient<ICrawler, WebCrawler>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);

    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "DeepResearchAgent/1.0");
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy
            .WithOrigins("http://localhost:50163")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddHttpClient<IResearchSearcher, SearchService>(
    client =>
    {
        client.BaseAddress = new Uri(
            "https://api.tavily.com/");
    });

builder.Services.AddScoped<IResearchRepository, ResearchRepository>();
builder.Services.AddScoped<IResearchService, ResearchService>();
builder.Services.AddScoped<IResearchPlanner, ResearchPlanner>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("Frontend");

app.MapControllers();

app.Run();