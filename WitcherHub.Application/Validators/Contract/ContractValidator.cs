using FluentValidation;
using WitcherHub.Application.Models.DTO.Contracts;

namespace WitcherHub.Application.Validators.Contract
{
    public sealed class ContractValidator : AbstractValidator<ContractDTOs>
    {
        public ContractValidator()
        {
            RuleFor(x => x.Contract).NotNull().SetValidator(new ContractDtoValidator());
            RuleFor(x => x.Items).NotNull();
            RuleForEach(x => x.Items).SetValidator(new ContractItemDtoValidator());
        }
    }

    public sealed class ContractDtoValidator : AbstractValidator<ContractDto>
    {
        public ContractDtoValidator()
        {
            RuleFor(x => x.ProjectId).NotEmpty();
            RuleFor(x => x.Currency).NotEmpty().Length(3, 10);

            RuleFor(x => x.EndDate)
                .GreaterThan(x => x.StartDate)
                .When(x => x.StartDate.HasValue && x.EndDate.HasValue);

            RuleFor(x => x.Status).IsInEnum();
        }
    }

    public sealed class ContractItemDtoValidator : AbstractValidator<ContractItemDto>
    {
        public ContractItemDtoValidator()
        {
            RuleFor(x => x.Title).NotEmpty().MaximumLength(250);
            RuleFor(x => x.AgreedPrice).GreaterThanOrEqualTo(0).When(x => x.AgreedPrice.HasValue);
            RuleFor(x => x.Position).GreaterThan(0);
        }
    }

    public sealed class UpdateContractDtoValidator : AbstractValidator<UpdateContractDto>
    {
        public UpdateContractDtoValidator()
        {
            RuleFor(x => x.Contract).NotNull().SetValidator(new ContractDtoValidator());
            RuleForEach(x => x.Items).SetValidator(new ContractItemDtoValidator());
        }
    }

    public sealed class CreateContractItemDtoValidator : AbstractValidator<CreateContractItemDto>
    {
        public CreateContractItemDtoValidator()
        {
            RuleFor(x => x.ContractId).NotEmpty();
            RuleFor(x => x.Item).NotNull().SetValidator(new ContractItemDtoValidator());
        }
    }

    public sealed class UpdateContractItemDtoValidator : AbstractValidator<UpdateContractItemDto>
    {
        public UpdateContractItemDtoValidator()
        {
            RuleFor(x => x.ContractId).NotEmpty();
            RuleFor(x => x.ItemId).NotEmpty();
            RuleFor(x => x.Item).NotNull().SetValidator(new ContractItemDtoValidator());
        }
    }

    public sealed class DeleteContractItemDtoValidator : AbstractValidator<DeleteContractItemDto>
    {
        public DeleteContractItemDtoValidator()
        {
            RuleFor(x => x.ContractId).NotEmpty();
            RuleFor(x => x.ItemId).NotEmpty();
        }
    }
}
