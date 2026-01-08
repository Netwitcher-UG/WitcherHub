using System.Net.Mail;
using WitcherHub.Application.Models.Email;

namespace WitcherHub.Application.Interfaces.Email
{
    public interface IEmailSender
    {
        Task SendAsync(EmailMessage message, CancellationToken ct = default);
    }
}
