namespace WitcherHub.Infrastructure.Services.OpenAI
{
    /// <summary>
    /// Everything about how the assistant is called, in one place.
    ///
    /// Model names used to appear as literals in the options default, in the DI
    /// registration's fallback, and implicitly in whatever an environment
    /// happened to set — three answers to one question, and no way to tell from
    /// the code which one a running instance was using. There is now one source,
    /// it is logged at start-up, and the failure messages name the setting an
    /// administrator has to change.
    /// </summary>
    public class OpenAIOptions
    {
        public const string SectionName = "OpenAI";

        public string ApiKey { get; set; } = string.Empty;

        /// <summary>
        /// The model to use. Deliberately not defaulted to a specific name:
        /// a wrong default is worse than a missing one, because a missing one
        /// says so at start-up while a wrong one fails at the moment of use with
        /// an error about the model rather than about the configuration.
        /// </summary>
        public string Model { get; set; } = string.Empty;

        /// <summary>
        /// Used when <see cref="Model"/> is unusable for this account. Optional:
        /// left empty, a model failure is reported rather than worked around.
        /// </summary>
        public string? FallbackModel { get; set; }

        /// <summary>How long to wait for one call before giving up.</summary>
        public int TimeoutSeconds { get; set; } = 60;

        /// <summary>
        /// How many times to retry a call that failed for a reason that might
        /// pass. Zero disables retrying.
        ///
        /// Kept low on purpose. Retrying a rate limit is how an account that is
        /// merely busy becomes an account that is over quota, and every retry of
        /// a long prompt costs what the first attempt cost.
        /// </summary>
        public int MaxRetries { get; set; } = 1;

        /// <summary>Delay before the first retry; doubled for each one after.</summary>
        public int RetryBaseDelayMilliseconds { get; set; } = 2000;

        /// <summary>
        /// The name of the setting to quote at an administrator when there is no
        /// key. Written once here so every message agrees.
        /// </summary>
        public const string ApiKeySettingName = "OpenAI__ApiKey";

        public const string ModelSettingName = "OpenAI__Model";

        public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(Model);

        /// <summary>
        /// What is missing, for the start-up log and the health endpoint. Empty
        /// when the assistant is ready to be called.
        /// </summary>
        public IReadOnlyList<string> MissingSettings
        {
            get
            {
                var missing = new List<string>();

                if (string.IsNullOrWhiteSpace(ApiKey)) missing.Add(ApiKeySettingName);
                if (string.IsNullOrWhiteSpace(Model)) missing.Add(ModelSettingName);

                return missing;
            }
        }
    }
}
