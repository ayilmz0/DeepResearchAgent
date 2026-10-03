using DeepResearchAgent.Engine.Interfaces;
using DeepResearchAgent.Engine.Services;
using DeepResearchAgent.Infrastructure.Persistence;
using DeepResearchAgent.Infrastructure.Persistence.Repositories;
using DeepResearchAgent.Worker;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<IResearchRepository, ResearchRepository>();
builder.Services.AddScoped<IResearchService, ResearchService>();

builder.Services.AddHostedService<Worker>();

var host = builder.Build();

host.Run();