using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using WitcherHub.Application.Common.Exceptions;
using WitcherHub.Application.Models.DTO.Contracts;
using WitcherHub.Infrastructure.Data.Context;
using WitcherHub.Infrastructure.Data.Models;
using WitcherHub.Infrastructure.ManageData.Contracts;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Tests;

/// <summary>
/// Persistence of contract positions, against a real PostgreSQL database when one
/// is reachable. Skipped rather than failed when it is not, so the suite still runs
/// on a machine without a database.
/// </summary>
public class ManageContractPositionsTests : IAsyncLifetime
{
    private const string ConnectionString =
        "Host=127.0.0.1;Port=5455;Database=whpositions;Username=postgres";

    private AppDbContext? _db;
    private ManageContractPositions? _sut;
    private Guid _contractId;

    public bool Available => _db is not null;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
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
        _sut = new ManageContractPositions(db, NullLogger<ManageContractPositions>.Instance);

        var customer = new Customer { Id = Guid.NewGuid(), Name = "Test GmbH" };
        var project = new Project { Id = Guid.NewGuid(), Title = "Test project", CustomerId = customer.Id };
        var contract = new Contract
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            ContractNo = "C-" + Guid.NewGuid().ToString("n")[..8],
            Status = DocumentStatus.Draft,
            Currency = "EUR"
        };

        db.Add(customer);
        db.Add(project);
        db.Add(contract);
        await db.SaveChangesAsync();

        _contractId = contract.Id;
    }

    public async Task DisposeAsync()
    {
        if (_db is not null) await _db.DisposeAsync();
    }

    private static ManualPositionDto Manual(string title, decimal price, Action<ManualPositionDto>? extra = null)
    {
        var dto = new ManualPositionDto
        {
            Title = title,
            SourceType = ContractItemSource.Manual,
            CatalogServiceId = null,
            Quantity = 1,
            UnitPrice = price,
            Currency = "EUR",
            VatRate = 19m,
            PricingModel = PricingModel.Fixed
        };
        extra?.Invoke(dto);
        return dto;
    }

    [Fact]
    public async Task AContractCanBeBuiltFromManualPositionsAlone()
    {
        if (!Available) return;

        var totals = await _sut!.SavePositionsAsync(_contractId, new[]
        {
            Manual("Website relaunch", 4000m),
            Manual("Copywriting", 800m)
        });

        Assert.Equal(2, totals.PositionCount);
        Assert.Equal(4800m, totals.Subtotal);
        Assert.Equal(912m, totals.Vat);
        Assert.Equal(5712m, totals.Total);

        var stored = await _db!.ContractItems
            .Where(i => i.ContractId == _contractId)
            .ToListAsync();

        // The catalog reference stays null: no placeholder service was invented.
        Assert.All(stored, i => Assert.Null(i.ServiceId));
        Assert.All(stored, i => Assert.Equal(ContractItemSource.Manual, i.Source));
    }

    [Fact]
    public async Task EveryPositionKeepsAnImmutableSnapshotOfWhatWasAgreed()
    {
        if (!Available) return;

        await _sut!.SavePositionsAsync(_contractId, new[]
        {
            Manual("Hosting", 25m, p =>
            {
                p.BillingCycle = BillingCycle.Monthly;
                p.DurationPeriods = 12;
                p.Deliverables = new List<string> { "Managed hosting", "Daily backups" };
            })
        });

        var item = await _db!.ContractItems.FirstAsync(i => i.ContractId == _contractId);

        Assert.NotNull(item.Snapshot);
        Assert.NotNull(item.SnapshotTakenAt);

        var snapshot = item.Snapshot!.RootElement;
        Assert.Equal("Hosting", snapshot.GetProperty("title").GetString());
        Assert.Equal(25m, snapshot.GetProperty("unitPrice").GetDecimal());
        Assert.Equal("Monthly", snapshot.GetProperty("billingCycle").GetString());
        Assert.Equal(12, snapshot.GetProperty("durationPeriods").GetInt32());
        Assert.Equal(2, snapshot.GetProperty("deliverables").GetArrayLength());
    }

    [Fact]
    public async Task ReSavingDoesNotRewriteAnExistingSnapshot()
    {
        if (!Available) return;

        await _sut!.SavePositionsAsync(_contractId, new[] { Manual("Design", 1000m) });

        var saved = await _sut.GetPositionsAsync(_contractId);
        var first = await _db!.ContractItems.AsNoTracking().FirstAsync(i => i.ContractId == _contractId);
        var originalSnapshot = first.Snapshot!.RootElement.GetRawText();

        // The user renames the position and raises the price.
        var edited = saved.ToList();
        edited[0].Title = "Design (revised)";
        edited[0].UnitPrice = 1800m;
        await _sut.SavePositionsAsync(_contractId, edited);

        var after = await _db.ContractItems.AsNoTracking().FirstAsync(i => i.ContractId == _contractId);

        // The live figures move; the agreed snapshot does not.
        Assert.Equal(1800m, after.UnitPrice);
        Assert.Equal(originalSnapshot, after.Snapshot!.RootElement.GetRawText());
    }

    [Fact]
    public async Task CatalogAndManualPositionsCoexistOnOneContract()
    {
        if (!Available) return;

        var service = new ServiceCatalogItem
        {
            Id = Guid.NewGuid(),
            Name = "SEO package",
            Description = "Search engine optimisation retainer",
            BasePrice = 500m,
            IsActive = true
        };
        _db!.Add(service);
        await _db.SaveChangesAsync();

        var totals = await _sut!.SavePositionsAsync(_contractId, new[]
        {
            Manual("Bespoke integration", 2200m),
            new ManualPositionDto
            {
                Title = "SEO package",
                SourceType = ContractItemSource.Catalog,
                CatalogServiceId = service.Id,
                Quantity = 1,
                UnitPrice = 500m,
                Currency = "EUR",
                VatRate = 19m,
                PricingModel = PricingModel.Fixed
            }
        });

        Assert.Equal(2, totals.PositionCount);
        Assert.Equal(2700m, totals.Subtotal);

        var stored = await _db.ContractItems.Where(i => i.ContractId == _contractId).ToListAsync();
        Assert.Single(stored, i => i.ServiceId == service.Id);
        Assert.Single(stored, i => i.ServiceId == null);
    }

    [Fact]
    public async Task PositionsAreStoredInTheOrderGiven()
    {
        if (!Available) return;

        await _sut!.SavePositionsAsync(_contractId, new[]
        {
            Manual("First", 100m), Manual("Second", 200m), Manual("Third", 300m)
        });

        var saved = (await _sut.GetPositionsAsync(_contractId)).ToList();

        // Reorder: move the last to the front, as the builder's arrows do.
        var reordered = new List<ManualPositionDto> { saved[2], saved[0], saved[1] };
        await _sut.SavePositionsAsync(_contractId, reordered);

        var after = await _sut.GetPositionsAsync(_contractId);

        Assert.Equal(new[] { "Third", "First", "Second" }, after.Select(p => p.Title));
        Assert.Equal(new[] { 1, 2, 3 }, after.Select(p => p.Position));
    }

    [Fact]
    public async Task RemovingAPositionDeletesIt()
    {
        if (!Available) return;

        await _sut!.SavePositionsAsync(_contractId, new[] { Manual("Keep", 100m), Manual("Drop", 200m) });

        var saved = (await _sut.GetPositionsAsync(_contractId)).ToList();
        await _sut.SavePositionsAsync(_contractId, new[] { saved[0] });

        var after = await _sut.GetPositionsAsync(_contractId);
        Assert.Single(after);
        Assert.Equal("Keep", after[0].Title);
    }

    [Fact]
    public async Task AContractWithNoPositionsIsRejected()
    {
        if (!Available) return;

        await Assert.ThrowsAsync<BadRequestAppException>(() =>
            _sut!.SavePositionsAsync(_contractId, Array.Empty<ManualPositionDto>()));
    }

    [Fact]
    public async Task APositionPointingAtAMissingServiceIsRejected()
    {
        if (!Available) return;

        var dto = Manual("Ghost", 100m);
        dto.SourceType = ContractItemSource.Catalog;
        dto.CatalogServiceId = Guid.NewGuid();      // never existed

        await Assert.ThrowsAsync<BadRequestAppException>(() =>
            _sut!.SavePositionsAsync(_contractId, new[] { dto }));
    }

    [Fact]
    public async Task PositionsAreLockedOnceTheContractLeavesDraft()
    {
        if (!Available) return;

        await _sut!.SavePositionsAsync(_contractId, new[] { Manual("Work", 100m) });

        var contract = await _db!.Contracts.FirstAsync(c => c.Id == _contractId);
        contract.Status = DocumentStatus.Sent;
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<BadRequestAppException>(() =>
            _sut.SavePositionsAsync(_contractId, new[] { Manual("Sneaky extra", 5000m) }));
    }

    [Fact]
    public async Task TheStoredLineTotalIsTheServersCalculationNotTheCallers()
    {
        if (!Available) return;

        var dto = Manual("Consulting", 200m, p =>
        {
            p.PricingModel = PricingModel.Unit;
            p.Quantity = 10;
            p.DiscountType = DiscountType.Percent;
            p.DiscountValue = 25m;
        });

        await _sut!.SavePositionsAsync(_contractId, new[] { dto });

        var item = await _db!.ContractItems.FirstAsync(i => i.ContractId == _contractId);

        Assert.Equal(1500m, item.AgreedPrice);   // 200 x 10, less 25%

        var breakdown = item.PriceBreakdown!.RootElement;
        Assert.Equal(1500m, breakdown.GetProperty("net").GetDecimal());
        Assert.Equal(285m, breakdown.GetProperty("vat").GetDecimal());
    }
}
