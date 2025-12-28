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
        private readonly IValidator<CreateCustomerDto> _createCustomerValidator;

        public IndexModel(IValidator<CreateCustomerDto> createCustomerValidator)
        {
            _createCustomerValidator = createCustomerValidator;
        }

        public TableCardVm ClientsTable { get; private set; } = new();

        // BindProperties اللي تتطابق مع asp-for في _CreateCustomerFields
        [BindProperty] public CustomerDto Customer { get; set; } = new();
        [BindProperty] public AddressDto Address { get; set; } = new();
        [BindProperty] public ContactDto Contact { get; set; } = new();

        // هذا هو مودال الـ Template
        public ModalVm CreateCustomerModal { get; private set; } = new();

        public void OnGet()
        {
            LoadTable();
            BuildCreateCustomerModal(autoOpen: false);
        }

        public async Task<IActionResult> OnPostAsync()
        {
            LoadTable();

            var dto = new CreateCustomerDto
            {
                Customer = Customer,
                Address = Address,
                Contact = Contact
            };

            var result = await _createCustomerValidator.ValidateAsync(dto);

            if (!result.IsValid)
            {
                foreach (var err in result.Errors)
                    ModelState.AddModelError(err.PropertyName, err.ErrorMessage);

                // رجّع الصفحة وافتح المودال تلقائياً + خليه يحتفظ بالقيم
                BuildCreateCustomerModal(autoOpen: true);
                return Page();
            }

            // TODO: هنا لاحقاً ترسل Command عبر MediatR أو تحفظ في DB
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
            new { Name="Anas Sadek", Type="Individual", Phone="+49 174 234 5678", Email="anas@email.com", City="Berlin", TaxId="—" },
            new { Name="ACME LLC", Type="Company", Phone="+1 212 555 0199", Email="finance@acme.com", City="New York", TaxId="CR-123456" },
            new { Name="Mona Ali", Type="Individual", Phone="+966 50 123 4567", Email="mona.ali@example.com", City="Riyadh", TaxId="—" },
        };

            ClientsTable = new TableCardVm
            {
                Title = "Clients",
                PrimaryButtonText = "Add Client",
                PrimaryButtonTarget = "#FormModal",
                SearchPlaceholder = "Search clients...",
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
                    ActionsButtons()
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

        private static IHtmlContent ActionsButtons() => new HtmlString("""
        <a class="btn btn-sm btn-outline-info rounded-circle">
            <i class="material-icons-outlined">edit</i>
        </a>
        <a class="btn btn-sm btn-outline-danger rounded-circle">
            <i class="material-icons-outlined">delete</i>
        </a>
    """);
    }
}
