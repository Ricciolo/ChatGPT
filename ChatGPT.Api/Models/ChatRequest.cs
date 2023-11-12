using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ChatGPT.Api.Models;

public record Message
{
    [JsonPropertyName("content")]
    [Required]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("role")]
    [Required]
    public string Role { get; set; } = string.Empty;
}

public record ChatRequest
{
    [JsonPropertyName("messages")]
    [Required]
    public Message[] Messages { get; set; } = Array.Empty<Message>();

    [JsonPropertyName("stream")]
    public bool Stream { get; set; }

    //[JsonPropertyName("session_state")]
    //[Required]
    //public object SessionState { get; set; }
}