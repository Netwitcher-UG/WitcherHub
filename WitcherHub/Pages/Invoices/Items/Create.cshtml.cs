using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using WitcherHub.Application.Common.Exceptions;
using WitcherHub.Application.Interfaces.ManageData;
using WitcherHub.Application.Models.DTO.Invoices;
using WitcherHub.Application.Models.View.Invoices;
using WitcherHub.Infrastructure.Data.Context;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Pages.Invoices.Items
{
    [Authorize]
    public class CreateModel : PageModel
    {
        private readonly IInvoice _invoices;
        private readonly IServiceCatalog _services;
        private readonly IValidator<CreateInvoiceItemDto> _validator;

        // taxes lookup temporary
        private readonly AppDbContext _db;

        public CreateModel(
            IInvoice invoices,
            IServiceCatalog services,
            IValidator<CreateInvoiceItemDto> validator,
            AppDbContext db)
        {
            _invoices = invoices;
            _services = services;
            _validator = validator;
            _db = db;
        }

        [BindProperty(SupportsGet = true)]
        public Guid InvoiceId { get; set; }

        public InvoiceViews.InvoiceDetailsView? Invoice { get; private set; }

        public List<SelectListItem> ServiceOptions { get; private set; } = new();
        public List<SelectListItem> TaxRateOptions { get; private set; } = new();

        [BindProperty]
        public Guid SelectedServiceId { get; set; }

        [BindProperty]
        public string ConfigJson { get; set; } = "{}";

        [BindProperty]
        public CreateInvoiceItemDto Form { get; set; } = new();

        public async Task<IActionResult> OnGetAsync(CancellationToken ct)
        {
            if (InvoiceId == Guid.Empty) return NotFound();

            Invoice = await _invoices.GetInvoiceAsync(InvoiceId, ct);
            if (Invoice is null) return NotFound();

            await LoadLookupsAsync(ct);

            Form.InvoiceId = Invoice.Id;
            Form.Item.Quantity = 1;
            Form.Item.Position = (Invoice.Items?.Count ?? 0) + 1;
            ConfigJson = "{}";

            return Page();
        }

        public async Task<IActionResult> OnPostAsync(CancellationToken ct)
        {
            try
            {
                if (InvoiceId == Guid.Empty) throw new BadRequestAppException("Invalid invoice id.");

                Invoice = await _invoices.GetInvoiceAsync(InvoiceId, ct);
                if (Invoice is null) throw new NotFoundAppException("Invoice not found.");

                if (Invoice.Status != DocumentStatus.Draft)
                    throw new BadRequestAppException("Items are locked because invoice is not Draft.");

                if (SelectedServiceId == Guid.Empty)
                {
                    ModelState.AddModelError(nameof(SelectedServiceId), "Service is required.");
                    await LoadLookupsAsync(ct);
                    return Page();
                }

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

                var service = await _services.GetServiceAsync(SelectedServiceId, ct);
                if (service is null) throw new NotFoundAppException("Service not found.");

                Form.InvoiceId = InvoiceId;
                Form.Item.ServiceId = SelectedServiceId;
                Form.Item.Config = config;

                // snapshot
                Form.Item.Title = service.Name ?? "";
                Form.Item.UnitPrice = service.BasePrice;

                var vr = await _validator.ValidateAsync(Form, ct);
                if (!vr.IsValid)
                {
                    foreach (var err in vr.Errors)
                        ModelState.AddModelError("Form." + err.PropertyName, err.ErrorMessage);

                    await LoadLookupsAsync(ct);
                    return Page();
                }

                await _invoices.CreateItemAsync(Form, ct);

                TempData["Toast.Type"] = "success";
                TempData["Toast.Title"] = "Added";
                TempData["Toast.Message"] = "Line item added.";

                return RedirectToPage("/Invoices/Edit", new { id = InvoiceId });
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

            if (SelectedServiceId == Guid.Empty && ServiceOptions.Count > 0)
                SelectedServiceId = Guid.Parse(ServiceOptions[0].Value!);

            TaxRateOptions = await _db.TaxRates
                .Where(t => t.IsActive)
                .OrderBy(t => t.Name)
                .Select(t => new SelectListItem($"{t.Name} ({t.RatePercent}%)", t.Id.ToString()))
                .ToListAsync(ct);
        }
    }
}