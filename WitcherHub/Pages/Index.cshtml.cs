using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Globalization;
using WitcherHub.Application.Common.Exceptions;
using WitcherHub.Application.Common.Pagination;
using WitcherHub.Application.Interfaces;
using WitcherHub.Application.Interfaces.ManageData;
using WitcherHub.Application.Models.DTO.Customers;
using WitcherHub.Application.Models.View.Customers;
using WitcherHub.Pages.Models.UI;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Pages
{
    public class IndexModel : PageModel
    {
        private readonly ICustomer _customers;
        private readonly ILexwareSyncService _lexwareSync;
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

        private const string DuplicateEmailMessagePrefix =
            "Email address is already used by another client: ";

        private const string DuplicateEmailInRequestMessagePrefix =
            "Duplicate email in request: ";

        private sealed record FieldValidationError(string Field, string Error);

        public IndexModel(
            ICustomer customers,
            ILexwareSyncService lexwareSync,
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
            _lexwareSync = lexwareSync;
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
        public List<SelectListItem> CountryOptions { get; private set; } = new();

        private static readonly Lazy<IReadOnlyList<(string Code, string Name)>> _allCountries =
    new(() =>
    {
        var list = CultureInfo.GetCultures(CultureTypes.SpecificCultures)
            .Where(ci => !ci.CultureTypes.HasFlag(CultureTypes.UserCustomCulture))
            .Select(ci =>
            {
                try
                {
                    var r = new RegionInfo(ci.Name);
                    return (Code: r.TwoLetterISORegionName, Name: r.EnglishName);
                }
                catch { return (Code: "", Name: ""); }
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Code) && !string.IsNullOrWhiteSpace(x.Name))
            .Distinct()
            .OrderBy(x => x.Name)
            .ToList();

        return list;
    });

        private void EnsureCountryOptions()
        {
            if (string.IsNullOrWhiteSpace(Address.CountryCode))
                Address.CountryCode = "DE";

            CountryOptions = _allCountries.Value
                .Select(c => new SelectListItem
                {
                    Value = c.Code,
                    Text = c.Name,
                    Selected = string.Equals(c.Code, Address.CountryCode, StringComparison.OrdinalIgnoreCase)
                })
                .ToList();

            // ✅ عبّي الاسم كمان (Country) من الكود
            Address.Country = CountryOptions.FirstOrDefault(x => x.Value == Address.CountryCode)?.Text ?? "Germany";
        }




        

        public ModalVm CreateCustomerModal { get; private set; } = new();

        public async Task OnGetAsync(CancellationToken ct)
        {
            EnsureEmailSlot();
            EnsureCountryOptions();
            ViewData["p"] = Page;
            ViewData["pageSize"] = PageSize;
            ViewData["q"] = Search;
            await LoadTableAsync(ct);
            BuildCreateCustomerModal(autoOpen: false);
        }
        private void EnsureEmailSlot()
        {
            Customer.EmailAddresses ??= new List<EmailAddressDto>();

            if (Customer.EmailAddresses.Count == 0)
                Customer.EmailAddresses.Add(new EmailAddressDto { Kind = "business" });
        }
        // =========================
        // POST: Create (normal form)
        // =========================
        public async Task<IActionResult> OnPostAsync(CancellationToken ct)
        {
            EnsureEmailSlot();
            await LoadTableAsync(ct);
            EnsureCountryOptions();

            // Remove validation errors for fields that do not belong to the
            // selected customer type and derive the first address display name.
            NormalizeCreateModel();

            var dto = new CustomerDTOs
            {
                Customer = Customer,
                Address = Address,
                Contact = Contact
            };

            var validationResult = await _createValidator.ValidateAsync(dto, ct);

            // Some validators may still contain rules shared by both customer
            // types. Ignore only fields that are not applicable to the selected type.
            var createValidationErrors = validationResult.Errors
                .Where(error => IsRelevantCreateValidationField(error.PropertyName))
                .ToList();

            var requiredAddressErrors = GetRequiredAddressErrors(Address);
            var requiredContactErrors = Customer.Type == CustomerType.Company
                ? GetRequiredCreateCompanyContactErrors(Contact)
                : new List<FieldValidationError>();

            foreach (var error in createValidationErrors)
                ModelState.AddModelError(error.PropertyName, error.ErrorMessage);

            foreach (var error in requiredAddressErrors)
                ModelState.AddModelError(error.Field, error.Error);

            foreach (var error in requiredContactErrors)
                ModelState.AddModelError(error.Field, error.Error);

            if (!ModelState.IsValid
                || requiredAddressErrors.Count > 0
                || requiredContactErrors.Count > 0)
            {
                EnsureCountryOptions();
                BuildCreateCustomerModal(autoOpen: true);

                SetValidationToast(
                    GetFirstModelStateError()
                    ?? "Please check the required fields.");

                return Page();
            }

            try
            {
                await _customers.CreateAsync(dto, ct);
            }
            catch (BadRequestAppException ex)
                when (TryGetDuplicateEmailInRequest(ex.Message, out var duplicateEmail))
            {
                AddCreateEmailError(
                    duplicateEmail,
                    "The same email cannot be entered more than once.");

                EnsureCountryOptions();
                BuildCreateCustomerModal(autoOpen: true);
                SetValidationToast("The same email cannot be entered more than once.");

                return Page();
            }
            catch (BadRequestAppException ex)
                when (TryGetDuplicateEmail(ex.Message, out var duplicateEmail))
            {
                AddCreateEmailError(
                    duplicateEmail,
                    "This email address is already used by another client.");

                EnsureCountryOptions();
                BuildCreateCustomerModal(autoOpen: true);
                SetValidationToast("This email address is already used by another client.");

                return Page();
            }
            catch (BadRequestAppException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);

                EnsureCountryOptions();
                BuildCreateCustomerModal(autoOpen: true);
                SetValidationToast(ex.Message);

                return Page();
            }

            TempData["Toast.Type"] = "success";
            TempData["Toast.Title"] = "Success";
            TempData["Toast.Message"] = "Client added successfully.";

            return RedirectToPage(
                "./Index",
                new { p = Page, pageSize = PageSize, q = Search });
        }

        private void NormalizeCreateModel()
        {
            if (Customer.Type == CustomerType.Company)
            {
                // A company has a company name. Customer first/last name fields
                // belong only to an individual customer.
                Customer.FirstName = null;
                Customer.LastName = null;

                ModelState.Remove("Customer.FirstName");
                ModelState.Remove("Customer.LastName");

                // The create modal uses one full contact-name field. Structured
                // contact fields belong to the separate contact editor and must not
                // invalidate company creation.
                ModelState.Remove("Contact.Salutation");
                ModelState.Remove("Contact.FirstName");
                ModelState.Remove("Contact.LastName");
            }
            else
            {
                // An individual has first/last name. Customer.Name belongs only
                // to a company; the persisted display name is derived in the service.
                Customer.Name = null;
                ModelState.Remove("Customer.Name");

                // Company contacts are not part of an individual customer create.
                var contactKeys = ModelState.Keys
                    .Where(key => key.Equals("Contact", StringComparison.OrdinalIgnoreCase)
                                  || key.StartsWith("Contact.", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var key in contactKeys)
                    ModelState.Remove(key);
            }

            // Country is derived from CountryCode by EnsureCountryOptions().
            // It is not a separate user input, so discard any binder error for it.
            ModelState.Remove("Address.Country");

            if (string.IsNullOrWhiteSpace(Address.FullNameOrCompany))
            {
                Address.FullNameOrCompany = Customer.Type == CustomerType.Company
                    ? (Customer.Name ?? string.Empty).Trim()
                    : $"{Customer.FirstName} {Customer.LastName}".Trim();

                // ModelState takes precedence over the model when the page is
                // rendered again, so remove the originally posted empty value.
                ModelState.Remove("Address.FullNameOrCompany");
            }
        }

        private bool IsRelevantCreateValidationField(string? propertyName)
        {
            if (string.IsNullOrWhiteSpace(propertyName))
                return true;

            if (Customer.Type == CustomerType.Company)
            {
                return !propertyName.Equals("Customer.FirstName", StringComparison.OrdinalIgnoreCase)
                       && !propertyName.Equals("Customer.LastName", StringComparison.OrdinalIgnoreCase)
                       && !propertyName.Equals("Contact.Salutation", StringComparison.OrdinalIgnoreCase)
                       && !propertyName.Equals("Contact.FirstName", StringComparison.OrdinalIgnoreCase)
                       && !propertyName.Equals("Contact.LastName", StringComparison.OrdinalIgnoreCase);
            }

            return !propertyName.Equals("Customer.Name", StringComparison.OrdinalIgnoreCase)
                   && !propertyName.Equals("Contact", StringComparison.OrdinalIgnoreCase)
                   && !propertyName.StartsWith("Contact.", StringComparison.OrdinalIgnoreCase);
        }

        private string? GetFirstModelStateError()
        {
            foreach (var value in ModelState.Values)
            {
                foreach (var error in value.Errors)
                {
                    if (!string.IsNullOrWhiteSpace(error.ErrorMessage))
                        return error.ErrorMessage;

                    var exceptionMessage = error.Exception?.GetBaseException().Message;
                    if (!string.IsNullOrWhiteSpace(exceptionMessage))
                        return exceptionMessage;
                }
            }

            return null;
        }

        private void SetValidationToast(string message)
        {
            TempData["Toast.Type"] = "error";
            TempData["Toast.Title"] = "Validation";
            TempData["Toast.Message"] = message;
        }

        // =========================
        // POST: Delete (normal form)
        // =========================
        public async Task<IActionResult> OnPostDeleteClientAsync(Guid clientId, CancellationToken ct)
        {
            if (clientId == Guid.Empty)
                throw new BadRequestAppException("Invalid client id.");

            // Server-side protection: never rely only on the JavaScript check.
            var projects = await _customers.GetCustomerProjectsAsync(clientId, ct);
            if (projects.Count > 0)
            {
                TempData["Toast.Type"] = "error";
                TempData["Toast.Title"] = "Delete not allowed";
                TempData["Toast.Message"] =
                    $"This client cannot be deleted because it is linked to {projects.Count} project(s) and related project data.";

                return RedirectToPage("./Index", new { p = Page, pageSize = PageSize, q = Search });
            }

            await _customers.DeleteAsync(clientId, ct);
            TempData["Toast.Type"] = "success";
            TempData["Toast.Title"] = "Deleted";
            TempData["Toast.Message"] = "Client deleted successfully.";
            return RedirectToPage("./Index", new { p = Page, pageSize = PageSize, q = Search });
        }

        // =========================
        // GET: Delete information (Ajax JSON)
        // =========================
        public async Task<IActionResult> OnGetDeleteInfoAsync(Guid clientId, CancellationToken ct)
        {
            if (clientId == Guid.Empty)
                return BadRequest(new { message = "Invalid client id." });

            var client = await _customers.GetCustomerAsync(clientId, ct);
            if (client is null)
                return NotFound(new { message = "Client not found." });

            var projects = await _customers.GetCustomerProjectsAsync(clientId, ct);
            var projectNames = projects
                .Select(x => x.Title)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Take(5)
                .ToList();

            return new JsonResult(new
            {
                canDelete = projects.Count == 0,
                clientName = client.Name,
                projectCount = projects.Count,
                projectNames,
                message = projects.Count == 0
                    ? "Are you sure you want to delete this client?"
                    : $"This client is linked to {projects.Count} project(s) and related project data, so it cannot be deleted. Remove or reassign the projects first."
            });
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

            // ✅ المشاريع: (الطريقة الأفضل) تجيبها من السيرفس/ManageCustomer
            var projects = await _customers.GetCustomerProjectsAsync(id, ct); // سنضيفها بالخطوة القادمة

            return new JsonResult(new
            {
                customer = client,
                projects
            });
        }

        // =========================
        // POST: Lexware Import Contacts (Ajax JSON)
        // =========================
        public async Task<IActionResult> OnPostLexwareImportContactsAsync(CancellationToken ct)
        {
            var res = await _lexwareSync.ImportAllContactsAsync(ct);
            return new JsonResult(res);
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
                Customer = req.Customer
            };

            try
            {
                await _customers.UpdateAsync(req.CustomerId, updateDto, ct);
            }
            catch (BadRequestAppException ex)
                when (TryGetDuplicateEmail(ex.Message, out var duplicateEmail))
            {
                return BuildUpdateEmailValidationError(
                    req.Customer?.EmailAddresses,
                    duplicateEmail,
                    "This email address is already used by another client.");
            }
            catch (BadRequestAppException ex)
                when (TryGetDuplicateEmailInRequest(ex.Message, out var duplicateEmail))
            {
                return BuildUpdateEmailValidationError(
                    req.Customer?.EmailAddresses,
                    duplicateEmail,
                    "The same email cannot be entered more than once.");
            }

            var updated = await _customers.GetCustomerAsync(req.CustomerId, ct);
            return new JsonResult(updated);
        }


        public async Task<IActionResult> OnPostAddAddressAsync([FromBody] CreateCustomerAddressDto req, CancellationToken ct)
        {
            if (req is null)
                return BadRequest(new { message = "Body is null." });

            var vr = await _createAddressValidator.ValidateAsync(req, ct);
            var errors = vr.Errors
                .Select(e => new FieldValidationError(e.PropertyName, e.ErrorMessage))
                .Concat(GetRequiredAddressErrors(req.Address))
                .GroupBy(e => e.Field, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            if (errors.Count > 0)
                return BadRequest(new
                {
                    message = "All address fields are required.",
                    errors = errors.Select(e => new { field = e.Field, error = e.Error })
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
            var errors = vr.Errors
                .Select(e => new FieldValidationError(e.PropertyName, e.ErrorMessage))
                .Concat(GetRequiredAddressErrors(req.Address))
                .GroupBy(e => e.Field, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            if (errors.Count > 0)
                return BadRequest(new
                {
                    message = "All address fields are required.",
                    errors = errors.Select(e => new { field = e.Field, error = e.Error })
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
            var errors = vr.Errors
                .Select(e => new FieldValidationError(e.PropertyName, e.ErrorMessage))
                .Concat(GetRequiredStructuredCompanyContactErrors(req.Contact))
                .GroupBy(e => e.Field, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            if (errors.Count > 0)
                return BadRequest(new
                {
                    message = "All company contact fields are required.",
                    errors = errors.Select(e => new { field = e.Field, error = e.Error })
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
            var errors = vr.Errors
                .Select(e => new FieldValidationError(e.PropertyName, e.ErrorMessage))
                .Concat(GetRequiredStructuredCompanyContactErrors(req.Contact))
                .GroupBy(e => e.Field, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            if (errors.Count > 0)
                return BadRequest(new
                {
                    message = "All company contact fields are required.",
                    errors = errors.Select(e => new { field = e.Field, error = e.Error })
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
                    new() { Header="Name",    Width="17%", HeaderClass="ps-4", CellClass="ps-4 fw-semibold" },
                    new() { Header="Type",    Width="12%" },
                    new() { Header="Phone",   Width="12%" },
                    new() { Header="Email",   Width="20%" },
                    new() { Header="City",    Width="8%" },
                    new() { Header="Tax ID",  Width="8%" },
                    new() { Header="Lexware", Width="13%"  },
                    new() { Header="Actions", Width="10%", HeaderClass="text-end pe-4", CellClass="text-end pe-4" },
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
                        LexwareBadge(c.LexwareType.ToString()),
                        ActionsButtons(c.Id.ToString(), c.LexwareType == LexwareType.NotExported)
                    }
                });
            }
        }
        private static Microsoft.AspNetCore.Html.IHtmlContent LexwareBadge(string status)
        {
            var cls = status switch
            {
                "Exported" => "badge bg-primary bg-opacity-10 text-primary",
                "Imported" => "badge bg-secondary bg-opacity-10 text-secondary",
                _ => "badge bg-warning bg-opacity-10 text-warning" // NotExported
            };

            var safe = System.Text.Encodings.Web.HtmlEncoder.Default.Encode(status ?? "");
            return new Microsoft.AspNetCore.Html.HtmlString($"<span class=\"{cls}\">{safe}</span>");
        }
        


        private static List<FieldValidationError> GetRequiredAddressErrors(AddressDto? address)
        {
            var errors = new List<FieldValidationError>();

            if (address is null)
            {
                errors.Add(new FieldValidationError("Address", "Address is required."));
                return errors;
            }

            AddIfMissing(address.FullNameOrCompany, "Address.FullNameOrCompany", "Full name or company is required.");
            AddIfMissing(address.Label, "Address.Label", "Address label is required.");
            AddIfMissing(address.StreetRaw, "Address.StreetRaw", "Street and house number are required.");
            AddIfMissing(address.AddressLine2, "Address.AddressLine2", "Address line 2 is required.");
            AddIfMissing(address.PostalCode, "Address.PostalCode", "Postal code is required.");
            AddIfMissing(address.City, "Address.City", "City is required.");
            AddIfMissing(address.Country, "Address.Country", "Country is required.");
            AddIfMissing(address.CountryCode, "Address.CountryCode", "Country code is required.");

            return errors;

            void AddIfMissing(string? value, string field, string message)
            {
                if (string.IsNullOrWhiteSpace(value))
                    errors.Add(new FieldValidationError(field, message));
            }
        }

        private static List<FieldValidationError> GetRequiredCreateCompanyContactErrors(ContactDto? contact)
        {
            var errors = new List<FieldValidationError>();

            if (contact is null)
            {
                errors.Add(new FieldValidationError("Contact", "Company contact is required."));
                return errors;
            }

            // The create modal has one full-name input for the company contact.
            AddIfMissing(contact.Name, "Contact.Name", "Contact name is required.");
            AddIfMissing(contact.Position, "Contact.Position", "Position is required.");
            AddIfMissing(contact.Email, "Contact.Email", "Email is required.");
            AddIfMissing(contact.Phone, "Contact.Phone", "Phone is required.");

            return errors;

            void AddIfMissing(string? value, string field, string message)
            {
                if (string.IsNullOrWhiteSpace(value))
                    errors.Add(new FieldValidationError(field, message));
            }
        }

        private static List<FieldValidationError> GetRequiredStructuredCompanyContactErrors(ContactDto? contact)
        {
            var errors = new List<FieldValidationError>();

            if (contact is null)
            {
                errors.Add(new FieldValidationError("Contact", "Company contact is required."));
                return errors;
            }

            // The add/edit contact UI uses separate first-name and last-name fields.
            // Salutation is optional.
            AddIfMissing(contact.FirstName, "Contact.FirstName", "First name is required.");
            AddIfMissing(contact.LastName, "Contact.LastName", "Last name is required.");
            AddIfMissing(contact.Position, "Contact.Position", "Position is required.");
            AddIfMissing(contact.Email, "Contact.Email", "Email is required.");
            AddIfMissing(contact.Phone, "Contact.Phone", "Phone is required.");

            return errors;

            void AddIfMissing(string? value, string field, string message)
            {
                if (string.IsNullOrWhiteSpace(value))
                    errors.Add(new FieldValidationError(field, message));
            }
        }

        private static string NormalizeEmail(string? email)
            => (email ?? string.Empty).Trim().ToLowerInvariant();

        private static bool TryGetDuplicateEmail(string? message, out string email)
            => TryGetEmailFromPrefixedMessage(
                message,
                DuplicateEmailMessagePrefix,
                out email);

        private static bool TryGetDuplicateEmailInRequest(string? message, out string email)
            => TryGetEmailFromPrefixedMessage(
                message,
                DuplicateEmailInRequestMessagePrefix,
                out email);

        private static bool TryGetEmailFromPrefixedMessage(
            string? message,
            string prefix,
            out string email)
        {
            email = string.Empty;

            if (string.IsNullOrWhiteSpace(message)
                || !message.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            email = NormalizeEmail(message[prefix.Length..]);
            return !string.IsNullOrWhiteSpace(email);
        }

        private void AddCreateEmailError(string duplicateEmail, string errorMessage)
        {
            var normalizedDuplicate = NormalizeEmail(duplicateEmail);
            var matched = false;

            for (var i = 0; i < (Customer.EmailAddresses?.Count ?? 0); i++)
            {
                var email = NormalizeEmail(Customer.EmailAddresses![i].Email);
                if (!string.Equals(
                        email,
                        normalizedDuplicate,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                ModelState.AddModelError(
                    $"Customer.EmailAddresses[{i}].Email",
                    errorMessage);
                matched = true;
            }

            if (!matched)
            {
                ModelState.AddModelError(
                    "Customer.EmailAddresses[0].Email",
                    errorMessage);
            }
        }

        private IActionResult BuildUpdateEmailValidationError(
            IEnumerable<EmailAddressDto>? emailItems,
            string duplicateEmail,
            string errorMessage)
        {
            emailItems ??= Array.Empty<EmailAddressDto>();

            var matchingIndexes = emailItems
                .Select((item, index) => new
                {
                    Index = index,
                    Email = NormalizeEmail(item.Email)
                })
                .Where(x => string.Equals(
                    x.Email,
                    NormalizeEmail(duplicateEmail),
                    StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Index)
                .ToList();

            if (matchingIndexes.Count == 0)
                matchingIndexes.Add(0);

            return BadRequest(new
            {
                message = errorMessage,
                errors = matchingIndexes.Select(index => new
                {
                    field = $"Customer.EmailAddresses[{index}].Email",
                    error = errorMessage
                })
            });
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
        public async Task<IActionResult> OnPostLexwareExportAsync([FromBody] CustomerIdRequest? req, CancellationToken ct)
        {
            if (req is null)
                return BadRequest(new { message = "Body is null. Send { customerId: '...' }" });

            if (req.CustomerId == Guid.Empty)
                return BadRequest(new { message = "CustomerId is empty." });

            var res = await _lexwareSync.ExportCustomerAsync(req.CustomerId, ct);
            return new JsonResult(res);
        }

        public async Task<IActionResult> OnPostLexwareDeleteAsync([FromBody] LexwareDeleteRequest req, CancellationToken ct)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.ContactId))
                return new JsonResult(new { message = "ContactId is missing." }) { StatusCode = 400 };

            var updated = await _lexwareSync.DeleteCustomerFromLexwareAsync(req.ContactId, ct);
            return new JsonResult(updated);
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

        private static Microsoft.AspNetCore.Html.IHtmlContent ActionsButtons(string customerId, bool canExport)
        {
            var exportBtn = canExport
                ? $$"""
<button type="button"
        class="btn vc-icon-btn text-warning"
        title="Export to Lexware"
        data-vc-action="table-export"
        data-client-id="{{Enc(customerId)}}">
    <i class="ri-upload-2-line"></i>
</button>
"""
                : "";

            return new Microsoft.AspNetCore.Html.HtmlString($$"""
<div class="vc-actions-wrap">
    {{exportBtn}}

    <a class="btn vc-icon-btn text-secondary"
   title="View"
   href="/Clients/Details/{{Enc(customerId)}}">
    <i class="ri-eye-line"></i>
</a>

    <button type="button"
            class="btn vc-icon-btn text-danger"
            title="Delete"
            data-vc-action="table-delete"
            data-client-id="{{Enc(customerId)}}">
        <i class="ri-delete-bin-line"></i>
    </button>
</div>
""");
   

    }

}


}
