using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using WitcherHub.Application.Interfaces;

namespace WitcherHub.Pages.Auth
{
    [AllowAnonymous]
    public class ResetPasswordModel : PageModel
    {
        private readonly IAuthService _auth;

        public ResetPasswordModel(IAuthService auth)
        {
            _auth = auth;
        }

        [BindProperty(SupportsGet = true)]
        public string? Email { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Token { get; set; }

        [BindProperty]
        [Required(ErrorMessage = "Please enter a new password.")]
        [DataType(DataType.Password)]
        public string NewPassword { get; set; } = "";

        [BindProperty]
        [Required(ErrorMessage = "Please confirm the new password.")]
        [DataType(DataType.Password)]
        [Compare(nameof(NewPassword), ErrorMessage = "The two passwords do not match.")]
        public string ConfirmPassword { get; set; } = "";

        public bool Completed { get; private set; }

        public IReadOnlyList<string> Errors { get; private set; } = [];

        public IActionResult OnGet()
        {
            if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Token))
            {
                Errors = ["This reset link is incomplete. Please request a new one."];
            }

            return Page();
        }

        public async Task<IActionResult> OnPostAsync(CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Token))
            {
                Errors = ["This reset link is incomplete. Please request a new one."];
                return Page();
            }

            if (!ModelState.IsValid)
                return Page();

            var result = await _auth.ResetPasswordAsync(Email, Token, NewPassword, ct);

            if (!result.Succeeded)
            {
                Errors = result.Errors;
                return Page();
            }

            Completed = true;
            return Page();
        }

        /// <summary>
        /// True when the link itself is unusable, so the form is pointless to show.
        /// </summary>
        public bool LinkIsBroken =>
            string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Token);
    }
}
