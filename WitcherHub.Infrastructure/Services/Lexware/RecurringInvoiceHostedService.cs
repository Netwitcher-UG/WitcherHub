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

                    _logger.LogInformation("Recurring job running. Today={Today}", today);

                    var dueContracts = await db.Contracts
                        .Include(c => c.Items)
                        .Where(c =>
                            c.Status == WitcherHub.Infrastructure.Data.Models.Enums.DocumentStatus.Signed &&
                            c.InvoiceSendMode == WitcherHub.Infrastructure.Data.Models.Enums.InvoiceSendMode.Automatic &&
                            c.RecurringEnabled &&
                            c.RecurringIsActive &&
                            c.NextRecurringInvoiceDate != null &&
                            c.NextRecurringInvoiceDate <= today)
                        .ToListAsync(stoppingToken);

                    _logger.LogInformation("Due contracts count = {Count}", dueContracts.Count);

                    foreach (var contract in dueContracts)
                    {
                        _logger.LogInformation(
                            "Processing ContractId={ContractId}, NextRecurringInvoiceDate={NextRecurringInvoiceDate}",
                            contract.Id,
                            contract.NextRecurringInvoiceDate);

                        try
                        {
                            var result = await lex.CreateRecurringInvoiceFromContractAsync(
                                contract.Id,
                                contract.NextRecurringInvoiceDate!.Value,
                                stoppingToken);

                            if (result.Created)
                            {
                                _logger.LogInformation(
                                    "Recurring invoice created. ContractId={ContractId}. Message={Message}",
                                    contract.Id,
                                    result.Message);
                            }
                            else
                            {
                                _logger.LogWarning(
                                    "Recurring invoice not created. ContractId={ContractId}. Message={Message}",
                                    contract.Id,
                                    result.Message);
                            }
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
