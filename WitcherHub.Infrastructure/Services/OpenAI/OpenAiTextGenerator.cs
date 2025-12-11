using OpenAI.Chat;
using WitcherHub.Application.Interfaces;

namespace WitcherHub.Infrastructure.Services.OpenAI
{
    public class OpenAiTextGenerator : IAiTextGenerator
    {
        private readonly ChatClient _chatClient;

        public OpenAiTextGenerator(ChatClient chatClient)
        {
            _chatClient = chatClient;
        }

        public async Task<string> GenerateTextAsync(string prompt)
        {
            ChatCompletion completion = await _chatClient.CompleteChatAsync(prompt);

            return completion.Content.Count > 0
                ? completion.Content[0].Text
                : string.Empty;
        }
    }
}
