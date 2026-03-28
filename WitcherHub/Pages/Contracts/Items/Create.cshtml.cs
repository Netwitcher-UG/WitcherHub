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
using WitcherHub.Application.Models.DTO.Contracts;
using WitcherHub.Application.Models.View.Contracts;
using WitcherHub.Infrastructure.Data.Context;
using WitcherHub.Infrastructure.Data.Models;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Pages.Contracts.Items
{
    [Authorize]
    public class CreateModel : PageModel
    {
        private readonly IContract _contracts;
        private readonly IServiceCatalog _services;
        private readonly IValidator<CreateContractItemDto> _validator;
        private readonly AppDbContext _db;

        public CreateModel(
            IContract contracts,
            IServiceCatalog services,
            IValidator<CreateContractItemDto> validator,
            AppDbContext db)
        {
            _contracts = contracts;
            _services = services;
            _validator = validator;
            _db = db;
        }

        [BindProperty(SupportsGet = true)]
        public Guid ContractId { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? ReturnTo { get; set; }

        public ContractViews.ContractDetailsView? Contract { get; private set; }

        public List<SelectListItem> ServiceOptions { get; private set; } = new();

        [BindProperty]
        public Guid SelectedServiceId { get; set; }

        [BindProperty]
        public string ConfigJson { get; set; } = "{}";

        [BindProperty]
        public CreateContractItemDto Form { get; set; } = new();
        public sealed class PreviewPriceRequest
        {
            public Guid ServiceId { get; set; }
            public decimal Quantity { get; set; } = 1;
            public string? BillingCycle { get; set; } = "OneTime";
            public string? DiscountType { get; set; }
            public decimal? DiscountValue { get; set; }
            public string ConfigJson { get; set; } = "{}";
            public List<Guid> PricingRuleIds { get; set; } = new();
        }
        public async Task<IActionResult> OnGetAsync(CancellationToken ct)
        {
            if (ContractId == Guid.Empty) return NotFound();

            Contract = await _contracts.GetContractAsync(ContractId, ct);
            if (Contract is null) return NotFound();

            await LoadLookupsAsync(ct);

            Form.ContractId = Contract.Id;
            Form.Item.Quantity = 1;
            Form.Item.Position = (Contract.Items?.Count ?? 0) + 1;
            ConfigJson = "{}";
            Form.Item.UnitName = "";
            Form.Item.Description = "";
            // ✅ Toast لما يكون في redirect من flow إنشاء عقد بدون Quote
            if (string.Equals(ReturnTo, "items", StringComparison.OrdinalIgnoreCase))
            {
                TempData["Toast.Type"] = "info";
                TempData["Toast.Title"] = "Next step";
                TempData["Toast.Message"] = "You were redirected here to add contract positions. Please add at least one item to continue.";
            }

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

        // ✅ handler: ?handler=PricingRules&serviceId=...
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

                JsonDocument configDoc;
                try
                {
                    configDoc = JsonDocument.Parse(string.IsNullOrWhiteSpace(request.ConfigJson) ? "{}" : request.ConfigJson);
                }
                catch
                {
                    return BadRequest(new { ok = false, message = "Invalid JSON config." });
                }

                var service = await _services.GetServiceAsync(request.ServiceId, ct);
                if (service is null) return NotFound(new { ok = false, message = "Service not found." });

                // Parsing Enums
                var billingCycle = BillingCycle.OneTime;
                if (!string.IsNullOrWhiteSpace(request.BillingCycle))
                {
                    if (Enum.TryParse<BillingCycle>(request.BillingCycle, true, out var parsedBC)) billingCycle = parsedBC;
                    else if (int.TryParse(request.BillingCycle, out var bcInt) && Enum.IsDefined(typeof(BillingCycle), bcInt)) billingCycle = (BillingCycle)bcInt;
                }

                DiscountType? discountType = null;
                if (!string.IsNullOrWhiteSpace(request.DiscountType))
                {
                    if (Enum.TryParse<DiscountType>(request.DiscountType, true, out var parsedDT)) discountType = parsedDT;
                    else if (int.TryParse(request.DiscountType, out var dtInt) && Enum.IsDefined(typeof(DiscountType), dtInt)) discountType = (DiscountType)dtInt;
                }

                var item = new ContractItemDto
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

                var (breakdown, effectiveUnitPrice) = await _contracts.PreviewItemPriceAsync(item, ct);

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
            catch (Exception)
            {
                Response.StatusCode = 500;
                return new JsonResult(new { ok = false, message = "Internal error during preview." });
            }
        }
        public async Task<IActionResult> OnPostAsync(CancellationToken ct)
        {
            try
            {
                if (ContractId == Guid.Empty) throw new BadRequestAppException("Invalid contract id.");

                Contract = await _contracts.GetContractAsync(ContractId, ct);
                if (Contract is null) throw new NotFoundAppException("Contract not found.");

                if (Contract.Status != DocumentStatus.Draft)
                    throw new BadRequestAppException("Items are locked because contract is not Draft.");

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
                    Form.Item.Description = service.DefaultDescription;
                // ✅ Apply defaults + Validate config vs schema (if exists)
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

                    // keep pretty json
                    ConfigJson = JsonSerializer.Serialize(
                        finalConfig.RootElement,
                        new JsonSerializerOptions { WriteIndented = true });
                }

                // Fill dto
                Form.ContractId = ContractId;
                Form.Item.ServiceId = SelectedServiceId;
                Form.Item.Config = finalConfig;

                // Snapshot fields (validator + backend pricing will trust Service.BasePrice anyway)
                Form.Item.Title = service.Name ?? "";
                Form.Item.UnitPrice = service.BasePrice;

                // ✅ أهم سطر: خلّي AgreedPrice = null
                // لأننا بدنا backend يشتغل Auto Pricing مثل Quote items
                Form.Item.AgreedPrice = null;

                var vr = await _validator.ValidateAsync(Form, ct);
                if (!vr.IsValid)
                {
                    foreach (var err in vr.Errors)
                        ModelState.AddModelError("Form." + err.PropertyName, err.ErrorMessage);

                    await LoadLookupsAsync(ct);
                    return Page();
                }

                await _contracts.CreateItemAsync(Form, ct);

                TempData["Toast.Type"] = "success";
                TempData["Toast.Title"] = "Added";
                TempData["Toast.Message"] = "Position added.";

                // ✅ نفس فكرة Quotes/Edit -> هون equivalent هي Items/Manage
                return RedirectToPage("/Contracts/Items/Manage", new { contractId = ContractId });
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
        }
    }
}
