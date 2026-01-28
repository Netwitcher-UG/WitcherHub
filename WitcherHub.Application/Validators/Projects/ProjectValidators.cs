using FluentValidation;
using WitcherHub.Application.Models.DTO.Project;

namespace WitcherHub.Application.Validators.Projects
{
    // =========================
    // Create
    // =========================
    public sealed class CreateProjectDtoValidator : AbstractValidator<CreateProjectDto>
    {
        public CreateProjectDtoValidator()
        {
            RuleFor(x => x.CustomerId)
      .NotEmpty().WithMessage("Customer is required.");



            RuleFor(x => x.Title)
                .NotEmpty().WithMessage("Title is required.")
                .MaximumLength(250).WithMessage("Title must be 250 characters or less.");

            RuleFor(x => x.Description)
                .MaximumLength(2000).WithMessage("Description is too long.")
                .When(x => x.Description != null);

            RuleFor(x => x)
                .Must(ValidDates)
                .WithMessage("EndDate cannot be before StartDate.");
        }

        private static bool ValidDates(CreateProjectDto x)
        {
            if (!x.StartDate.HasValue || !x.EndDate.HasValue) return true;
            return x.EndDate.Value >= x.StartDate.Value;
        }
    }

    // =========================
    // Update
    // =========================
    public sealed class UpdateProjectDtoValidator : AbstractValidator<UpdateProjectDto>
    {
        public UpdateProjectDtoValidator()
        {
            RuleFor(x => x.Title)
                .MaximumLength(250).WithMessage("Title must be 250 characters or less.")
                .When(x => x.Title != null);

            RuleFor(x => x.Description)
                .MaximumLength(2000).WithMessage("Description is too long.")
                .When(x => x.Description != null);

            RuleFor(x => x)
                .Must(ValidDates)
                .WithMessage("EndDate cannot be before StartDate.");
        }

        private static bool ValidDates(UpdateProjectDto x)
        {
            if (!x.StartDate.HasValue || !x.EndDate.HasValue) return true;
            return x.EndDate.Value >= x.StartDate.Value;
        }
    }
}
