using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Text;
using System.Text.Json;
using WitcherHub.Application.Common.Exceptions;
using WitcherHub.Application.Interfaces;
using WitcherHub.Application.Interfaces.ManageData;
using WitcherHub.Application.Models.DTO.Contracts;
using WitcherHub.Application.Models.View.Contracts;
using WitcherHub.Application.Models.View.Project;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Pages.Contracts.Items
{
    [Authorize]
    public class ManageModel : PageModel
    {
        private readonly IContract _contracts;
        private readonly IServiceCatalog _services;
        private readonly IProject _projects;
        private readonly IContractDocumentGenerator _contractDocumentGenerator;
        private readonly ILogger<ManageModel> _logger;

        public ManageModel(
            IContract contracts,
            IServiceCatalog services,
            IProject projects,
            IContractDocumentGenerator contractDocumentGenerator,
            ILogger<ManageModel> logger)
        {
            _contracts = contracts;
            _services = services;
            _projects = projects;
            _contractDocumentGenerator = contractDocumentGenerator;
            _logger = logger;
        }

        [BindProperty(SupportsGet = true)]
        public Guid ContractId { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? ReturnTo { get; set; }

        public ContractViews.ContractDetailsView? Contract { get; private set; }

        public List<SelectListItem> ServiceOptions { get; private set; } = new();

        public bool IsLocked { get; private set; }

        public int NextPosition { get; private set; } = 1;

        // Create fields
        [BindProperty] public Guid CreateServiceId { get; set; }
        [BindProperty] public string? CreateTitle { get; set; }
        [BindProperty] public decimal? CreateAgreedPrice { get; set; }
        [BindProperty] public int CreatePosition { get; set; } = 1;
        [BindProperty] public string CreateConfigJson { get; set; } = "{}";

    
 
        // Edit fields (✅ like Quote)
        [BindProperty] public Guid EditItemId { get; set; }
        [BindProperty] public Guid EditServiceId { get; set; }
        [BindProperty] public string? EditTitle { get; set; }
        [BindProperty] public decimal? EditAgreedPrice { get; set; }
        [BindProperty] public decimal EditQuantity { get; set; } = 1m;
        [BindProperty] public decimal EditUnitPrice { get; set; } = 0m;
        [BindProperty] public BillingCycle EditBillingCycle { get; set; } = BillingCycle.OneTime;

        [BindProperty] public DiscountType? EditDiscountType { get; set; }
        [BindProperty] public decimal? EditDiscountValue { get; set; }

        [BindProperty] public int EditPosition { get; set; } = 1;
        [BindProperty] public string EditConfigJson { get; set; } = "{}";

        // Delete field
        [BindProperty] public Guid ItemId { get; set; }
        // Header
        [BindProperty] public DocumentStatus HeaderStatus { get; set; }
        [BindProperty] public DateOnly? HeaderStartDate { get; set; }
        [BindProperty] public DateOnly? HeaderEndDate { get; set; }
        [BindProperty] public string? HeaderTerms { get; set; }
        [BindProperty] public InvoiceSendMode HeaderInvoiceSendMode { get; set; } = InvoiceSendMode.Automatic;

        public async Task<IActionResult> OnGetAsync(CancellationToken ct)
        {
            if (ContractId == Guid.Empty) return NotFound();

            Contract = await _contracts.GetContractAsync(ContractId, ct);
            if (Contract is null) return NotFound();
            HeaderStatus = Contract.Status;
            HeaderStartDate = Contract.StartDate;
            HeaderEndDate = Contract.EndDate;
            HeaderTerms = Contract.Terms;
            HeaderInvoiceSendMode = Contract.InvoiceSendMode;
            await LoadServicesAsync(ct);

            IsLocked = IsContractLocked(Contract);

            NextPosition = (Contract.Items?.Count ?? 0) + 1;

            

            return Page();
        }
        public async Task<IActionResult> OnPostSaveHeaderAsync(CancellationToken ct)
        {
            try
            {
                if (ContractId == Guid.Empty)
                    throw new BadRequestAppException("Invalid contract id.");

                Contract = await _contracts.GetContractAsync(ContractId, ct);
                if (Contract is null)
                    throw new NotFoundAppException("Contract not found.");

             
                if (IsContractLocked(Contract))
                    throw new BadRequestAppException("Contract is locked.");
                await _contracts.UpdateHeaderAsync(
                    ContractId,
                    HeaderStatus,
                    HeaderStartDate,
                    HeaderEndDate,
                    HeaderTerms,
                    HeaderInvoiceSendMode,
                    ct);

                TempData["Toast.Type"] = "success";
                TempData["Toast.Title"] = "Saved";
                TempData["Toast.Message"] = "Header updated.";

                return RedirectToPage("/Contracts/Items/Manage", new { contractId = ContractId });
            }
            catch (Exception ex) when (ex is BadRequestAppException or NotFoundAppException)
            {
                TempData["Toast.Type"] = "error";
                TempData["Toast.Title"] = "Error";
                TempData["Toast.Message"] = ex.Message;

                return RedirectToPage("/Contracts/Items/Manage", new { contractId = ContractId });
            }
        }
        public async Task<IActionResult> OnPostCreateItemAsync(CancellationToken ct)
        {
            try
            {
                if (ContractId == Guid.Empty) throw new BadRequestAppException("Invalid contract id.");

                Contract = await _contracts.GetContractAsync(ContractId, ct);
                if (Contract is null) throw new NotFoundAppException("Contract not found.");

                if (IsContractLocked(Contract))
                    throw new BadRequestAppException("Contract is locked.");

                if (CreateServiceId == Guid.Empty)
                    throw new BadRequestAppException("Service is required.");

                var service = await _services.GetServiceAsync(CreateServiceId, ct);
                if (service is null) throw new NotFoundAppException("Service not found.");

                JsonDocument cfg;
                try
                {
                    cfg = JsonDocument.Parse(string.IsNullOrWhiteSpace(CreateConfigJson) ? "{}" : CreateConfigJson);
                }
                catch
                {
                    throw new BadRequestAppException("Invalid JSON.");
                }

                var dto = new CreateContractItemDto();
                dto.ContractId = ContractId;
                dto.Item.ServiceId = CreateServiceId;
                dto.Item.Title = string.IsNullOrWhiteSpace(CreateTitle) ? (service.Name ?? "") : CreateTitle!.Trim();
                dto.Item.Config = cfg;
                dto.Item.AgreedPrice = CreateAgreedPrice ?? service.BasePrice;
                dto.Item.Position = CreatePosition > 0 ? CreatePosition : (Contract.Items?.Count ?? 0) + 1;

                await _contracts.CreateItemAsync(dto, ct);

                TempData["Toast.Type"] = "success";
                TempData["Toast.Title"] = "Added";
                TempData["Toast.Message"] = "Position added.";

                return RedirectToPage("/Contracts/Items/Manage", new { contractId = ContractId });
            }
            catch (Exception ex) when (ex is BadRequestAppException or NotFoundAppException)
            {
                TempData["Toast.Type"] = "error";
                TempData["Toast.Title"] = "Error";
                TempData["Toast.Message"] = ex.Message;

                return RedirectToPage("/Contracts/Items/Manage", new { contractId = ContractId });
            }
        }

        public async Task<IActionResult> OnPostUpdateItemAsync(CancellationToken ct)
        {
            try
            {
                if (ContractId == Guid.Empty) throw new BadRequestAppException("Invalid contract id.");
                if (EditItemId == Guid.Empty) throw new BadRequestAppException("Invalid item id.");

                Contract = await _contracts.GetContractAsync(ContractId, ct);
                if (Contract is null) throw new NotFoundAppException("Contract not found.");

                if (IsContractLocked(Contract))
                    throw new BadRequestAppException("Contract is locked.");

                if (EditServiceId == Guid.Empty)
                    throw new BadRequestAppException("Service is required.");

                var service = await _services.GetServiceAsync(EditServiceId, ct);
                if (service is null) throw new NotFoundAppException("Service not found.");

                JsonDocument cfg;
                try
                {
                    cfg = JsonDocument.Parse(string.IsNullOrWhiteSpace(EditConfigJson) ? "{}" : EditConfigJson);
                }
                catch
                {
                    throw new BadRequestAppException("Invalid JSON.");
                }

                var dto = new UpdateContractItemDto
                {
                    ContractId = ContractId,
                    ItemId = EditItemId
                };

                dto.Item.ServiceId = EditServiceId;
                dto.Item.Title = string.IsNullOrWhiteSpace(EditTitle) ? (service.Name ?? "") : EditTitle!.Trim();
                dto.Item.Config = cfg;

                dto.Item.Quantity = EditQuantity <= 0 ? 1 : EditQuantity;
                dto.Item.UnitPrice = EditUnitPrice;                 // (server may override to effective unit)
                dto.Item.BillingCycle = EditBillingCycle;

                dto.Item.DiscountType = EditDiscountType;
                dto.Item.DiscountValue = EditDiscountValue;

                dto.Item.Position = EditPosition > 0 ? EditPosition : 1;

                // ✅ IMPORTANT: like Quote => trigger recalculation
                dto.Item.AgreedPrice = null;

                await _contracts.UpdateItemAsync(dto, ct);

                TempData["Toast.Type"] = "success";
                TempData["Toast.Title"] = "Saved";
                TempData["Toast.Message"] = "Position updated.";

                return RedirectToPage("/Contracts/Items/Manage", new { contractId = ContractId });
            }
            catch (Exception ex) when (ex is BadRequestAppException or NotFoundAppException)
            {
                TempData["Toast.Type"] = "error";
                TempData["Toast.Title"] = "Error";
                TempData["Toast.Message"] = ex.Message;

                return RedirectToPage("/Contracts/Items/Manage", new { contractId = ContractId });
            }
        }

        public async Task<IActionResult> OnPostDeleteItemAsync(CancellationToken ct)
        {
            try
            {
                if (ContractId == Guid.Empty) throw new BadRequestAppException("Invalid contract id.");
                if (ItemId == Guid.Empty) throw new BadRequestAppException("Invalid item id.");

                Contract = await _contracts.GetContractAsync(ContractId, ct);
                if (Contract is null) throw new NotFoundAppException("Contract not found.");

                if (IsContractLocked(Contract))
                    throw new BadRequestAppException("Contract is locked.");

                var dto = new DeleteContractItemDto
                {
                    ContractId = ContractId,
                    ItemId = ItemId
                };

                await _contracts.DeleteItemAsync(dto, ct);

                TempData["Toast.Type"] = "success";
                TempData["Toast.Title"] = "Deleted";
                TempData["Toast.Message"] = "Position deleted.";

                return RedirectToPage("/Contracts/Items/Manage", new { contractId = ContractId });
            }
            catch (Exception ex) when (ex is BadRequestAppException or NotFoundAppException)
            {
                TempData["Toast.Type"] = "error";
                TempData["Toast.Title"] = "Error";
                TempData["Toast.Message"] = ex.Message;

                return RedirectToPage("/Contracts/Items/Manage", new { contractId = ContractId });
            }
        }

        public async Task<IActionResult> OnPostGenerateContractAsync(CancellationToken ct)
        {
            try
            {
                if (ContractId == Guid.Empty) throw new BadRequestAppException("Invalid contract id.");

                var contract = await _contracts.GetContractAsync(ContractId, ct);
                if (contract is null) throw new NotFoundAppException("Contract not found.");

                if (IsContractLocked(contract))
                    throw new BadRequestAppException("Contract is locked.");

                var itemsCount = contract.Items?.Count ?? 0;
                if (itemsCount == 0)
                    throw new BadRequestAppException("Please add at least one Position first.");

                var prj = await _projects.GetProjectAsync(contract.ProjectId, ct);
                if (prj is null) throw new NotFoundAppException("Project not found.");

                // ✅ generate structured from GPT (لكن لا تكتب العقد النهائي هنا)
                var req = BuildGenerateRequest(prj, contract);
                var doc = await _contractDocumentGenerator.GenerateAsync(req, ct);

                var update = new UpdateContractDto
                {
                    Contract = new ContractDto
                    {
                        ProjectId = contract.ProjectId,
                        Currency = contract.Currency,
                        Status = DocumentStatus.Draft,
                        StartDate = contract.StartDate,
                        EndDate = contract.EndDate,

                        Terms = contract.Terms,              
                        TermsStructured = doc.Structured,    
                        SignedAt = contract.SignedAt
                    },
                    Items = null
                };

                await _contracts.UpdateAsync(contract.Id, update, ct);

                return RedirectToPage("/Contracts/Override", new { id = contract.Id });
            }
            catch (Exception ex) when (ex is BadRequestAppException or NotFoundAppException)
            {
                TempData["Toast.Type"] = "error";
                TempData["Toast.Title"] = "Error";
                TempData["Toast.Message"] = ex.Message;

                return RedirectToPage("/Contracts/Items/Manage", new { contractId = ContractId });
            }
        }
        private async Task LoadServicesAsync(CancellationToken ct)
        {
            var result = await _services.GetServicesAsync(page: 1, pageSize: 500, search: null, ct: ct);
            var items = result.Items;

            ServiceOptions = items
                .OrderBy(s => s.Name)
                .Select(s => new SelectListItem(s.Name, s.Id.ToString()))
                .ToList();
        }

        private static bool IsContractLocked(ContractViews.ContractDetailsView c)
        {
            var isSigned = c.SignedAt is not null;
            var st = c.Status.ToString().ToLowerInvariant();
            return isSigned || st == "signed";
        }

        private GenerateContractDocumentRequest BuildGenerateRequest(
            ProjectViews.ProjectDetailsView prj,
            ContractViews.ContractDetailsView contract)
        {
            var customerName = prj.Customer?.Name ?? "";
            var customerEmail = prj.Customer?.Email ?? "";

            var customerBlock = new StringBuilder();
            customerBlock.AppendLine($"Name/Firma: {customerName}");

            customerBlock.AppendLine("Adresse:");
            customerBlock.AppendLine("PLZ/Ort:");

            if (!string.IsNullOrWhiteSpace(customerEmail))
                customerBlock.AppendLine($"E-Mail: {customerEmail}");

            var customerBlockText = customerBlock.ToString().TrimEnd();

            var lines = (contract.Items ?? new List<ContractViews.ContractItemItemView>())
                .OrderBy(x => x.Position)
                .Select(x => new ContractServiceLineDto
                {
                    Position = x.Position,
                    Title = x.Title,
                    ServiceName = x.ServiceName,
                    AgreedPrice = x.AgreedPrice,
                    Config = x.Config is null
                        ? new Dictionary<string, object>()
                        : JsonSerializer.Deserialize<Dictionary<string, object>>(x.Config.RootElement.GetRawText()) ?? new()
                })
                .ToList();

            return new GenerateContractDocumentRequest
            {
                ContractNo = contract.ContractNo,
                ProjectTitle = prj.Title ?? "Project",
                Currency = contract.Currency ?? "EUR",
                StartDate = contract.StartDate ?? prj.StartDate,
                EndDate = contract.EndDate ?? prj.EndDate,

                SignerName = "",
                SignerEmail = customerEmail,

                LeaveCustomerFieldsBlank = false,
                IncludePricesInServicesSection = true,
                CustomerBlockOverride = customerBlockText,

                Services = lines
            };
        }
    }
}
