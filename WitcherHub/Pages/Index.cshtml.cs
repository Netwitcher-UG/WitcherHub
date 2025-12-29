using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WitcherHub.Application.Common.Exceptions;
using WitcherHub.Application.Common.Pagination;
using WitcherHub.Application.Interfaces.ManageData;
using WitcherHub.Application.Models.DTO.Customers;
using WitcherHub.Application.Models.View.Customers;
using WitcherHub.Pages.Models.UI;

namespace WitcherHub.Pages
{
    public class IndexModel : PageModel
    {
        private readonly ICustomer _customers;
        private readonly IValidator<CustomerDTOs> _createValidator;
        private readonly IValidator<UpdateBasicRequest> _validator;
        private readonly IValidator<CreateCustomerAddressDto> _createAddressValidator;
        private readonly IValidator<UpdateCustomerAddressDto> _updateAddressValidator;
        private readonly IValidator<DeleteCustomerAddressDto> _deleteAddressValidator;
        private readonly IValidator<SetDefaultCustomerAddressDto> _setDefaultAddressValidator;

        private readonly IValidator<CreateCustomerContactDto> _createContactValidator;
        private readonly IValidator<UpdateCustomerContactDto> _updateContactValidator;
        private readonly IValidator<DeleteCustomerContactDto> _deleteContactValidator;
        private readonly IValidator<SetPrimaryCustomerContactDto> _setPrimaryContactValidator;

        public IndexModel(
            ICustomer customers,
            IValidator<CustomerDTOs> createValidator,
            IValidator<UpdateBasicRequest> validator,
            IValidator<CreateCustomerAddressDto> createAddressValidator,
            IValidator<UpdateCustomerAddressDto> updateAddressValidator,
            IValidator<DeleteCustomerAddressDto> deleteAddressValidator,
            IValidator<SetDefaultCustomerAddressDto> setDefaultAddressValidator,
            IValidator<CreateCustomerContactDto> createContactValidator,
            IValidator<UpdateCustomerContactDto> updateContactValidator,
            IValidator<DeleteCustomerContactDto> deleteContactValidator,
            IValidator<SetPrimaryCustomerContactDto> setPrimaryContactValidator
        )
        {
            _customers = customers;
            _createValidator = createValidator;
            _validator = validator;

            _createAddressValidator = createAddressValidator;
            _updateAddressValidator = updateAddressValidator;
            _deleteAddressValidator = deleteAddressValidator;
            _setDefaultAddressValidator = setDefaultAddressValidator;

            _createContactValidator = createContactValidator;
            _updateContactValidator = updateContactValidator;
            _deleteContactValidator = deleteContactValidator;
            _setPrimaryContactValidator = setPrimaryContactValidator;
        }


        // query-string (pagination/search)
        [BindProperty(SupportsGet = true, Name = "p")] public new int Page { get; set; } = 1;
        [BindProperty(SupportsGet = true)] public int PageSize { get; set; } = 10;
        [BindProperty(SupportsGet = true, Name = "q")] public string? Search { get; set; }

        public TableCardVm ClientsTable { get; private set; } = new();

        // Create form DTO
        [BindProperty] public CustomerDto Customer { get; set; } = new();
        [BindProperty] public AddressDto Address { get; set; } = new();
        [BindProperty] public ContactDto Contact { get; set; } = new();

        public ModalVm CreateCustomerModal { get; private set; } = new();

        public async Task OnGetAsync(CancellationToken ct)
        {
            ViewData["p"] = Page;
            ViewData["pageSize"] = PageSize;
            ViewData["q"] = Search;
            await LoadTableAsync(ct);
            BuildCreateCustomerModal(autoOpen: false);
        }

