using FluentValidation;
using Mapster;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using WitcherHub.Application.Common.Behaviours;


namespace WitcherHub.Application
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddApplication(this IServiceCollection services)
        {
            // الـ Assembly حق طبقة Application
            var assembly = typeof(DependencyInjection).Assembly;

            // MediatR: تسجيل كل الـ Handlers في هذا الـ Assembly
            services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(assembly));

            // FluentValidation: تسجيل كل الـ Validators في هذا الـ Assembly
            services.AddValidatorsFromAssembly(assembly);

            // Pipeline Behavior للـ validation (يشتغل قبل أي Handler)
            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

            // Mapster: إعداد الـ config العام + عمل Scan للـ Assembly
            var config = TypeAdapterConfig.GlobalSettings;
            config.Scan(assembly); // لما تضيف IRegister classes لاحقاً يلتقطها

            services.AddSingleton(config); // عشان تقدر تحقنه لو حبيت

            return services;
        }
    }
}
