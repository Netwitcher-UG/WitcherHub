namespace WitcherHub.Infrastructure.Services.Email_Sender.Options
{
    public sealed class BackgroundTaskOptions
    {
        public int Capacity { get; init; } = 200;
        public int MaxConcurrency { get; init; } = 1;
    }
}