        // =========================
        // POST: Create (normal form)
        // =========================
        public async Task<IActionResult> OnPostAsync(CancellationToken ct)
        {
            await LoadTableAsync(ct);

            var dto = new CustomerDTOs
            {
                Customer = Customer,
                Address = Address,
                Contact = Contact
            };

            var result = await _createValidator.ValidateAsync(dto, ct);

            if (!result.IsValid)
            {
                foreach (var err in result.Errors)
                    ModelState.AddModelError(err.PropertyName, err.ErrorMessage);

                BuildCreateCustomerModal(autoOpen: true);

                TempData["Toast.Type"] = "error";
                TempData["Toast.Title"] = "Validation";
                TempData["Toast.Message"] = "Please fix the highlighted fields.";

                return Page();
            }

            await _customers.CreateAsync(dto, ct);
            TempData["Toast.Type"] = "success";
            TempData["Toast.Title"] = "Success";
            TempData["Toast.Message"] = "Client added successfully.";
            return RedirectToPage("./Index", new { p = Page, pageSize = PageSize, q = Search });

        }

        // =========================
        // POST: Delete (normal form)
        // =========================
        public async Task<IActionResult> OnPostDeleteClientAsync(Guid clientId, CancellationToken ct)
        {
            if (clientId == Guid.Empty)
                throw new BadRequestAppException("Invalid client id.");

            await _customers.DeleteAsync(clientId, ct);
            TempData["Toast.Type"] = "success";
            TempData["Toast.Title"] = "Deleted";
            TempData["Toast.Message"] = "Client deleted successfully.";
            return RedirectToPage("./Index", new { p = Page, pageSize = PageSize, q = Search });
        }

        // =========================
        // GET: Details (Ajax JSON)
        // =========================
        public async Task<IActionResult> OnGetClientAsync(Guid id, CancellationToken ct)
        {
            if (id == Guid.Empty)
                throw new BadRequestAppException("Invalid client id.");

            var client = await _customers.GetCustomerAsync(id, ct);
            if (client is null)
                throw new NotFoundAppException("Customer not found.");

            return new JsonResult(client);
        }

        // =========================
        // POST: Update Basic (Ajax JSON)
        // =========================
        public async Task<IActionResult> OnPostUpdateBasicAsync([FromBody] UpdateBasicRequest? req, CancellationToken ct)
        {
            if (req is null)
                return BadRequest(new { message = "Body is null." });

            if (req.CustomerId == Guid.Empty)
                return BadRequest(new { message = "CustomerId is empty." });

            var vr = await _validator.ValidateAsync(req, ct);
            if (!vr.IsValid)
                return BadRequest(new
                {
                    message = "Validation failed",
                    errors = vr.Errors.Select(e => new { field = e.PropertyName, error = e.ErrorMessage })
                });

            var updateDto = new UpdateCustomerDto
            {
                Customer = new CustomerDTOs
                {
                    Customer = req.Customer,
                    Address = new AddressDto(),
                    Contact = new ContactDto()
                }
            };

            await _customers.UpdateAsync(req.CustomerId, updateDto, ct);

            var updated = await _customers.GetCustomerAsync(req.CustomerId, ct);
            return new JsonResult(updated);
        }

        public async Task<IActionResult> OnPostAddAddressAsync([FromBody] CreateCustomerAddressDto req, CancellationToken ct)
        {
            if (req is null)
                return BadRequest(new { message = "Body is null." });

            var vr = await _createAddressValidator.ValidateAsync(req, ct);
            if (!vr.IsValid)
                return BadRequest(new
                {
                    message = "Validation failed",
                    errors = vr.Errors.Select(e => new { field = e.PropertyName, error = e.ErrorMessage })
                });

            await _customers.CreateAddressAsync(req, ct);
            var updated = await _customers.GetCustomerAsync(req.CustomerId, ct);
            return new JsonResult(updated);
        }


        public async Task<IActionResult> OnPostDeleteAddressAsync([FromBody] DeleteCustomerAddressDto req, CancellationToken ct)
        {
            if (req is null)
                return BadRequest(new { message = "Body is null." });

            var vr = await _deleteAddressValidator.ValidateAsync(req, ct);
            if (!vr.IsValid)
                return BadRequest(new
                {
                    message = "Validation failed",
                    errors = vr.Errors.Select(e => new { field = e.PropertyName, error = e.ErrorMessage })
                });

            await _customers.DeleteAddressAsync(req, ct);
            var updated = await _customers.GetCustomerAsync(req.CustomerId, ct);
            return new JsonResult(updated);
        }


