using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using System.ClientModel;
using System.Diagnostics;
using System.Net.Sockets;
using System.Security.Cryptography;
using WitcherHub.Application.Interfaces;

namespace WitcherHub.Infrastructure.Services.OpenAI
{
    /// <summary>
    /// The one place a model call is made.
    ///
    /// Every failure used to arrive at the caller as a bare Exception, which each
    /// caller turned into "the assistant is not reachable right now". That is the
    /// same sentence for a missing API key, a model the account cannot use, a
    /// rate limit and blocked outbound networking — four problems with four
    /// different fixes and no way to tell them apart from the screen. Failures
    /// are classified here, logged with the technical detail, and handed on with
    /// a reference the user can quote.
    /// </summary>
    public class OpenAiTextGenerator : IAiTextGenerator
    {
        private readonly IServiceProvider _services;
        private readonly OpenAIOptions _options;
        private readonly ILogger<OpenAiTextGenerator> _logger;

        public OpenAiTextGenerator(
            IServiceProvider services,
            IOptions<OpenAIOptions> options,
            ILogger<OpenAiTextGenerator> logger)
        {
            _services = services;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<string> GenerateTextAsync(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt))
                return string.Empty;

            var correlationId = NewCorrelationId();

            // Resolved here rather than injected: the registration throws when no
            // key is configured, and as a constructor dependency that turned an
            // unconfigured environment into a 500 on a page that would otherwise
            // have worked perfectly well without the assistant.
            ChatClient client;
            try
            {
                client = (ChatClient)_services.GetService(typeof(ChatClient))!
                    ?? throw new InvalidOperationException("No ChatClient is registered.");
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    "AI call {CorrelationId} was not attempted: the client could not be created. Model {Model}. {Detail}",
                    correlationId, _options.Model, ex.GetBaseException().Message);

                throw new AiInvocationException(
                    AiFailureKind.NotConfigured,
                    "the OpenAI client could not be created",
                    correlationId,
                    inner: ex);
            }

            var started = Stopwatch.GetTimestamp();

            try
            {
                ChatCompletion completion = await client.CompleteChatAsync(prompt);

                var text = completion.Content.Count > 0 ? completion.Content[0].Text : string.Empty;

                _logger.LogInformation(
                    "AI call {CorrelationId} succeeded in {Elapsed}ms using {Model}. Response length {Length}.",
                    correlationId,
                    (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                    _options.Model,
                    text.Length);

                return text;
            }
            catch (Exception ex)
            {
                var (kind, status) = Classify(ex);

                // Prompt and response are deliberately absent: they carry contract
                // text and customer data, and this line goes to the platform log.
                _logger.LogError(
                    "AI call {CorrelationId} failed after {Elapsed}ms. Kind {Kind}, HTTP {Status}, model {Model}, " +
                    "exception {Exception}: {Detail}",
                    correlationId,
                    (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                    kind,
                    status?.ToString() ?? "-",
                    _options.Model,
                    ex.GetType().Name,
                    Redact(ex.GetBaseException().Message));

                throw new AiInvocationException(kind, ex.GetType().Name, correlationId, status, ex);
            }
        }

        /// <summary>
        /// Turns whatever the client threw into something a person can act on.
        /// </summary>
        internal static (AiFailureKind Kind, int? StatusCode) Classify(Exception ex)
        {
            if (ex is ClientResultException client)
            {
                return client.Status switch
                {
                    401 or 403 => (AiFailureKind.Authentication, client.Status),
                    404 => (AiFailureKind.ModelUnavailable, client.Status),
                    429 => (AiFailureKind.RateLimited, client.Status),
                    408 => (AiFailureKind.Timeout, client.Status),
                    >= 500 => (AiFailureKind.ServiceError, client.Status),

                    // 400 from this API is most often an unusable model name for
                    // the endpoint being called.
                    400 => (AiFailureKind.ModelUnavailable, client.Status),
                    _ => (AiFailureKind.Unknown, client.Status)
                };
            }

            if (ex is TaskCanceledException or TimeoutException)
                return (AiFailureKind.Timeout, null);

            if (ex is HttpRequestException or SocketException)
                return (AiFailureKind.Network, null);

            if (ex is InvalidOperationException or ArgumentException)
                return (AiFailureKind.NotConfigured, null);

            if (ex.InnerException is not null)
                return Classify(ex.InnerException);

            return (AiFailureKind.Unknown, null);
        }

        /// <summary>
        /// Provider messages quote the offending request back, which can include
        /// a key fragment. Anything that looks like one is removed.
        /// </summary>
        internal static string Redact(string message)
        {
            if (string.IsNullOrEmpty(message)) return "";

            return System.Text.RegularExpressions.Regex.Replace(
                message, @"sk-[A-Za-z0-9_\-]{8,}", "sk-***");
        }

        private static string NewCorrelationId() =>
            Convert.ToHexString(RandomNumberGenerator.GetBytes(4));
    }
}
