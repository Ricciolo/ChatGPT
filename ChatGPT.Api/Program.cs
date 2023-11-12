using ChatGPT.Api;
using ChatGPT.Api.Approaches;

using Microsoft.Extensions.Azure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAzureClients(b =>
{
    b.AddSearchClient(builder.Configuration.GetSection("Search"));
    b.AddOpenAIClient(builder.Configuration.GetSection("OpenAI"));
});

//builder.Services.AddSingleton<ApproachBase, SimpleApproach>();
//builder.Services.AddSingleton<ApproachBase, SearchApproach>();
builder.Services.AddSingleton<ApproachBase, FunctionsSearchApproach>();

builder.Services.AddCors();

var app = builder.Build();

app.UseCors(o => o.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());

app.MapChatApi();

app.Run();