using OpenAI.Chat;

namespace ScrumMaster.API.Services;

public class GeminiService : IGeminiService
{
    private readonly ChatClient _client;
    private readonly ChatCompletionOptions _requestOptions;

    public GeminiService(ChatClient client)
    {
        _client = client;
        _requestOptions = new ChatCompletionOptions()
        {
            MaxOutputTokenCount = 13107,
            Temperature = 1.0f,
            TopP = 1.0f,
            FrequencyPenalty = 0.0f,
            PresencePenalty = 0.0f
        };
    }

    public async Task<string> AnalyzeStandupAsync(string prompt, CancellationToken ct = default)
    {
        var message = new UserChatMessage(prompt);

        var response = await _client.CompleteChatAsync([message], _requestOptions, cancellationToken: ct);

        return response.Value.Content[0].Text;
    }
}