using System.Text.Json.Serialization;
using Immo24AiDemo;
using Immo24AiDemo.Components;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// 1. Blazor UI Services
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// 2. Backend & KI-Scoring Services
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddSingleton<IApplicationRepository, InMemoryApplicationRepository>();
builder.Services.AddScoped<IAiEvaluationService, MockAiEvaluationService>();
builder.Services.AddScoped<IApplicantScoringPipeline, ApplicantScoringPipeline>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStaticFiles();
app.UseAntiforgery();

// 3. REST-API Endpunkte (für externe Requests / Webhooks)
var api = app.MapGroup("/api/v1/applications");

api.MapPost("/evaluate", async (
    [FromBody] ApplicantSubmissionRequest request,
    IApplicantScoringPipeline pipeline,
    IApplicationRepository repo,
    CancellationToken ct) =>
{
    var evaluation = await pipeline.ProcessApplicationAsync(request, ct);
    repo.Save(evaluation);
    return Results.Created($"/api/v1/applications/{evaluation.ApplicationId}", evaluation);
});

api.MapGet("/listing/{listingId}", (string listingId, IApplicationRepository repo) =>
{
    var results = repo.GetByListing(listingId);
    return Results.Ok(new { ListingId = listingId, TotalApplicants = results.Count, TopCandidates = results });
});

// 4. Blazor UI Endpunkte
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();