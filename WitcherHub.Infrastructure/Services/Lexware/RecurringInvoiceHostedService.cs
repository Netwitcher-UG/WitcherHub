using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WitcherHub.Infrastructure.Data.Context;
using WitcherHub.Infrastructure.Services.Lexware;
using static WitcherHub.Infrastructure.Data.Models.Enums;

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

        private sealed class DueRecurringDocument
        {
            public Guid Id { get; init; }
            public DateOnly CycleDate { get; init; }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunOnceAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Recurring invoice hosted service loop failed.");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        private async Task RunOnceAsync(CancellationToken stoppingToken)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();

            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var lex = scope.ServiceProvider.GetRequiredService<LexwareInvoiceSyncService>();

            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            _logger.LogInformation("Recurring job running. Today={Today}", today);

            var dueContracts = await db.Contracts
                .AsNoTracking()
                .Where(c =>
                    c.Status == DocumentStatus.Signed &&
                    c.InvoiceSendMode == InvoiceSendMode.Automatic &&
                    c.RecurringEnabled &&
                    c.RecurringIsActive &&
                    c.NextRecurringInvoiceDate.HasValue &&
                    c.NextRecurringInvoiceDate.Value <= today)
                .Select(c => new DueRecurringDocument
                {
                    Id = c.Id,
                    CycleDate = c.NextRecurringInvoiceDate!.Value
                })
                .ToListAsync(stoppingToken);

            var dueQuotes = await db.Quotes
                .AsNoTracking()
                .Where(q =>
                    q.Status == DocumentStatus.Signed &&
                    q.InvoiceSendMode == InvoiceSendMode.Automatic &&
                    q.RecurringEnabled &&
                    q.RecurringIsActive &&
                    q.NextRecurringInvoiceDate.HasValue &&
                    q.NextRecurringInvoiceDate.Value <= today)
                .Select(q => new DueRecurringDocument
                {
                    Id = q.Id,
                    CycleDate = q.NextRecurringInvoiceDate!.Value
                })
                .ToListAsync(stoppingToken);

            _logger.LogInformation(
                "Recurring job due items. Contracts={ContractsCount}, Quotes={QuotesCount}",
                dueContracts.Count,
                dueQuotes.Count);

            foreach (var contract in dueContracts)
            {
                await ProcessContractAsync(lex, contract, stoppingToken);
            }

            foreach (var quote in dueQuotes)
            {
                await ProcessQuoteAsync(lex, quote, stoppingToken);
            }

            _logger.LogInformation(
                "Recurring job finished. ContractsProcessed={ContractsCount}, QuotesProcessed={QuotesCount}",
                dueContracts.Count,
                dueQuotes.Count);
        }

        private async Task ProcessContractAsync(
            LexwareInvoiceSyncService lex,
            DueRecurringDocument contract,
            CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "Processing recurring contract invoice. ContractId={ContractId}, CycleDate={CycleDate}",
                contract.Id,
                contract.CycleDate);

            try
            {
                var result = await lex.CreateRecurringInvoiceFromContractAsync(
                    contract.Id,
                    contract.CycleDate,
                    stoppingToken);

                if (result.Created)
                {
                    _logger.LogInformation(
                        "Recurring contract invoice created. ContractId={ContractId}. Message={Message}",
                        contract.Id,
                        result.Message);
                }
                else
                {
                    _logger.LogWarning(
                        "Recurring contract invoice not created. ContractId={ContractId}. Message={Message}",
                        contract.Id,
                        result.Message);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Recurring contract invoice run failed. ContractId={ContractId}",
                    contract.Id);
            }
        }

        private async Task ProcessQuoteAsync(
            LexwareInvoiceSyncService lex,
            DueRecurringDocument quote,
            CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "Processing recurring quote invoice. QuoteId={QuoteId}, CycleDate={CycleDate}",
                quote.Id,
                quote.CycleDate);

            try
            {
                var result = await lex.CreateRecurringInvoiceFromQuoteAsync(
                    quote.Id,
                    quote.CycleDate,
                    stoppingToken);

                if (result.Created)
                {
                    _logger.LogInformation(
                        "Recurring quote invoice created. QuoteId={QuoteId}. Message={Message}",
                        quote.Id,
                        result.Message);
                }
                else
                {
                    _logger.LogWarning(
                        "Recurring quote invoice not created. QuoteId={QuoteId}. Message={Message}",
                        quote.Id,
                        result.Message);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Recurring quote invoice run failed. QuoteId={QuoteId}",
                    quote.Id);
            }
        }
    }
}
