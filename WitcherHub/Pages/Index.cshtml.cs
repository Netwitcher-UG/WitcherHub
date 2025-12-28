using FluentValidation;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.Encodings.Web;
using WitcherHub.Application.Models.DTO.Customers;
using WitcherHub.Pages.Models.UI;

namespace WitcherHub.Pages
{
    public class IndexModel : PageModel
    {
        private readonly IValidator<CustomerDTOs> _customerDTOsValidator;

        public IndexModel(IValidator<CustomerDTOs> customerDTOsValidator)
        {
            _customerDTOsValidator = customerDTOsValidator;
        }

        public TableCardVm ClientsTable { get; private set; } = new();

        // ✅ اربط الفورم على الـ DTOs اللي موجودة داخل form
        // (asp-for="Customer.Name" , asp-for="Address.City" ... إلخ)
        [BindProperty] public CustomerDto Customer { get; set; } = new();
        [BindProperty] public AddressDto Address { get; set; } = new();
        [BindProperty] public ContactDto Contact { get; set; } = new();

        public ModalVm CreateCustomerModal { get; private set; } = new();

        public void OnGet()
        {
            LoadTable();
            BuildCreateCustomerModal(autoOpen: false);
        }

        public async Task<IActionResult> OnPostAsync()
        {
            LoadTable();

            // ✅ هنا كان الخطأ: CustomerDTO غير موجود
            // الصح: CustomerDTOs
            var dto = new CustomerDTOs
            {
                Customer = Customer,
                Address = Address,
                Contact = Contact
            };

            var result = await _customerDTOsValidator.ValidateAsync(dto);

            if (!result.IsValid)
            {
                foreach (var err in result.Errors)
                    ModelState.AddModelError(err.PropertyName, err.ErrorMessage);

                BuildCreateCustomerModal(autoOpen: true);
                return Page();
            }

            // TODO: Save (MediatR/Service)
            return RedirectToPage();
        }

        private void BuildCreateCustomerModal(bool autoOpen)
        {
            CreateCustomerModal = new ModalVm
            {
                Id = "FormModal",
                Title = "Add Individual",
                SizeClass = "modal-lg",
                SubmitText = "Save",
                CancelText = "Cancel",
                Handler = null, // لأننا نستخدم OnPostAsync
                BodyPartialPath = "~/Pages/Shared/Modals/_CreateCustomerFields.cshtml",
                BodyModel = this,
                AutoOpen = autoOpen
            };
        }

        private void LoadTable()
        {
            var data = new[]
            {
                new { Id="demo-individual", Name="Anas Sadek", Type="Individual", Phone="+49 174 234 5678", Email="anas@email.com", City="Berlin", TaxId="—" },
                new { Id="demo-company", Name="ACME LLC", Type="Company", Phone="+1 212 555 0199", Email="finance@acme.com", City="New York", TaxId="CR-123456" },
                new { Id="demo-3", Name="Mona Ali", Type="Individual", Phone="+966 50 123 4567", Email="mona.ali@example.com", City="Riyadh", TaxId="—" },
            };


            ClientsTable = new TableCardVm
            {
                Title = "Clients",
                PrimaryButtonText = "Add Client",
                PrimaryButtonTarget = "#FormModal",
                SearchPlaceholder = "Search clients...",
                Pagination = new PaginationVm
                {
                    Page = 2,
                    PageSize = 10,
                    TotalItems = 57,
                    SearchQuery = null
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

            foreach (var c in data)
            {
                ClientsTable.Rows.Add(new TableRowVm
                {
                    Cells =
                    {
                        Html(c.Name),
                        TypeBadge(c.Type),
                        Html(c.Phone),
                        Html(c.Email),
                        Html(c.City),
                        Html(c.TaxId),
                        ActionsButtons(c.Id)
                    }
                });
            }


        }

        private static IHtmlContent Html(string? text)
            => new HtmlString(HtmlEncoder.Default.Encode(text ?? ""));

        private static IHtmlContent TypeBadge(string type)
        {
            var isCompany = type == "Company";
            var cls = isCompany ? "badge bg-success bg-opacity-10 text-success" : "badge bg-info bg-opacity-10 text-info";
            return new HtmlString($"<span class='{cls}'>{HtmlEncoder.Default.Encode(type)}</span>");
        }

        private static string Enc(string? v) => HtmlEncoder.Default.Encode(v ?? "");

        private static IHtmlContent ActionsButtons(string customerId) => new HtmlString($$"""
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
