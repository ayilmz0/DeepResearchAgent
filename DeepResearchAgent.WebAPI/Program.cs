using DeepResearchAgent.Engine.Interfaces;
using DeepResearchAgent.Engine.Services;
using DeepResearchAgent.Engine.Services.Analysis;
using DeepResearchAgent.Infrastructure.Persistence;
using DeepResearchAgent.Infrastructure.Persistence.Repositories;
using DeepResearchAgent.Infrastructure.Services;
using DeepResearchAgent.Infrastructure.Services.AI;
using DeepResearchAgent.Infrastructure.Services.Analysis;
using DeepResearchAgent.Infrastructure.Services.Crawling;
using DeepResearchAgent.Infrastructure.Services.Planning;
using DeepResearchAgent.Infrastructure.Services.Reporting;
using DeepResearchAgent.Infrastructure.Services.Search;
using DeepResearchAgent.Infrastructure.Services.Verification;
using Microsoft.EntityFrameworkCore;
using DeepResearchAgent.Infrastructure.Services.Rag;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddScoped<
    IResearchAnalyzer,
    ResearchAnalyzer>();

builder.Services.AddHttpClient<IAIClient, NvidiaAIClient>(client =>
{
    client.Timeout = TimeSpan.FromMinutes(5);
});

builder.Services.AddScoped<IFactVerifier, FactVerifier>();

builder.Services.AddScoped<IFactRelevanceAnalyzer, FactRelevanceAnalyzer>();

builder.Services.AddScoped<IReportGenerator, ReportGenerator>();

builder.Services.AddCrawlerHttpClient();

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
builder.Services.AddScoped<ISourceRelevanceAnalyzer, SourceRelevanceAnalyzer>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddOpenApi();

builder.Services.AddHttpClient<
    IResearchChunkIndexer,
    QdrantResearchChunkIndexer>(client =>
    {
        client.BaseAddress = new Uri("http://localhost:6333/");
        client.Timeout = TimeSpan.FromMinutes(2);
    });

builder.Services.AddHttpClient<
    IResearchChunkRetriever,
    QdrantResearchChunkRetriever>(client =>
    {
        client.BaseAddress = new Uri(
        builder.Configuration["Qdrant:BaseUrl"] ?? "http://localhost:6333/");
        client.Timeout = TimeSpan.FromMinutes(2);
    });

builder.Services.AddHttpClient<
    IEmbeddingService,
    NvidiaEmbeddingService>(client =>
    {
        client.BaseAddress =
            new Uri("https://integrate.api.nvidia.com/");

        client.Timeout = TimeSpan.FromMinutes(5);
    });

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("Frontend");

app.MapControllers();

app.Run();