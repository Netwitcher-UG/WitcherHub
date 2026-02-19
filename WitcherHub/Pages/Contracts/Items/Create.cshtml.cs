using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Text.Json;
using WitcherHub.Application.Common.Exceptions;
using WitcherHub.Application.Interfaces.ManageData;
using WitcherHub.Application.Models.DTO.Contracts;
using WitcherHub.Application.Models.View.Contracts;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Pages.Contracts.Items
{
    [Authorize]
    public class CreateModel : PageModel
    {
        private readonly IContract _contracts;
        private readonly IServiceCatalog _services;
        private readonly IValidator<CreateContractItemDto> _validator;

        public CreateModel(
            IContract contracts,
            IServiceCatalog services,
            IValidator<CreateContractItemDto> validator)
        {
            _contracts = contracts;
            _services = services;
            _validator = validator;
        }

        [BindProperty(SupportsGet = true)]
        public Guid ContractId { get; set; }

        public ContractViews.ContractDetailsView? Contract { get; private set; }

        public List<SelectListItem> ServiceOptions { get; private set; } = new();

        [BindProperty]
        public Guid SelectedServiceId { get; set; }

        [BindProperty]
        public string ConfigJson { get; set; } = "{}";

        [BindProperty]
        public CreateContractItemDto Form { get; set; } = new();
        [BindProperty(SupportsGet = true)]
        public string? ReturnTo { get; set; }
        public async Task<IActionResult> OnGetAsync(CancellationToken ct)
        {
            if (ContractId == Guid.Empty) return NotFound();

            Contract = await _contracts.GetContractAsync(ContractId, ct);
            if (Contract is null) return NotFound();

            await LoadLookupsAsync(ct);

            Form.ContractId = Contract.Id;
            Form.Item.Position = (Contract.Items?.Count ?? 0) + 1;
            Form.Item.AgreedPrice = null;
            ConfigJson = "{}";

            // ✅ Toast عند الوصول من التحويل
            if (string.Equals(ReturnTo, "items", StringComparison.OrdinalIgnoreCase))
            {
                TempData["Toast.Type"] = "info";
                TempData["Toast.Title"] = "Next step";
                TempData["Toast.Message"] = "You were redirected here to add contract Positions. Please add at least one item to continue.";
            }

            return Page();
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

                Form.ContractId = ContractId;
                Form.Item.ServiceId = SelectedServiceId;
                Form.Item.Config = config;

                // Set title from service
                Form.Item.Title = service.Name ?? "";

                // Default price from service if user didn't enter one
                if (Form.Item.AgreedPrice is null)
                    Form.Item.AgreedPrice = service.BasePrice;

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

                if (string.Equals(ReturnTo, "items", StringComparison.OrdinalIgnoreCase))
                {
                    return RedirectToPage("/Contracts/Items/Create", new { contractId = ContractId, returnTo = "items" });

                }

                return RedirectToPage("/Contracts/Details", new { id = ContractId });

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
        }
    }
}
