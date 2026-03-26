using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;

namespace WitcherHub.Infrastructure.Services.Pdf
{
    public sealed class PlaywrightBrowserInstaller
    {
        private static readonly SemaphoreSlim Gate = new(1, 1);
        private static bool _doneForCurrentProcess;

        private readonly ILogger<PlaywrightBrowserInstaller> _logger;

        public PlaywrightBrowserInstaller(ILogger<PlaywrightBrowserInstaller> logger)
        {
            _logger = logger;
        }

        public async Task EnsureInstalledAsync(CancellationToken ct = default)
        {
            if (_doneForCurrentProcess)
                return;

            await Gate.WaitAsync(ct);

            try
            {
                if (_doneForCurrentProcess)
                    return;

                _logger.LogInformation("Ensuring Playwright Chromium browser is installed.");

                await Task.Run(() =>
                {
                    Microsoft.Playwright.Program.Main(new[] { "install", "chromium" });
                }, ct);

                _doneForCurrentProcess = true;

                _logger.LogInformation("Playwright Chromium browser is ready.");
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Playwright browser installation was canceled.");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Playwright browser installation failed.");
                throw;
            }
            finally
            {
                Gate.Release();
            }
        }
    }
}