        public async Task<IActionResult> OnPostSetDefaultAddressAsync([FromBody] SetDefaultCustomerAddressDto req, CancellationToken ct)
        {
            if (req is null)
                return BadRequest(new { message = "Body is null." });

            var vr = await _setDefaultAddressValidator.ValidateAsync(req, ct);
            if (!vr.IsValid)
                return BadRequest(new
                {
                    message = "Validation failed",
                    errors = vr.Errors.Select(e => new { field = e.PropertyName, error = e.ErrorMessage })
                });

            await _customers.SetDefaultAddressAsync(req, ct);
            var updated = await _customers.GetCustomerAsync(req.CustomerId, ct);
            return new JsonResult(updated);
        }

        public async Task<IActionResult> OnPostUpdateAddressAsync([FromBody] UpdateCustomerAddressDto req, CancellationToken ct)
        {
            if (req is null)
                return BadRequest(new { message = "Body is null." });

            var vr = await _updateAddressValidator.ValidateAsync(req, ct);
            if (!vr.IsValid)
                return BadRequest(new
                {
                    message = "Validation failed",
                    errors = vr.Errors.Select(e => new { field = e.PropertyName, error = e.ErrorMessage })
                });

            await _customers.UpdateAddressAsync(req, ct);
            var updated = await _customers.GetCustomerAsync(req.CustomerId, ct);
            return new JsonResult(updated);
        }


        // -------- Contacts --------

        public async Task<IActionResult> OnPostAddContactAsync([FromBody] CreateCustomerContactDto req, CancellationToken ct)
        {
            if (req is null)
                return BadRequest(new { message = "Body is null." });

            var vr = await _createContactValidator.ValidateAsync(req, ct);
            if (!vr.IsValid)
                return BadRequest(new
                {
                    message = "Validation failed",
                    errors = vr.Errors.Select(e => new { field = e.PropertyName, error = e.ErrorMessage })
                });

            await _customers.CreateContactAsync(req, ct);
            var updated = await _customers.GetCustomerAsync(req.CustomerId, ct);
            return new JsonResult(updated);
        }


        public async Task<IActionResult> OnPostDeleteContactAsync([FromBody] DeleteCustomerContactDto req, CancellationToken ct)
        {
            if (req is null)
                return BadRequest(new { message = "Body is null." });

            var vr = await _deleteContactValidator.ValidateAsync(req, ct);
            if (!vr.IsValid)
                return BadRequest(new
                {
                    message = "Validation failed",
                    errors = vr.Errors.Select(e => new { field = e.PropertyName, error = e.ErrorMessage })
                });

            await _customers.DeleteContactAsync(req, ct);
            var updated = await _customers.GetCustomerAsync(req.CustomerId, ct);
            return new JsonResult(updated);
        }

        public async Task<IActionResult> OnPostSetPrimaryContactAsync([FromBody] SetPrimaryCustomerContactDto req, CancellationToken ct)
        {
            if (req is null)
                return BadRequest(new { message = "Body is null." });

            var vr = await _setPrimaryContactValidator.ValidateAsync(req, ct);
            if (!vr.IsValid)
                return BadRequest(new
                {
                    message = "Validation failed",
                    errors = vr.Errors.Select(e => new { field = e.PropertyName, error = e.ErrorMessage })
                });

            await _customers.SetPrimaryContactAsync(req, ct);
            var updated = await _customers.GetCustomerAsync(req.CustomerId, ct);
            return new JsonResult(updated);
        }
        public async Task<IActionResult> OnPostUpdateContactAsync([FromBody] UpdateCustomerContactDto req, CancellationToken ct)
        {
            if (req is null)
                return BadRequest(new { message = "Body is null." });

            var vr = await _updateContactValidator.ValidateAsync(req, ct);
            if (!vr.IsValid)
                return BadRequest(new
                {
                    message = "Validation failed",
                    errors = vr.Errors.Select(e => new { field = e.PropertyName, error = e.ErrorMessage })
                });

            await _customers.UpdateContactAsync(req, ct);
            var updated = await _customers.GetCustomerAsync(req.CustomerId, ct);
            return new JsonResult(updated);
        }


