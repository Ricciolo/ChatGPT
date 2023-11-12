using System.Text.Json.Serialization;

namespace ChatGPT.Api.Models;

using System.Collections.Generic;

public record ResponseMessage
{
    [JsonPropertyName("content")]
    public required string Content { get; set; }

    [JsonPropertyName("role")]
    public required string Role { get; set; }
}

public record ResponseContext
{
    //[JsonPropertyName("thoughts")]
    //public string Thoughts { get; set; }

    //[JsonPropertyName("data_points")]
    //public List<string> DataPoints { get; set; }

    //[JsonPropertyName("followup_questions")]
    //public List<string> FollowupQuestions { get; set; }
}

public record ResponseChoice
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("message")]
    public required ResponseMessage Message { get; set; }

    //[JsonPropertyName("session_state")]
    //public object SessionState { get; set; }
}

public record ChatAppResponseOrError : ChatAppResponse
{
    [JsonPropertyName("error")]
    public required string Error { get; set; }
}

[JsonDerivedType(typeof(ChatAppResponseOrError))]
public record ChatAppResponse
{
    [JsonPropertyName("choices")]
    public List<ResponseChoice> Choices { get; } = new();
}
