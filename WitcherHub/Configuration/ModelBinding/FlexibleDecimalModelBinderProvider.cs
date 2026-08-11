using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace WitcherHub.Configuration.ModelBinding
{
    /// <summary>
    /// Applies <see cref="FlexibleDecimalModelBinder"/> to every decimal and double
    /// property, nullable or not.
    /// </summary>
    public sealed class FlexibleDecimalModelBinderProvider : IModelBinderProvider
    {
        public IModelBinder? GetBinder(ModelBinderProviderContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            var type = Nullable.GetUnderlyingType(context.Metadata.ModelType) ?? context.Metadata.ModelType;

            if (type == typeof(decimal) || type == typeof(double))
                return new FlexibleDecimalModelBinder();

            return null;
        }
    }
}
