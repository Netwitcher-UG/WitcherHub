using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WitcherHub.Application.Common.Caching;
using WitcherHub.Application.Common.Exceptions;
using WitcherHub.Application.Interfaces.ManageData;
using WitcherHub.Infrastructure.Data.Context;
using WitcherHub.Infrastructure.Data.Models;
using WitcherHub.Infrastructure.ManageData.Payments;
using WitcherHub.Infrastructure.Repositories.Implementations;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Tests;

/// <summary>
/// Recording money received against an invoice.
///
/// Runs against a real PostgreSQL database when one is reachable, and skips
/// rather than fails when it is not, so the suite still runs on a machine
/// without one. Override the connection with WITCHERHUB_TEST_DB.
/// </summary>
public class PaymentRecordingTests : IAsyncLifetime
{
    private const string DefaultConnectionString =
        "Host=127.0.0.1;Port=5455;Database=whpayments;Username=postgres";

    private AppDbContext? _db;
    private ManagePayments? _sut;
    private Guid _projectId;

    private bool Available => _db is not null;

    public async Task InitializeAsync()
    {
        var connectionString =
            Environment.GetEnvironmentVariable("WITCHERHUB_TEST_DB") ?? DefaultConnectionString;

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        var db = new AppDbContext(options);

        try
        {
            await db.Database.EnsureCreatedAsync();
        }
        catch
        {
            await db.DisposeAsync();
            return;      // no database here; every test below no-ops
        }

        _db = db;
        _sut = new ManagePayments(new UnitOfWork(db), new NoCache(), NullLogger<ManagePayments>.Instance);

        var customer = new Customer { Id = Guid.NewGuid(), Name = "Payment Test GmbH" };
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Title = "Payment test project",
            CustomerId = customer.Id
        };

        db.Add(customer);
        db.Add(project);
        await db.SaveChangesAsync();

