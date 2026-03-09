using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WitcherHub.Infrastructure.Data.Context;
using WitcherHub.Infrastructure.Services.Lexware;

namespace WitcherHub.Infrastructure.Services.BackgroundTasks
{
    public sealed class RecurringInvoiceHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<RecurringInvoiceHostedService> _logger;

        public RecurringInvoiceHostedService(
            IServiceScopeFactory scopeFactory,
            ILogger<RecurringInvoiceHostedService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();

                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var lex = scope.ServiceProvider.GetRequiredService<LexwareInvoiceSyncService>();

                    var today = DateOnly.FromDateTime(DateTime.UtcNow);

                    var dueContracts = await db.Contracts
                        .Include(c => c.Items)
                        .Where(c =>
                            c.Status == WitcherHub.Infrastructure.Data.Models.Enums.DocumentStatus.Signed &&
                            c.RecurringEnabled &&
                            c.RecurringIsActive &&
                            c.NextRecurringInvoiceDate != null &&
                            c.NextRecurringInvoiceDate <= today)
                        .ToListAsync(stoppingToken);

                    foreach (var contract in dueContracts)
                    {
                        try
                        {
                            await lex.CreateRecurringInvoiceFromContractAsync(
                                contract.Id,
                                contract.NextRecurringInvoiceDate!.Value,
                                stoppingToken);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Recurring invoice run failed. ContractId={ContractId}", contract.Id);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Recurring invoice hosted service loop failed.");
                }

                await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
            }
        }
    }
}
