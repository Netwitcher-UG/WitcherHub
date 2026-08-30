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

        public async Task<string> GenerateTextAsync(string prompt) =>
            (await CompleteAsync(new AiRequest(prompt))).Text;

        public async Task<AiCompletion> CompleteAsync(AiRequest request, CancellationToken ct = default)
        {
            var prompt = request.Prompt;

            if (string.IsNullOrWhiteSpace(prompt))
                return new AiCompletion(string.Empty, AiFinishReason.Stop);

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

            var attempts = Math.Max(0, _options.MaxRetries) + 1;

            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    return await AttemptAsync(client, request, correlationId, attempt, ct);
                }
                catch (AiInvocationException ex) when (
                    attempt < attempts && IsWorthRetrying(ex.Kind) && !ct.IsCancellationRequested)
                {
                    // OpenAI__MaxRetries and OpenAI__RetryBaseDelayMilliseconds
                    // were configurable, documented, and read by nothing at all:
                    // one rate-limited call failed the whole contract and the
                    // settings that claimed to govern that did not exist in the
                    // code. They govern this loop now.
                    var delay = TimeSpan.FromMilliseconds(
                        Math.Max(0, _options.RetryBaseDelayMilliseconds) * Math.Pow(2, attempt - 1));

                    _logger.LogWarning(
                        "AI call {CorrelationId} failed with {Kind} on attempt {Attempt} of {Attempts}. " +
                        "Retrying in {Delay}ms.",
                        correlationId, ex.Kind, attempt, attempts, (int)delay.TotalMilliseconds);

                    await Task.Delay(delay, ct);
                }
            }
        }

        /// <summary>
        /// Which failures are worth a second attempt.
        ///
        /// Only the ones that can pass on their own. A rejected key, a model this
        /// account cannot use and an exhausted quota are all still true a second
        /// later, so retrying them turns a clear permanent fault into a slow
        /// intermittent-looking one and bills for the privilege. A timeout is
        /// excluded on the same grounds from the other direction: it usually means
        /// the prompt was too large for the deadline, and a second attempt pays
        /// the full timeout again before saying so.
        /// </summary>
        internal static bool IsWorthRetrying(AiFailureKind kind) => kind switch
        {
            AiFailureKind.RateLimited => true,
            AiFailureKind.ServiceError => true,
            AiFailureKind.Network => true,
            _ => false
        };

        private async Task<AiCompletion> AttemptAsync(
            ChatClient client,
            AiRequest request,
            string correlationId,
            int attempt,
            CancellationToken ct)
        {
            var prompt = request.Prompt;

            var started = Stopwatch.GetTimestamp();

            try
            {
                var messages = new List<ChatMessage>();

                // The system instruction used to have nowhere to go, so the rules
                // that constrain what the model may invent travelled as ordinary
                // prompt text or, in the contract generator's case, not at all —
                // ContractGeneratorPrompt.SystemInstruction was written, reviewed
                // and never sent.
                if (!string.IsNullOrWhiteSpace(request.SystemInstruction))
                    messages.Add(new SystemChatMessage(request.SystemInstruction));

                messages.Add(new UserChatMessage(prompt));

                var options = new ChatCompletionOptions();

                // No budget was set at all before this, so the provider's default
                // decided how long an answer could be. For a contract that is a
                // silent ceiling on the document's length that nobody could see or
                // configure.
                if (request.MaxOutputTokens is > 0)
                    options.MaxOutputTokenCount = request.MaxOutputTokens;

                ChatCompletion completion = await client.CompleteChatAsync(messages, options, ct);

                // Every part, not the first. A model that answers in several
                // content parts had all but the first thrown away here, which
                // truncates the answer before anything else has a chance to.
                var text = string.Concat(completion.Content.Select(part => part.Text ?? ""));

                var finish = Translate(completion.FinishReason);

                _logger.LogInformation(
                    "AI call {CorrelationId} [{Purpose}] finished {Finish} in {Elapsed}ms using {Model}. " +
                    "Characters {Length}, input tokens {In}, output tokens {Out}, budget {Budget}.",
                    correlationId,
                    request.Purpose ?? "-",
                    finish,
                    (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                    _options.Model,
                    text.Length,
                    completion.Usage?.InputTokenCount,
                    completion.Usage?.OutputTokenCount,
                    request.MaxOutputTokens?.ToString() ?? "provider default");

                if (finish == AiFinishReason.Length)
                {
                    // Logged as a warning rather than swallowed: an answer that ran
                    // out of room is a capacity problem with a setting behind it,
                    // and it looks exactly like a badly-written prompt from the
                    // outside. The caller decides what to do; it is told, which is
                    // the part that was missing.
                    _logger.LogWarning(
                        "AI call {CorrelationId} [{Purpose}] was cut off at the output limit ({Budget} tokens). " +
                        "The answer is incomplete. Raise {Setting} if this recurs.",
                        correlationId,
                        request.Purpose ?? "-",
                        request.MaxOutputTokens?.ToString() ?? "provider default",
                        OpenAIOptions.MaxOutputTokensSettingName);
                }

                return new AiCompletion(
                    text,
                    finish,
                    completion.Usage?.InputTokenCount,
                    completion.Usage?.OutputTokenCount);
            }
            catch (Exception ex)
            {
                var (kind, status) = Classify(ex);

                // Prompt and response are deliberately absent: they carry contract
                // text and customer data, and this line goes to the platform log.
                _logger.LogError(
                    "AI call {CorrelationId} attempt {Attempt} failed after {Elapsed}ms. Kind {Kind}, HTTP {Status}, " +
                    "model {Model}, exception {Exception}: {Detail}",
                    correlationId,
                    attempt,
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
        /// The provider's stop reason, in terms this application acts on.
        ///
        /// Only <see cref="ChatFinishReason.Length"/> matters much: it is the one
        /// that means the text is incomplete while looking perfectly well-formed
        /// up to where it stops.
        /// </summary>
        internal static AiFinishReason Translate(ChatFinishReason reason) => reason switch
        {
            ChatFinishReason.Stop => AiFinishReason.Stop,
            ChatFinishReason.Length => AiFinishReason.Length,
            ChatFinishReason.ContentFilter => AiFinishReason.ContentFilter,
            _ => AiFinishReason.Other
        };

        /// <summary>
        /// Turns whatever the client threw into something a person can act on.
        /// </summary>
        internal static (AiFailureKind Kind, int? StatusCode) Classify(Exception ex)
        {
            if (ex is ClientResultException client)
            {
                var body = BodyOf(client);

                return client.Status switch
                {
                    401 or 403 => (AiFailureKind.Authentication, client.Status),
                    404 => (AiFailureKind.ModelUnavailable, client.Status),

                    // 429 covers two different faults. Ordinary rate limiting
                    // clears by waiting; an exhausted quota or a reached spending
                    // limit never clears on its own. Only the body tells them
                    // apart, and telling the owner to "try again shortly" when
                    // the account is out of credit sends them into a loop.
                    429 => (ClassifyTooManyRequests(body), client.Status),

                    408 => (AiFailureKind.Timeout, client.Status),
                    >= 500 => (AiFailureKind.ServiceError, client.Status),

                    // 400 from this API is most often an unusable model name for
                    // the endpoint being called, but a billing stop can also
                    // surface here.
                    400 => (IndicatesQuota(body)
                        ? AiFailureKind.QuotaExhausted
                        : AiFailureKind.ModelUnavailable, client.Status),

                    _ => (AiFailureKind.Unknown, client.Status)
                };
            }

            if (ex is TaskCanceledException or TimeoutException)
                return (AiFailureKind.Timeout, null);

            if (ex is HttpRequestException or SocketException)
                return (AiFailureKind.Network, null);

            // Look inside before guessing from the outer type. The two checks
            // below read a shape rather than a statement of fact, and a wrapped
            // provider error carries a statement of fact: a ClientResultException
            // inside an InvalidOperationException used to be reported as "no
            // OpenAI API key is configured" no matter what the provider said,
            // which sends the owner to the wrong setting entirely.
            if (ex.InnerException is not null)
            {
                var inner = Classify(ex.InnerException);
                if (inner.Kind != AiFailureKind.Unknown || inner.StatusCode is not null)
                    return inner;
            }

            if (ex is InvalidOperationException or ArgumentException)
                return (AiFailureKind.NotConfigured, null);

            return (AiFailureKind.Unknown, null);
        }

        /// <summary>
        /// A 429 is either "you are going too fast" or "you have run out", and
        /// the two need opposite advice. The provider says which in the error
        /// code; when it says nothing, the safer reading is ordinary rate
        /// limiting, because that one at least suggests waiting rather than
        /// sending the owner to a billing page for no reason.
        /// </summary>
        private static AiFailureKind ClassifyTooManyRequests(string body) =>
            IndicatesQuota(body) ? AiFailureKind.QuotaExhausted : AiFailureKind.RateLimited;

        private static bool IndicatesQuota(string body)
        {
            if (string.IsNullOrEmpty(body)) return false;

            // The literal codes and phrases OpenAI returns for an account that
            // cannot spend anything more. Matched case-insensitively because the
            // wording of the human-readable half is not contractual.
            string[] markers =
            [
                "insufficient_quota",
                "billing_hard_limit_reached",
                "billing_not_active",
                "account_deactivated",
                "exceeded your current quota",
                "check your plan and billing"
            ];

            return markers.Any(marker => body.Contains(marker, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Everything the client is willing to tell us about the failed call.
        ///
        /// The exception message usually already carries the response body, but
        /// not on every path, so the raw response is read as well. Reading it can
        /// itself throw once the response is disposed — that is not worth losing
        /// the classification over.
        /// </summary>
        private static string BodyOf(ClientResultException client)
        {
            var message = client.Message ?? "";

            try
            {
                var content = client.GetRawResponse()?.Content?.ToString();
                if (!string.IsNullOrEmpty(content))
                    return message + "\n" + content;
            }
            catch
            {
                // Fall through to the message alone.
            }

            return message;
        }

        /// <summary>
        /// Provider messages quote the offending request back, which can include
        /// the key. Anything that looks like one is replaced outright.
        ///
        /// The whole token goes, the "sk-" and any trailing characters with it.
        /// An earlier version left the prefix and wrote sk-***, and the client's
        /// own masking — sk-AbCde**********************wxyz — leaves the first
        /// few and last few characters in place. Neither is enough to reconstruct
        /// a key, and neither is worth defending: there is no use for a fragment
        /// of a key in a log, and "no part of the key is written" is a rule that
        /// can be checked, where "not too much of it" is not.
        ///
        /// Bearer tokens go the same way. The same secret travels as an
        /// Authorization header, and a message quoting the failed request back
        /// can carry it in that form instead.
        /// </summary>
        internal static string Redact(string message)
        {
            if (string.IsNullOrEmpty(message)) return "";

            // sk-, sk-proj-, sk-svcacct- and the masked forms in one pass: the
            // prefix, then anything a key or its mask is made of.
            var redacted = System.Text.RegularExpressions.Regex.Replace(
                message, @"sk-[A-Za-z0-9_*\-]{2,}", "[REDACTED]");

            return System.Text.RegularExpressions.Regex.Replace(
                redacted, @"(?i)\bBearer\s+[A-Za-z0-9._~+/*\-]+=*", "Bearer [REDACTED]");
        }

        private static string NewCorrelationId() =>
            Convert.ToHexString(RandomNumberGenerator.GetBytes(4));
    }
}