        // =========================
        // Table loading (real data)
        // =========================
        private async Task LoadTableAsync(CancellationToken ct)
        {
            var res = await _customers.GetCustomersAsync(Page, PageSize, Search, ct);

            ClientsTable = new TableCardVm
            {
                Title = "Clients",
                PrimaryButtonText = "Add Client",
                PrimaryButtonTarget = "#FormModal",
                SearchPlaceholder = "Search clients...",
                Pagination = new PaginationVm
                {
                    Page = res.Page,
                    PageSize = res.PageSize,
                    TotalItems = res.TotalItems,
                    SearchQuery = Search,
                    SearchParam = "q",
                    PageParam = "p",        
                    PageSizeParam = "pageSize"
                },
                Columns =
                {
                    new() { Header="Name", HeaderClass="ps-4", CellClass="ps-4 fw-semibold" },
                    new() { Header="Type" },
                    new() { Header="Phone" },
                    new() { Header="Email" },
                    new() { Header="City" },
                    new() { Header="Tax ID" },
                    new() { Header="Actions", HeaderClass="text-end pe-4", CellClass="text-end pe-4" },
                }
            };

            foreach (var c in res.Items)
            {
                ClientsTable.Rows.Add(new TableRowVm
                {
                    Cells =
                    {
                        Html(c.Name),
                        TypeBadge(c.Type.ToString()),
                        Html(c.Phone),
                        Html(c.Email),
                        Html(c.City),
                        Html(c.TaxId),
                        ActionsButtons(c.Id.ToString())
                    }
                });
            }
        }

        private void BuildCreateCustomerModal(bool autoOpen)
        {
            CreateCustomerModal = new ModalVm
            {
                Id = "FormModal",
                Title = "Add Client",
                SizeClass = "modal-lg",
                SubmitText = "Save",
                CancelText = "Cancel",
                Handler = null, // OnPostAsync
                BodyPartialPath = "~/Pages/Shared/Modals/_CreateCustomerFields.cshtml",
                BodyModel = this,
                AutoOpen = autoOpen
            };
        }

        // ========= helpers (same you had) =========
        private static Microsoft.AspNetCore.Html.IHtmlContent Html(string? text)
            => new Microsoft.AspNetCore.Html.HtmlString(System.Text.Encodings.Web.HtmlEncoder.Default.Encode(text ?? ""));

        private static Microsoft.AspNetCore.Html.IHtmlContent TypeBadge(string type)
        {
            var isCompany = type == "Company";
            var cls = isCompany ? "badge bg-success bg-opacity-10 text-success" : "badge bg-info bg-opacity-10 text-info";
            return new Microsoft.AspNetCore.Html.HtmlString($"<span class='{cls}'>{System.Text.Encodings.Web.HtmlEncoder.Default.Encode(type)}</span>");
        }

        private static string Enc(string? v) => System.Text.Encodings.Web.HtmlEncoder.Default.Encode(v ?? "");

        private static Microsoft.AspNetCore.Html.IHtmlContent ActionsButtons(string customerId) => new Microsoft.AspNetCore.Html.HtmlString($$"""
<button type="button"
        class="btn vc-icon-btn text-secondary"
        title="View"
        data-bs-toggle="modal"
        data-bs-target="#ViewClientModal"
        data-client-id="{{Enc(customerId)}}">
    <i class="material-icons-outlined">visibility</i>
</button>

<button type="button"
        class="btn vc-icon-btn text-danger"
        title="Delete"
        data-vc-action="table-delete"
        data-client-id="{{Enc(customerId)}}">
    <i class="material-icons-outlined">delete</i>
</button>
""");
    }

    
}
