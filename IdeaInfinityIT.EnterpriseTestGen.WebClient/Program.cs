using System;
using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using IdeaInfinityIT.EnterpriseTestGen.Roslyn;
using IdeaInfinityIT.EnterpriseTestGen.Roslyn.Quality;
using IdeaInfinityIT.EnterpriseTestGen.Business;
using IdeaInfinityIT.EnterpriseTestGen.Business.Ai;
using IdeaInfinityIT.EnterpriseTestGen.CustomCases;
using IdeaInfinityIT.EnterpriseTestGen.Generator;
using IdeaInfinityIT.EnterpriseTestGen.Runner;
using IdeaInfinityIT.EnterpriseTestGen.Runner.History;

var builder = WebApplication.CreateBuilder(args);

// Register MVC Controllers and Razor Views (Matching cesc_mms_web pattern)
builder.Services.AddControllersWithViews();

// Configure maximum upload limits and form key count limits for large folders (e.g. cesc_mms_web has 5000+ files)
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.ValueCountLimit = int.MaxValue;
    options.ValueLengthLimit = int.MaxValue;
    options.KeyLengthLimit = 10000;
    options.MultipartBodyLengthLimit = 1024L * 1024L * 1024L; // 1 GB
    options.MultipartHeadersCountLimit = 10000;
});

builder.Services.Configure<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions>(options =>
{
    options.Limits.MaxRequestBodySize = 1024L * 1024L * 1024L; // 1 GB
});

// Register Test Engineering Engine Services
builder.Services.AddSingleton<RoslynCodeAnalyzer>();
builder.Services.AddSingleton<TestQualityVerifier>();
builder.Services.AddSingleton<BusinessDocParser>();
builder.Services.AddSingleton<CustomCaseParser>();
builder.Services.AddSingleton<XunitMoqGenerator>();
builder.Services.AddSingleton<DotnetTestRunner>();
builder.Services.AddSingleton<TestRunHistoryService>();
builder.Services.AddSingleton<IAiTestEnhancerService, AiTestEnhancerService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

// Default Route mapped to UnitTestGeneratorController -> UnitTestGeneratorView
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=UnitTestGenerator}/{action=UnitTestGeneratorView}/{id?}");

app.Run();
