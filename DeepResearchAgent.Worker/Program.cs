using DeepResearchAgent.Engine.Interfaces;
using DeepResearchAgent.Engine.Services;
using DeepResearchAgent.Infrastructure.Persistence;
using DeepResearchAgent.Infrastructure.Persistence.Repositories;
using DeepResearchAgent.Infrastructure.Services.AI;
using DeepResearchAgent.Infrastructure.Services.Analysis;
using DeepResearchAgent.Infrastructure.Services.Crawling;
using DeepResearchAgent.Infrastructure.Services.Planning;
using DeepResearchAgent.Infrastructure.Services.Search;
using DeepResearchAgent.Infrastructure.Services.Verification;
using DeepResearchAgent.Worker;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddScoped<
    IResearchAnalyzer,
    ResearchAnalyzer>();

builder.Services.AddHttpClient<IAIClient, GeminiClient>();

builder.Services.AddScoped<IFactVerifier, FactVerifier>();

builder.Services.AddHttpClient<ICrawler, WebCrawler>(
    client =>
    {
        client.Timeout = TimeSpan.FromSeconds(15);

        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
            "AppleWebKit/537.36 " +
            "(KHTML, like Gecko) " +
            "Chrome/154.0.0.0 Safari/537.36");
    });

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddHttpClient<IResearchSearcher, SearchService>(
    client =>
    {
        client.BaseAddress = new Uri(
            "https://api.tavily.com/");
    });

builder.Services.AddScoped<IResearchRepository, ResearchRepository>();
builder.Services.AddScoped<IResearchService, ResearchService>();
builder.Services.AddScoped<IResearchPlanner, ResearchPlanner>();

builder.Services.AddHostedService<Worker>();

var host = builder.Build();

host.Run();