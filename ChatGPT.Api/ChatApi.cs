using System.Text.Json;

using Azure.AI.OpenAI;

using ChatGPT.Api.Approaches;
using ChatGPT.Api.Models;

using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace ChatGPT.Api;

public static class ChatApi
{
    public static void MapChatApi(this WebApplication application) =>
        application.MapPost("/chat", async (
            ApproachBase approach,
            HttpContext context,
            ChatRequest request,
            IOptions<JsonOptions> jsonOptions) =>
        {
            var result = approach.ChatAsync(request, context.RequestAborted);

            if (!request.Stream)
            {
                // Recupero l'ultima risposta
                ChatAppResponse? latestResponse = null;
                await foreach (var response in result)
                {
                    if (latestResponse == null || latestResponse.Choices.Count == 0)
                    {
                        latestResponse = response;
                    }
                    else
                    {
                        latestResponse.Choices[0].Message.Content += response.Choices[0].Message.Content;
                    }
                }

                return latestResponse == null ? Results.Problem() : Results.Ok(latestResponse);
            }

            var first = true;
            await foreach (var response in result)
            {
                if (!first)
                {
                    // Scrivo un newline tra una risposta e l'altra
                    await context.Response.WriteAsync("\n");
                }

                // Serializzo ogni singola risposta
                await JsonSerializer.SerializeAsync(context.Response.Body, response, jsonOptions.Value.SerializerOptions);

                first = false;
            }

            return Results.Empty;
        });
}
