using Microsoft.EntityFrameworkCore;
using WitcherHub.Application.Common.Pagination;
using WitcherHub.Application.Interfaces;
using WitcherHub.Application.Interfaces.ManageData;
using WitcherHub.Application.Models.View.Overview;
using WitcherHub.Application.Models.View.Registers;
using WitcherHub.Infrastructure.Data.Models;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Infrastructure.ManageData.Registers
{
    /// <summary>
    /// Reads across the whole business rather than one project at a time.
    ///
    /// Every existing list query takes a project id, which meant a quote could only
    /// be found by first remembering which project it belonged to, and questions
    /// like "what is owed to us" had no query behind them at all.
    ///
    /// Deliberately uncached. These are the screens someone opens to find out what
    /// changed; a list that is thirty seconds stale is worse here than a query that
    /// runs each time, and the aggregates are cheap next to the page they feed.
    /// </summary>
    public sealed class DocumentRegister : IDocumentRegister
    {
        private readonly IUnitOfWork _unitOfWork;

        public DocumentRegister(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

        private static (int page, int size) Paging(RegisterFilter filter)
        {
            var page = filter.Page < 1 ? 1 : filter.Page;
            var size = filter.PageSize is < 1 or > 200 ? 20 : filter.PageSize;
            return (page, size);
        }

        private static string Pattern(string search) =>
            "%" + search.Trim()
                .Replace("!", "!!")
                .Replace("%", "!%")
                .Replace("_", "!_")
                .Replace("[", "![") + "%";

        // =====================================================================
        // Quotes
        // =====================================================================
        public async Task<PagedResult<QuoteRegisterRow>> GetQuotesAsync(
            RegisterFilter filter, CancellationToken ct = default)
        {
            var (page, size) = Paging(filter);
            var q = _unitOfWork.Repo<Quote>().Query(asNoTracking: true);

            if (filter.Status is not null)
                q = q.Where(x => x.Status == filter.Status);

            if (filter.CustomerId is not null)
                q = q.Where(x => x.Project.CustomerId == filter.CustomerId);

            if (filter.ProjectId is not null)
                q = q.Where(x => x.ProjectId == filter.ProjectId);

            if (filter.From is not null)
                q = q.Where(x => x.CreatedAt >= filter.From.Value.ToDateTime(TimeOnly.MinValue));

            if (filter.To is not null)
                q = q.Where(x => x.CreatedAt <= filter.To.Value.ToDateTime(TimeOnly.MaxValue));

            if (!string.IsNullOrWhiteSpace(filter.Search))
            {
                var p = Pattern(filter.Search);
                q = q.Where(x =>
                    EF.Functions.Like(x.QuoteNo, p, "!") ||
                    EF.Functions.Like(x.Project.Customer.Name, p, "!") ||
                    EF.Functions.Like(x.Project.Title, p, "!"));
            }

            var total = await q.LongCountAsync(ct);
            if (total == 0)
                return PagedResult<QuoteRegisterRow>.Empty(page, size);

            var items = await q
                .OrderByDescending(x => x.CreatedAt)
                .Skip((page - 1) * size)
                .Take(size)
                .Select(x => new QuoteRegisterRow
                {
                    Id = x.Id,
                    ProjectId = x.ProjectId,
                    CustomerId = x.Project.CustomerId,
                    QuoteNo = x.QuoteNo,
                    CustomerName = x.Project.Customer.Name,
                    ProjectTitle = x.Project.Title,
                    Status = x.Status,
                    Currency = x.Currency,
                    CreatedAt = x.CreatedAt,
                    IssuedAt = x.IssuedAt,
                    ExpiresAt = x.ExpiresAt,
                    SignedAt = x.SignedAt,
                    ItemCount = x.Items.Count,
                    ItemsTotal = x.Items.Sum(i => i.Quantity * i.UnitPrice)
                })
                .ToListAsync(ct);

            return new PagedResult<QuoteRegisterRow>
            {
                Items = items,
                Page = page,
                PageSize = size,
                TotalItems = total
            };
        }

        // =====================================================================
        // Contracts
        // =====================================================================
        public async Task<PagedResult<ContractRegisterRow>> GetContractsAsync(
            RegisterFilter filter, CancellationToken ct = default)
        {
            var (page, size) = Paging(filter);
            var q = _unitOfWork.Repo<Contract>().Query(asNoTracking: true);

            if (filter.Status is not null)
                q = q.Where(x => x.Status == filter.Status);

            if (filter.CustomerId is not null)
                q = q.Where(x => x.Project.CustomerId == filter.CustomerId);

            if (filter.ProjectId is not null)
                q = q.Where(x => x.ProjectId == filter.ProjectId);

            if (filter.From is not null)
                q = q.Where(x => x.CreatedAt >= filter.From.Value.ToDateTime(TimeOnly.MinValue));

            if (filter.To is not null)
                q = q.Where(x => x.CreatedAt <= filter.To.Value.ToDateTime(TimeOnly.MaxValue));

            if (!string.IsNullOrWhiteSpace(filter.Search))
            {
                var p = Pattern(filter.Search);
                q = q.Where(x =>
                    EF.Functions.Like(x.ContractNo, p, "!") ||
                    EF.Functions.Like(x.Project.Customer.Name, p, "!") ||
                    EF.Functions.Like(x.Project.Title, p, "!"));
            }

            var total = await q.LongCountAsync(ct);
            if (total == 0)
                return PagedResult<ContractRegisterRow>.Empty(page, size);

            var items = await q
                .OrderByDescending(x => x.CreatedAt)
                .Skip((page - 1) * size)
                .Take(size)
                .Select(x => new ContractRegisterRow
                {
                    Id = x.Id,
                    ProjectId = x.ProjectId,
                    CustomerId = x.Project.CustomerId,
                    ContractNo = x.ContractNo,
                    CustomerName = x.Project.Customer.Name,
                    ProjectTitle = x.Project.Title,
                    Status = x.Status,
                    Currency = x.Currency,
                    StartDate = x.StartDate,
                    EndDate = x.EndDate,
                    SignedAt = x.SignedAt,
                    CreatedAt = x.CreatedAt,
                    RecurringEnabled = x.RecurringEnabled,
                    RecurringIsActive = x.RecurringIsActive,
                    NextRecurringInvoiceDate = x.NextRecurringInvoiceDate,
                    ItemCount = x.Items.Count,
                    ItemsTotal = x.Items.Sum(i => i.Quantity * i.UnitPrice)
                })
                .ToListAsync(ct);

            return new PagedResult<ContractRegisterRow>
            {
                Items = items,
                Page = page,
                PageSize = size,
                TotalItems = total
            };
        }

        // =====================================================================
        // Invoices
        // =====================================================================
        public async Task<PagedResult<InvoiceRegisterRow>> GetInvoicesAsync(
            RegisterFilter filter, CancellationToken ct = default)
        {
            var (page, size) = Paging(filter);
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            var q = _unitOfWork.Repo<Invoice>().Query(asNoTracking: true);

            if (filter.Status is not null)
                q = q.Where(x => x.Status == filter.Status);

            if (filter.CustomerId is not null)
                q = q.Where(x => x.Project.CustomerId == filter.CustomerId);

            if (filter.ProjectId is not null)
                q = q.Where(x => x.ProjectId == filter.ProjectId);

            if (filter.From is not null)
                q = q.Where(x => x.IssueDate >= filter.From);

            if (filter.To is not null)
                q = q.Where(x => x.IssueDate <= filter.To);

            if (filter.OutstandingOnly || filter.OverdueOnly)
            {
                q = q.Where(x =>
                    x.Status != DocumentStatus.Draft &&
                    x.Status != DocumentStatus.Void &&
                    x.Status != DocumentStatus.Cancelled &&
                    (x.Totals == null || x.Totals.BalanceDue > 0m));
            }

            if (filter.OverdueOnly)
                q = q.Where(x => x.DueDate != null && x.DueDate < today);

            if (!string.IsNullOrWhiteSpace(filter.Search))
            {
                var p = Pattern(filter.Search);
                q = q.Where(x =>
                    EF.Functions.Like(x.InvoiceNo, p, "!") ||
                    EF.Functions.Like(x.Project.Customer.Name, p, "!") ||
                    EF.Functions.Like(x.Project.Title, p, "!"));
            }

            var total = await q.LongCountAsync(ct);
            if (total == 0)
                return PagedResult<InvoiceRegisterRow>.Empty(page, size);

            var items = await q
                .OrderByDescending(x => x.IssueDate ?? DateOnly.FromDateTime(x.CreatedAt.UtcDateTime))
                .ThenByDescending(x => x.CreatedAt)
                .Skip((page - 1) * size)
                .Take(size)
                .Select(x => new InvoiceRegisterRow
                {
                    Id = x.Id,
                    ProjectId = x.ProjectId,
                    CustomerId = x.Project.CustomerId,
                    InvoiceNo = x.InvoiceNo,
                    CustomerName = x.Project.Customer.Name,
                    ProjectTitle = x.Project.Title,
                    Status = x.Status,
                    Currency = x.Currency,
                    IssueDate = x.IssueDate,
                    DueDate = x.DueDate,
                    CreatedAt = x.CreatedAt,
                    Total = x.Totals != null ? x.Totals.Total : x.Items.Sum(i => i.Quantity * i.UnitPrice),
                    PaidTotal = x.Totals != null ? x.Totals.PaidTotal : 0m,
                    BalanceDue = x.Totals != null
                        ? x.Totals.BalanceDue
                        : x.Items.Sum(i => i.Quantity * i.UnitPrice),

                    // Overdue is a fact about today, not a stored status: nothing
                    // runs at midnight to move invoices into an Overdue state.
                    IsOverdue =
                        x.DueDate != null &&
                        x.DueDate < today &&
                        x.Status != DocumentStatus.Paid &&
                        x.Status != DocumentStatus.Draft &&
                        x.Status != DocumentStatus.Void &&
                        x.Status != DocumentStatus.Cancelled &&
                        (x.Totals == null || x.Totals.BalanceDue > 0m)
                })
                .ToListAsync(ct);

            return new PagedResult<InvoiceRegisterRow>
            {
                Items = items,
                Page = page,
                PageSize = size,
                TotalItems = total
            };
        }

        // =====================================================================
        // Filter options
        // =====================================================================
        public async Task<IReadOnlyList<(Guid Id, string Name)>> GetCustomersWithDocumentsAsync(
            DocumentKind kind, CancellationToken ct = default)
        {
            var customers = _unitOfWork.Repo<Customer>().Query(asNoTracking: true);

            var filtered = kind switch
            {
                DocumentKind.Quote => customers.Where(c => c.Projects.Any(p => p.Quotes.Any())),
                DocumentKind.Contract => customers.Where(c => c.Projects.Any(p => p.Contracts.Any())),
                _ => customers.Where(c => c.Projects.Any(p => p.Invoices.Any()))
            };

            var rows = await filtered
                .OrderBy(c => c.Name)
                .Select(c => new { c.Id, c.Name })
                .ToListAsync(ct);

            return rows.Select(r => (r.Id, r.Name)).ToList();
        }

        // =====================================================================
        // Overview
        // =====================================================================
        public async Task<BusinessOverview> GetOverviewAsync(CancellationToken ct = default)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var monthStart = new DateTimeOffset(new DateTime(today.Year, today.Month, 1), TimeSpan.Zero);
            var lastMonthStart = monthStart.AddMonths(-1);

            var quotes = _unitOfWork.Repo<Quote>().Query(asNoTracking: true);
            var contracts = _unitOfWork.Repo<Contract>().Query(asNoTracking: true);
            var invoices = _unitOfWork.Repo<Invoice>().Query(asNoTracking: true);
            var payments = _unitOfWork.Repo<Payment>().Query(asNoTracking: true)
                .Where(p => p.Status == PaymentStatus.Success);

            // ---- money -------------------------------------------------------
            var owedStatuses = new[]
            {
                DocumentStatus.Issued, DocumentStatus.Sent,
                DocumentStatus.Open, DocumentStatus.Overdue
            };

            var owed = invoices.Where(i =>
                owedStatuses.Contains(i.Status) &&
                (i.Totals == null || i.Totals.BalanceDue > 0m));

            var outstanding = await owed
                .SumAsync(i => i.Totals != null
                    ? i.Totals.BalanceDue
                    : i.Items.Sum(x => x.Quantity * x.UnitPrice), ct);

            var outstandingCount = await owed.CountAsync(ct);

            var lateQuery = owed.Where(i => i.DueDate != null && i.DueDate < today);

            var overdue = await lateQuery
                .SumAsync(i => i.Totals != null
                    ? i.Totals.BalanceDue
                    : i.Items.Sum(x => x.Quantity * x.UnitPrice), ct);

            var overdueCount = await lateQuery.CountAsync(ct);

            var collectedThisMonth = await payments
                .Where(p => p.PaidAt >= monthStart)
                .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;

            var collectedLastMonth = await payments
                .Where(p => p.PaidAt >= lastMonthStart && p.PaidAt < monthStart)
                .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;

            var collectedAllTime = await payments.SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;

            // ---- pipeline ----------------------------------------------------
            var quoteCount = await quotes.CountAsync(ct);

            var awaiting = quotes.Where(q => q.Status == DocumentStatus.Sent);
            var awaitingCount = await awaiting.CountAsync(ct);
            var awaitingValue = await awaiting
                .SumAsync(q => (decimal?)q.Items.Sum(i => i.Quantity * i.UnitPrice), ct) ?? 0m;

            var decidedCount = await quotes.CountAsync(q =>
                q.Status == DocumentStatus.Accepted ||
                q.Status == DocumentStatus.Signed ||
                q.Status == DocumentStatus.Rejected, ct);

            var wonCount = await quotes.CountAsync(q =>
                q.Status == DocumentStatus.Accepted || q.Status == DocumentStatus.Signed, ct);

            var contractCount = await contracts.CountAsync(ct);

            var live = contracts.Where(c =>
                c.Status == DocumentStatus.Signed || c.Status == DocumentStatus.Accepted);

            var liveCount = await live.CountAsync(ct);
            var liveValue = await live
                .SumAsync(c => (decimal?)c.Items.Sum(i => i.Quantity * i.UnitPrice), ct) ?? 0m;

            var awaitingSignatureCount = await contracts
                .CountAsync(c => c.Status == DocumentStatus.Sent, ct);

            var endingHorizon = today.AddDays(60);
            var endingSoonCount = await live
                .CountAsync(c => c.EndDate != null && c.EndDate >= today && c.EndDate <= endingHorizon, ct);

            var money = new MoneySummary
            {
                Outstanding = outstanding,
                Overdue = overdue,
                OutstandingInvoiceCount = outstandingCount,
                OverdueInvoiceCount = overdueCount,
                CollectedThisMonth = collectedThisMonth,
                CollectedLastMonth = collectedLastMonth,
                CollectedAllTime = collectedAllTime
            };

            var pipeline = new PipelineSummary
            {
                QuoteCount = quoteCount,
                QuotesAwaitingDecisionCount = awaitingCount,
                QuotesAwaitingDecisionValue = awaitingValue,
                ContractCount = contractCount,
                LiveContractCount = liveCount,
                LiveContractValue = liveValue,
                ContractsAwaitingSignatureCount = awaitingSignatureCount,
                ContractsEndingSoonCount = endingSoonCount,
                InvoiceCount = await invoices.CountAsync(ct),
                CustomerCount = await _unitOfWork.Repo<Customer>().Query(asNoTracking: true).CountAsync(ct),
                ActiveProjectCount = await _unitOfWork.Repo<Project>().Query(asNoTracking: true)
                    .CountAsync(p => p.Status == ProjectStatus.Active, ct),

                // Null rather than 0% while nothing has been decided: a business
                // with no answered quotes has not lost them all.
                WinRatePercent = decidedCount == 0
                    ? null
                    : Math.Round((decimal)wonCount / decidedCount * 100m, 1)
            };

            return new BusinessOverview
            {
                Money = money,
                Pipeline = pipeline,
                QuotesAwaitingDecision = await QuotesAwaitingDecisionAsync(quotes, today, ct),
                ContractsAwaitingSignature = await ContractsAwaitingSignatureAsync(contracts, today, ct),
                ContractsEndingSoon = await ContractsEndingSoonAsync(contracts, today, ct),
                OverdueInvoices = await OverdueInvoicesAsync(lateQuery, today, ct),
                RevenueByMonth = await RevenueByMonthAsync(invoices, payments, today, ct)
            };
        }

        private static async Task<IReadOnlyList<AttentionItem>> QuotesAwaitingDecisionAsync(
            IQueryable<Quote> quotes, DateOnly today, CancellationToken ct)
        {
            var rows = await quotes
                .Where(q => q.Status == DocumentStatus.Sent)
                .OrderBy(q => q.IssuedAt ?? q.CreatedAt)
                .Take(8)
                .Select(q => new
                {
                    q.Id,
                    q.ProjectId,
                    q.QuoteNo,
                    Customer = q.Project.Customer.Name,
                    q.Status,
                    q.Currency,
                    Sent = q.IssuedAt ?? q.CreatedAt,
                    Amount = q.Items.Sum(i => i.Quantity * i.UnitPrice)
                })
                .ToListAsync(ct);

            return rows.Select(r => new AttentionItem
            {
                Id = r.Id,
                ProjectId = r.ProjectId,
                Reference = r.QuoteNo,
                CustomerName = r.Customer,
                Status = r.Status,
                Amount = r.Amount,
                Currency = r.Currency,
                Date = DateOnly.FromDateTime(r.Sent.UtcDateTime),
                DaysElapsed = today.DayNumber - DateOnly.FromDateTime(r.Sent.UtcDateTime).DayNumber
            }).ToList();
        }

        private static async Task<IReadOnlyList<AttentionItem>> ContractsAwaitingSignatureAsync(
            IQueryable<Contract> contracts, DateOnly today, CancellationToken ct)
        {
            var rows = await contracts
                .Where(c => c.Status == DocumentStatus.Sent)
                .OrderBy(c => c.CreatedAt)
                .Take(8)
                .Select(c => new
                {
                    c.Id,
                    c.ProjectId,
                    c.ContractNo,
                    Customer = c.Project.Customer.Name,
                    c.Status,
                    c.Currency,
                    c.CreatedAt,
                    Amount = c.Items.Sum(i => i.Quantity * i.UnitPrice)
                })
                .ToListAsync(ct);

            return rows.Select(r => new AttentionItem
            {
                Id = r.Id,
                ProjectId = r.ProjectId,
                Reference = r.ContractNo,
                CustomerName = r.Customer,
                Status = r.Status,
                Amount = r.Amount,
                Currency = r.Currency,
                Date = DateOnly.FromDateTime(r.CreatedAt.UtcDateTime),
                DaysElapsed = today.DayNumber - DateOnly.FromDateTime(r.CreatedAt.UtcDateTime).DayNumber
            }).ToList();
        }

        /// <summary>
        /// Live contracts whose end date falls inside the next 60 days — the point
        /// at which a renewal conversation has to start rather than be discovered.
        /// </summary>
        private static async Task<IReadOnlyList<AttentionItem>> ContractsEndingSoonAsync(
            IQueryable<Contract> contracts, DateOnly today, CancellationToken ct)
        {
            var horizon = today.AddDays(60);

            var rows = await contracts
                .Where(c =>
                    (c.Status == DocumentStatus.Signed || c.Status == DocumentStatus.Accepted) &&
                    c.EndDate != null && c.EndDate >= today && c.EndDate <= horizon)
                .OrderBy(c => c.EndDate)
                .Take(8)
                .Select(c => new
                {
                    c.Id,
                    c.ProjectId,
                    c.ContractNo,
                    Customer = c.Project.Customer.Name,
                    c.Status,
                    c.Currency,
                    c.EndDate,
                    Amount = c.Items.Sum(i => i.Quantity * i.UnitPrice)
                })
                .ToListAsync(ct);

            return rows.Select(r => new AttentionItem
            {
                Id = r.Id,
                ProjectId = r.ProjectId,
                Reference = r.ContractNo,
                CustomerName = r.Customer,
                Status = r.Status,
                Amount = r.Amount,
                Currency = r.Currency,
                Date = r.EndDate,
                DaysElapsed = r.EndDate is null ? null : today.DayNumber - r.EndDate.Value.DayNumber
            }).ToList();
        }

        private static async Task<IReadOnlyList<AttentionItem>> OverdueInvoicesAsync(
            IQueryable<Invoice> late, DateOnly today, CancellationToken ct)
        {
            var rows = await late
                .OrderBy(i => i.DueDate)
                .Take(8)
                .Select(i => new
                {
                    i.Id,
                    i.ProjectId,
                    i.InvoiceNo,
                    Customer = i.Project.Customer.Name,
                    i.Status,
                    i.Currency,
                    i.DueDate,
                    Amount = i.Totals != null
                        ? i.Totals.BalanceDue
                        : i.Items.Sum(x => x.Quantity * x.UnitPrice)
                })
                .ToListAsync(ct);

            return rows.Select(r => new AttentionItem
            {
                Id = r.Id,
                ProjectId = r.ProjectId,
                Reference = r.InvoiceNo,
                CustomerName = r.Customer,
                Status = r.Status,
                Amount = r.Amount,
                Currency = r.Currency,
                Date = r.DueDate,
                DaysElapsed = r.DueDate is null ? null : today.DayNumber - r.DueDate.Value.DayNumber
            }).ToList();
        }

        /// <summary>
        /// Invoiced against collected, for the last twelve months including this
        /// one. Months with no activity are returned as zero so the chart has an
        /// unbroken axis instead of skipping quiet periods.
        /// </summary>
        private static async Task<IReadOnlyList<MonthlyRevenuePoint>> RevenueByMonthAsync(
            IQueryable<Invoice> invoices,
            IQueryable<Payment> payments,
            DateOnly today,
            CancellationToken ct)
        {
            var firstMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(-11);
            var from = new DateTimeOffset(firstMonth.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

            var invoiced = await invoices
                .Where(i =>
                    i.IssueDate != null && i.IssueDate >= firstMonth &&
                    i.Status != DocumentStatus.Draft &&
                    i.Status != DocumentStatus.Void &&
                    i.Status != DocumentStatus.Cancelled)
                .GroupBy(i => new { i.IssueDate!.Value.Year, i.IssueDate!.Value.Month })
                .Select(g => new
                {
                    g.Key.Year,
                    g.Key.Month,
                    Amount = g.Sum(i => i.Totals != null
                        ? i.Totals.Total
                        : i.Items.Sum(x => x.Quantity * x.UnitPrice))
                })
                .ToListAsync(ct);

            var collected = await payments
                .Where(p => p.PaidAt >= from)
                .GroupBy(p => new { p.PaidAt!.Value.Year, p.PaidAt!.Value.Month })
                .Select(g => new { g.Key.Year, g.Key.Month, Amount = g.Sum(p => p.Amount) })
                .ToListAsync(ct);

            var points = new List<MonthlyRevenuePoint>(12);

            for (var i = 0; i < 12; i++)
            {
                var month = firstMonth.AddMonths(i);

                var billed = invoiced
                    .FirstOrDefault(x => x.Year == month.Year && x.Month == month.Month)?.Amount ?? 0m;

                var received = collected
                    .FirstOrDefault(x => x.Year == month.Year && x.Month == month.Month)?.Amount ?? 0m;

                points.Add(new MonthlyRevenuePoint(month.Year, month.Month, billed, received));
            }

            return points;
        }
    }
}