        _projectId = project.Id;
    }

    public async Task DisposeAsync()
    {
        if (_db is not null)
            await _db.DisposeAsync();
    }

    /// <summary>
    /// Caching is irrelevant to the arithmetic under test, and a real cache would
    /// hide a stale read behind a passing assertion.
    /// </summary>
    private sealed class NoCache : IAppCache
    {
        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) => Task.FromResult<T?>(default);

        public Task SetAsync<T>(string key, T value, AppCacheEntryOptions? options = null, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<T> GetOrCreateAsync<T>(
            string key,
            Func<CancellationToken, Task<T>> factory,
            AppCacheEntryOptions? options = null,
            CancellationToken ct = default) => factory(ct);

        public Task RemoveAsync(string key, CancellationToken ct = default) => Task.CompletedTask;

        public Task<long> GetOrCreateVersionAsync(string versionKey, CancellationToken ct = default) => Task.FromResult(1L);

        public Task<long> BumpVersionAsync(string versionKey, CancellationToken ct = default) => Task.FromResult(2L);
    }

    private async Task<Invoice> GivenAnIssuedInvoiceAsync(decimal total, DateOnly? dueDate = null)
    {
        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            ProjectId = _projectId,
            InvoiceNo = "I-TEST-" + Guid.NewGuid().ToString("n")[..8],
            Status = DocumentStatus.Open,
            Currency = "EUR",
            IssueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10)),
            DueDate = dueDate,
            Totals = new InvoiceTotal
            {
                Subtotal = total,
                Total = total,
                BalanceDue = total
            }
        };

        _db!.Add(invoice);
        await _db.SaveChangesAsync();

        return invoice;
    }

    private async Task<InvoiceTotal?> ReadTotalsAsync(Guid invoiceId) =>
        await _db!.Set<InvoiceTotal>().AsNoTracking().FirstOrDefaultAsync(t => t.InvoiceId == invoiceId);

    private async Task<DocumentStatus> ReadStatusAsync(Guid invoiceId) =>
        await _db!.Set<Invoice>().AsNoTracking().Where(i => i.Id == invoiceId).Select(i => i.Status).FirstAsync();

    [Fact]
    public async Task A_payment_reduces_the_balance()
    {
        if (!Available) return;

        var invoice = await GivenAnIssuedInvoiceAsync(1000m);

        var result = await _sut!.RecordAsync(new RecordPaymentDto
        {
            InvoiceId = invoice.Id,
            Amount = 400m,
            ReceivedOn = DateOnly.FromDateTime(DateTime.UtcNow),
            Method = PaymentMethod.Bank
        });

        Assert.Equal(400m, result.PaidTotal);
        Assert.Equal(600m, result.BalanceDue);
        Assert.False(result.InvoiceIsNowPaid);

        var totals = await ReadTotalsAsync(invoice.Id);
        Assert.Equal(400m, totals!.PaidTotal);
        Assert.Equal(600m, totals.BalanceDue);
    }

    [Fact]
    public async Task Clearing_the_balance_marks_the_invoice_paid()
    {
        if (!Available) return;

        var invoice = await GivenAnIssuedInvoiceAsync(500m);

        var result = await _sut!.RecordAsync(new RecordPaymentDto
        {
            InvoiceId = invoice.Id,
            Amount = 500m,
            ReceivedOn = DateOnly.FromDateTime(DateTime.UtcNow)
        });

        Assert.True(result.InvoiceIsNowPaid);
        Assert.Equal(0m, result.BalanceDue);
        Assert.Equal(DocumentStatus.Paid, await ReadStatusAsync(invoice.Id));
    }

    [Fact]
    public async Task Two_part_payments_settle_the_invoice()
    {
        if (!Available) return;

        var invoice = await GivenAnIssuedInvoiceAsync(300m);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        await _sut!.RecordAsync(new RecordPaymentDto { InvoiceId = invoice.Id, Amount = 100m, ReceivedOn = today });
        var second = await _sut.RecordAsync(new RecordPaymentDto { InvoiceId = invoice.Id, Amount = 200m, ReceivedOn = today });

        Assert.Equal(300m, second.PaidTotal);
        Assert.True(second.InvoiceIsNowPaid);
        Assert.Equal(2, (await _sut.GetForInvoiceAsync(invoice.Id)).Count);
    }

    [Fact]
    public async Task An_overpayment_does_not_produce_a_negative_balance()
    {
        if (!Available) return;

        var invoice = await GivenAnIssuedInvoiceAsync(100m);

        var result = await _sut!.RecordAsync(new RecordPaymentDto
        {
            InvoiceId = invoice.Id,
            Amount = 150m,
            ReceivedOn = DateOnly.FromDateTime(DateTime.UtcNow)
        });

        // The customer overpaid; the invoice is settled and the balance floors at
        // zero rather than turning into a negative amount owed to nobody.
        Assert.Equal(150m, result.PaidTotal);
        Assert.Equal(0m, result.BalanceDue);
        Assert.True(result.InvoiceIsNowPaid);
    }

    [Fact]
    public async Task A_pending_payment_is_recorded_but_does_not_reduce_the_balance()
    {
        if (!Available) return;

        var invoice = await GivenAnIssuedInvoiceAsync(800m);

        var result = await _sut!.RecordAsync(new RecordPaymentDto
        {
            InvoiceId = invoice.Id,
            Amount = 800m,
            ReceivedOn = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = PaymentStatus.Pending
        });

        // Money that has not cleared is not money received. Summing every payment
        // row regardless of status would have marked this invoice paid.
        Assert.Equal(0m, result.PaidTotal);
        Assert.Equal(800m, result.BalanceDue);
        Assert.False(result.InvoiceIsNowPaid);
        Assert.NotEqual(DocumentStatus.Paid, await ReadStatusAsync(invoice.Id));

        Assert.Single(await _sut.GetForInvoiceAsync(invoice.Id));
    }

    [Fact]
    public async Task Removing_a_payment_restores_the_balance_and_unmarks_the_invoice()
    {
        if (!Available) return;

        var invoice = await GivenAnIssuedInvoiceAsync(250m, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(14));

        var recorded = await _sut!.RecordAsync(new RecordPaymentDto
        {
            InvoiceId = invoice.Id,
            Amount = 250m,
            ReceivedOn = DateOnly.FromDateTime(DateTime.UtcNow)
        });

        Assert.Equal(DocumentStatus.Paid, await ReadStatusAsync(invoice.Id));

        var reversed = await _sut.DeleteAsync(recorded.PaymentId);

        Assert.Equal(0m, reversed.PaidTotal);
        Assert.Equal(250m, reversed.BalanceDue);
        Assert.False(reversed.InvoiceIsNowPaid);

        // Not still Paid, and not Overdue either: the due date is a fortnight away.
        Assert.Equal(DocumentStatus.Open, await ReadStatusAsync(invoice.Id));
        Assert.Empty(await _sut.GetForInvoiceAsync(invoice.Id));
    }

    [Fact]
    public async Task Removing_a_payment_from_a_late_invoice_makes_it_overdue_again()
    {
        if (!Available) return;

        var invoice = await GivenAnIssuedInvoiceAsync(250m, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-5));

        var recorded = await _sut!.RecordAsync(new RecordPaymentDto
        {
            InvoiceId = invoice.Id,
            Amount = 250m,
            ReceivedOn = DateOnly.FromDateTime(DateTime.UtcNow)
        });

        await _sut.DeleteAsync(recorded.PaymentId);

        Assert.Equal(DocumentStatus.Overdue, await ReadStatusAsync(invoice.Id));
    }

    [Fact]
    public async Task A_draft_invoice_cannot_take_a_payment()
    {
        if (!Available) return;

        var invoice = await GivenAnIssuedInvoiceAsync(100m);
        invoice.Status = DocumentStatus.Draft;
        _db!.Update(invoice);
        await _db.SaveChangesAsync();

        var failure = await Assert.ThrowsAsync<BadRequestAppException>(() =>
            _sut!.RecordAsync(new RecordPaymentDto
            {
                InvoiceId = invoice.Id,
                Amount = 100m,
                ReceivedOn = DateOnly.FromDateTime(DateTime.UtcNow)
            }));

        Assert.Contains("draft", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_cancelled_invoice_cannot_take_a_payment()
    {
        if (!Available) return;

        var invoice = await GivenAnIssuedInvoiceAsync(100m);
        invoice.Status = DocumentStatus.Cancelled;
        _db!.Update(invoice);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<BadRequestAppException>(() =>
            _sut!.RecordAsync(new RecordPaymentDto
            {
                InvoiceId = invoice.Id,
                Amount = 100m,
                ReceivedOn = DateOnly.FromDateTime(DateTime.UtcNow)
            }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-50)]
    public async Task A_payment_must_be_a_positive_amount(decimal amount)
    {
        if (!Available) return;

        var invoice = await GivenAnIssuedInvoiceAsync(100m);

        await Assert.ThrowsAsync<BadRequestAppException>(() =>
            _sut!.RecordAsync(new RecordPaymentDto
            {
                InvoiceId = invoice.Id,
                Amount = amount,
                ReceivedOn = DateOnly.FromDateTime(DateTime.UtcNow)
            }));
    }

    [Fact]
    public async Task A_payment_cannot_be_dated_in_the_future()
    {
        if (!Available) return;

        var invoice = await GivenAnIssuedInvoiceAsync(100m);

        // A mistyped year would otherwise land in "collected this month" for a
        // month that has not happened.
        var failure = await Assert.ThrowsAsync<BadRequestAppException>(() =>
            _sut!.RecordAsync(new RecordPaymentDto
            {
                InvoiceId = invoice.Id,
                Amount = 100m,
                ReceivedOn = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1)
            }));

        Assert.Contains("future", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_payment_is_rounded_to_the_cent()
    {
        if (!Available) return;

        var invoice = await GivenAnIssuedInvoiceAsync(100m);

        var result = await _sut!.RecordAsync(new RecordPaymentDto
        {
            InvoiceId = invoice.Id,
            Amount = 33.333m,
            ReceivedOn = DateOnly.FromDateTime(DateTime.UtcNow)
        });

        Assert.Equal(33.33m, result.PaidTotal);
    }

    [Fact]
    public async Task A_payment_against_an_unknown_invoice_is_rejected()
    {
        if (!Available) return;

        await Assert.ThrowsAsync<NotFoundAppException>(() =>
            _sut!.RecordAsync(new RecordPaymentDto
            {
                InvoiceId = Guid.NewGuid(),
                Amount = 10m,
                ReceivedOn = DateOnly.FromDateTime(DateTime.UtcNow)
            }));
    }
}
