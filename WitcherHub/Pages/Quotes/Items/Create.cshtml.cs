using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using WitcherHub.Application.Common.ConfigSchema;
using WitcherHub.Application.Common.Exceptions;
using WitcherHub.Application.Interfaces.ManageData;
using WitcherHub.Application.Models.DTO.Quotes;
using WitcherHub.Application.Models.View.Quotes;
using WitcherHub.Infrastructure.Data.Context;
using WitcherHub.Infrastructure.Data.Models;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Pages.Quotes.Items
{
    [Authorize]
    public class CreateModel : PageModel
    {
        private readonly IQuote _quotes;
        private readonly IServiceCatalog _services;
        private readonly IValidator<CreateQuoteItemDto> _validator;
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

        [BindProperty(SupportsGet = true)]
        public string? ReturnUrl { get; set; }

   
        public sealed class PreviewPriceRequest
        {
            public Guid ServiceId { get; set; }
            public decimal Quantity { get; set; } = 1;

            // تغيير النوع إلى string لتفادي مشاكل الـ Parsing التلقائي
            public string? BillingCycle { get; set; } = "OneTime";
            public string? DiscountType { get; set; }

            public decimal? DiscountValue { get; set; }
            public string ConfigJson { get; set; } = "{}";
            public List<Guid> PricingRuleIds { get; set; } = new();
        }

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

        // ✅ handler: ?handler=ServiceSchema&serviceId=...
        public async Task<IActionResult> OnGetServiceSchemaAsync(Guid serviceId, CancellationToken ct)
        {
            if (serviceId == Guid.Empty)
                return new ContentResult { Content = "null", ContentType = "application/json" };

            var service = await _services.GetServiceAsync(serviceId, ct);
            if (service?.ConfigSchema is null)
                return new ContentResult { Content = "null", ContentType = "application/json" };

            return new ContentResult
            {
                Content = service.ConfigSchema.RootElement.GetRawText(),
                ContentType = "application/json"
            };
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
                JsonDocument configDoc;
                try
                {
                    configDoc = JsonDocument.Parse(string.IsNullOrWhiteSpace(ConfigJson) ? "{}" : ConfigJson);
                }
                catch
                {
                    ModelState.AddModelError(nameof(ConfigJson), "Invalid JSON.");
                    await LoadLookupsAsync(ct);
                    return Page();
                }

                // Load service details (includes ConfigSchema + BasePrice)
                var service = await _services.GetServiceAsync(SelectedServiceId, ct);
                if (service is null) throw new NotFoundAppException("Service not found.");

                if (string.IsNullOrWhiteSpace(Form.Item.UnitName) && !string.IsNullOrWhiteSpace(service.DefaultUnitName))
                    Form.Item.UnitName = service.DefaultUnitName;

                if (string.IsNullOrWhiteSpace(Form.Item.Description) && !string.IsNullOrWhiteSpace(service.DefaultDescription))
                    Form.Item.Description = service.DefaultDescription; // ✅ Apply defaults + Validate config vs schema (if exists)
                var finalConfig = configDoc;

                if (service.ConfigSchema is not null)
                {
                    finalConfig = ConfigSchemaLite.ApplyDefaults(service.ConfigSchema, configDoc);
                    var errs = ConfigSchemaLite.Validate(service.ConfigSchema, finalConfig);

                    if (errs.Count > 0)
                    {
                        foreach (var e in errs)
                            ModelState.AddModelError(nameof(ConfigJson), $"{e.Field}: {e.Message}");

                        ConfigJson = JsonSerializer.Serialize(
                            finalConfig.RootElement,
                            new JsonSerializerOptions { WriteIndented = true });

                        await LoadLookupsAsync(ct);
                        return Page();
                    }

                    // keep pretty json (optional)
                    ConfigJson = JsonSerializer.Serialize(
                        finalConfig.RootElement,
                        new JsonSerializerOptions { WriteIndented = true });
                }

                // Fill dto
                Form.QuoteId = QuoteId;
                Form.Item.ServiceId = SelectedServiceId;
                Form.Item.Config = finalConfig;

                // Snapshot fields (for validator only; backend pricing recalculates anyway)
                // Snapshot fields
                Form.Item.Title = service.Name ?? "";
                Form.Item.UnitPrice = service.BasePrice;

                // keep user-entered values if موجودة، otherwise take defaults from service
                if (string.IsNullOrWhiteSpace(Form.Item.UnitName))
                    Form.Item.UnitName = service.DefaultUnitName ?? "";

                if (string.IsNullOrWhiteSpace(Form.Item.Description))
                    Form.Item.Description = service.DefaultDescription ?? "";

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
                TempData["Toast.Message"] = "Position added.";

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
            var result = await _services.GetServicesAsync(page: 1, pageSize: 500, search: null, ct: ct);
            var items = result.Items;

            ServiceOptions = items
                .OrderBy(s => s.Name)
                .Select(s => new SelectListItem(s.Name, s.Id.ToString()))
                .ToList();

            //TaxRateOptions = await _db.TaxRates
            //    .Where(t => t.IsActive)
            //    .OrderBy(t => t.Name)
            //    .Select(t => new SelectListItem($"{t.Name} ({t.RatePercent}%)", t.Id.ToString()))
            //    .ToListAsync(ct);
        }

        public async Task<IActionResult> OnGetPricingRulesAsync(Guid serviceId, CancellationToken ct)
        {
            if (serviceId == Guid.Empty)
                return new JsonResult(Array.Empty<object>());

            var rules = await _db.Set<PricingRule>()
                .AsNoTracking()
                .Where(r =>
                    r.ServiceId == serviceId &&
                    r.IsActive &&
                    r.Scope == "LINE_ITEM")
                .OrderBy(r => r.Priority)
                .Select(r => new
                {
                    id = r.Id,
                    name = r.Name,
                    label = r.Label,
                    action = r.Action.ToString(),
                    priority = r.Priority
                })
                .ToListAsync(ct);

            return new JsonResult(rules);
        }
        public async Task<IActionResult> OnPostPreviewPriceAsync(
        [FromBody] PreviewPriceRequest request,
        CancellationToken ct)
        {
            try
            {
                if (request is null || request.ServiceId == Guid.Empty)
                    return BadRequest(new { ok = false, message = "Service is required." });

                // 1. التحقق من صحة الـ JSON الخاص بالإعدادات
                JsonDocument configDoc;
                try
                {
                    configDoc = JsonDocument.Parse(
                        string.IsNullOrWhiteSpace(request.ConfigJson) ? "{}" : request.ConfigJson);
                }
                catch
                {
                    return BadRequest(new { ok = false, message = "Invalid JSON config." });
                }

                var service = await _services.GetServiceAsync(request.ServiceId, ct);
                if (service is null)
                    return NotFound(new { ok = false, message = "Service not found." });

                // 2. معالجة الـ BillingCycle (دعم النص أو الرقم)
                var billingCycle = BillingCycle.OneTime;
                if (!string.IsNullOrWhiteSpace(request.BillingCycle))
                {
                    if (Enum.TryParse<BillingCycle>(request.BillingCycle, true, out var parsedBillingCycle))
                        billingCycle = parsedBillingCycle;
                    else if (int.TryParse(request.BillingCycle, out var billingCycleInt) && Enum.IsDefined(typeof(BillingCycle), billingCycleInt))
                        billingCycle = (BillingCycle)billingCycleInt;
                }

                // 3. معالجة الـ DiscountType (دعم النص أو الرقم)
                DiscountType? discountType = null;
                if (!string.IsNullOrWhiteSpace(request.DiscountType))
                {
                    if (Enum.TryParse<DiscountType>(request.DiscountType, true, out var parsedDiscountType))
                        discountType = parsedDiscountType;
                    else if (int.TryParse(request.DiscountType, out var discountTypeInt) && Enum.IsDefined(typeof(DiscountType), discountTypeInt))
                        discountType = (DiscountType)discountTypeInt;
                }

                // 4. بناء الـ DTO وإرساله لمحرك التسعير
                var item = new QuoteItemDto
                {
                    ServiceId = request.ServiceId,
                    Title = service.Name ?? "",
                    Quantity = request.Quantity < 0 ? 0 : request.Quantity,
                    UnitPrice = service.BasePrice,
                    Config = configDoc,
                    DiscountType = discountType,
                    DiscountValue = request.DiscountValue,
                    BillingCycle = billingCycle,
                    PricingRuleIds = request.PricingRuleIds ?? new List<Guid>()
                };

                var (breakdown, effectiveUnitPrice) = await _quotes.PreviewItemPriceAsync(item, ct);

                return new JsonResult(new
                {
                    ok = true,
                    serviceName = service.Name ?? "",
                    defaultUnitName = service.DefaultUnitName ?? "",
                    defaultDescription = service.DefaultDescription ?? "",
                    effectiveUnitPrice,
                    configJson = JsonSerializer.Serialize(item.Config.RootElement),
                    breakdown = JsonSerializer.Deserialize<object>(breakdown.RootElement.GetRawText())
                });
            }
            catch (BadRequestAppException ex)
            {
                Response.StatusCode = 400;
                return new JsonResult(new { ok = false, message = ex.Message });
            }
            catch (Exception ex) // معالجة شاملة للأخطاء غير المتوقعة
            {
                Response.StatusCode = 500;
                return new JsonResult(new { ok = false, message = "Internal server error during preview." });
            }
        }
    
    }

}
