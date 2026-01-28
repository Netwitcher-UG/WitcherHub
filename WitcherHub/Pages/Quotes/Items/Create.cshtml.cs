using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using WitcherHub.Application.Common.Exceptions;
using WitcherHub.Application.Interfaces.ManageData;
using WitcherHub.Application.Models.DTO.Quotes;
using WitcherHub.Application.Models.View.Quotes;
using WitcherHub.Infrastructure.Data;
using WitcherHub.Infrastructure.Data.Context;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Pages.Quotes.Items
{
    [Authorize]
    public class CreateModel : PageModel
    {
        private readonly IQuote _quotes;
        private readonly IServiceCatalog _services;
        private readonly IValidator<CreateQuoteItemDto> _validator;

        // مؤقتًا للضرائب فقط (إذا عندك ITaxRates منبدّلها)
        private readonly AppDbContext _db;

        public CreateModel(
            IQuote quotes,
            IServiceCatalog services,
            IValidator<CreateQuoteItemDto> validator,
            AppDbContext db)
        {
            _quotes = quotes;
            _services = services;
            _validator = validator;
            _db = db;
        }

        [BindProperty(SupportsGet = true)]
        public Guid QuoteId { get; set; }

        public QuoteViews.QuoteDetailsView? Quote { get; private set; }

        public List<SelectListItem> ServiceOptions { get; private set; } = new();
        public List<SelectListItem> TaxRateOptions { get; private set; } = new();

        [BindProperty]
        public Guid SelectedServiceId { get; set; }

        [BindProperty]
        public string ConfigJson { get; set; } = "{}";

        [BindProperty]
        public CreateQuoteItemDto Form { get; set; } = new();

        public async Task<IActionResult> OnGetAsync(CancellationToken ct)
        {
            if (QuoteId == Guid.Empty) return NotFound();

            Quote = await _quotes.GetQuoteAsync(QuoteId, ct);
            if (Quote is null) return NotFound();

            await LoadLookupsAsync(ct);

            Form.QuoteId = Quote.Id;
            Form.Item.Quantity = 1;
            Form.Item.Position = (Quote.Items?.Count ?? 0) + 1;
            ConfigJson = "{}";

            return Page();
        }

        public async Task<IActionResult> OnPostAsync(CancellationToken ct)
        {
            try
            {
                if (QuoteId == Guid.Empty) throw new BadRequestAppException("Invalid quote id.");

                Quote = await _quotes.GetQuoteAsync(QuoteId, ct);
                if (Quote is null) throw new NotFoundAppException("Quote not found.");

                if (Quote.Status != DocumentStatus.Draft)
                    throw new BadRequestAppException("Items are locked because quote is not Draft.");

                if (SelectedServiceId == Guid.Empty)
                {
                    ModelState.AddModelError(nameof(SelectedServiceId), "Service is required.");
                    await LoadLookupsAsync(ct);
                    return Page();
                }

                // Parse config JSON
                JsonDocument config;
                try
                {
                    config = JsonDocument.Parse(string.IsNullOrWhiteSpace(ConfigJson) ? "{}" : ConfigJson);
                }
                catch
                {
                    ModelState.AddModelError(nameof(ConfigJson), "Invalid JSON.");
                    await LoadLookupsAsync(ct);
                    return Page();
                }

                // Load service details (for snapshot fields)
                var service = await _services.GetServiceAsync(SelectedServiceId, ct);
                if (service is null) throw new NotFoundAppException("Service not found.");

                // Fill dto
                Form.QuoteId = QuoteId;
                Form.Item.ServiceId = SelectedServiceId;
                Form.Item.Config = config;

                // Snapshot (حتى يمر الـ validator + حتى لو backend لاحقًا صار يحسب لحاله)
                Form.Item.Title = service.Name ?? "";
                Form.Item.UnitPrice = service.BasePrice;

                // إذا بدك تضمن العملة
                // (عادة Quote.Currency هو الحاكم)
                // Form.Item.Currency? ما عندك على item، فخلاص.

                var vr = await _validator.ValidateAsync(Form, ct);
                if (!vr.IsValid)
                {
                    foreach (var err in vr.Errors)
                        ModelState.AddModelError("Form." + err.PropertyName, err.ErrorMessage);

                    await LoadLookupsAsync(ct);
                    return Page();
                }

                await _quotes.CreateItemAsync(Form, ct);

                TempData["Toast.Type"] = "success";
                TempData["Toast.Title"] = "Added";
                TempData["Toast.Message"] = "Line item added.";

                return RedirectToPage("/Quotes/Edit", new { id = QuoteId });
            }
            catch (Exception ex) when (ex is BadRequestAppException or NotFoundAppException)
            {
                TempData["Toast.Type"] = "error";
                TempData["Toast.Title"] = "Error";
                TempData["Toast.Message"] = ex.Message;

                await LoadLookupsAsync(ct);
                return Page();
            }
        }

        private async Task LoadLookupsAsync(CancellationToken ct)
        {
            // Services: عبر IServiceCatalog
            var result = await _services.GetServicesAsync(page: 1, pageSize: 500, search: null, ct: ct);

            // إذا PagedResult عندك اسمها مو Items، غيّر هذا السطر حسب الكلاس عندك
            var items = result.Items;

            ServiceOptions = items
                .OrderBy(s => s.Name)
                .Select(s => new SelectListItem(s.Name, s.Id.ToString()))
                .ToList();

            if (SelectedServiceId == Guid.Empty && ServiceOptions.Count > 0)
                SelectedServiceId = Guid.Parse(ServiceOptions[0].Value!);

            // Taxes: مؤقتًا من DB (إذا عندك Interface للضرائب منبدّلها)
            TaxRateOptions = await _db.TaxRates
                .Where(t => t.IsActive)
                .OrderBy(t => t.Name)
                .Select(t => new SelectListItem($"{t.Name} ({t.RatePercent}%)", t.Id.ToString()))
                .ToListAsync(ct);
        }
    }
}
