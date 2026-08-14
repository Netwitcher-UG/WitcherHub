using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.Json;
using WitcherHub.Application.Interfaces.ManageData;
using WitcherHub.Application.Models.View.Overview;
using WitcherHub.Domain.SeedData;
using WitcherHub.Pages.Models.UI;

namespace WitcherHub.Pages
{
    /// <summary>
    /// The state of the business on one screen.
    ///
    /// Nothing in the application answered "what is owed to us", "what is waiting
    /// on a customer" or "what is about to expire" — every list was scoped to a
    /// single project, so those questions could only be answered by opening
    /// projects one at a time and adding up by hand.
    /// </summary>
    [Authorize(Policy = AppPolicyPrefixes.Permission + AppPermissions.ManageNetwitcher)]
    public class DashboardModel : PageModel
    {
        private readonly IDocumentRegister _register;

        public DashboardModel(IDocumentRegister register) => _register = register;

        public BusinessOverview Overview { get; private set; } = default!;

        /// <summary>Series for the revenue chart, pre-serialised for the client.</summary>
        public string ChartLabelsJson { get; private set; } = "[]";
        public string ChartInvoicedJson { get; private set; } = "[]";
        public string ChartCollectedJson { get; private set; } = "[]";

        public async Task OnGetAsync(CancellationToken ct)
        {
            Overview = await _register.GetOverviewAsync(ct);

            ChartLabelsJson = JsonSerializer.Serialize(Overview.RevenueByMonth.Select(p => p.Label));
            ChartInvoicedJson = JsonSerializer.Serialize(Overview.RevenueByMonth.Select(p => decimal.Round(p.Invoiced, 2)));
            ChartCollectedJson = JsonSerializer.Serialize(Overview.RevenueByMonth.Select(p => decimal.Round(p.Collected, 2)));
        }

        public IReadOnlyList<StatCardVm> Cards()
        {
            var money = Overview.Money;
            var pipeline = Overview.Pipeline;

            return
            [
                new StatCardVm
                {
                    Label = "Outstanding",
                    Value = Format.MoneyCompact(money.Outstanding, money.Currency),
                    Detail = money.OutstandingInvoiceCount switch
                    {
                        0 => "Nothing unpaid",
                        1 => "1 unpaid invoice",
                        var n => $"{n} unpaid invoices"
                    },
                    Icon = "ri-time-line",
                    Tone = money.Outstanding > 0m ? "warning" : "success",
                    LinkUrl = Url.Page("/Invoices/Index", new { outstandingOnly = true }),
                    LinkText = "See unpaid invoices"
                },

                new StatCardVm
                {
                    Label = "Overdue",
                    Value = Format.MoneyCompact(money.Overdue, money.Currency),
                    Detail = money.OverdueInvoiceCount switch
                    {
                        0 => "Nothing past its due date",
                        1 => "1 invoice past its due date",
                        var n => $"{n} invoices past their due date"
                    },
                    Icon = "ri-alarm-warning-line",
                    Tone = money.Overdue > 0m ? "danger" : "success",
                    LinkUrl = Url.Page("/Invoices/Index", new { overdueOnly = true }),
                    LinkText = "Chase these"
                },

                new StatCardVm
                {
                    Label = "Collected this month",
                    Value = Format.MoneyCompact(money.CollectedThisMonth, money.Currency),
                    Detail = money.CollectedChangePercent switch
                    {
                        null => "No payments last month to compare with",
                        >= 0 => $"{money.CollectedChangePercent.Value:0.#}% up on last month",
                        _ => $"{Math.Abs(money.CollectedChangePercent.Value):0.#}% down on last month"
                    },
                    Icon = "ri-money-euro-circle-line",
                    Tone = "success",
                    LinkUrl = Url.Page("/Invoices/Index"),
                    LinkText = "All invoices"
                },

                new StatCardVm
                {
                    Label = "Quotes awaiting a decision",
                    Value = Format.MoneyCompact(pipeline.QuotesAwaitingDecisionValue, money.Currency),
                    Detail = pipeline.QuotesAwaitingDecisionCount switch
                    {
                        0 => "Nothing with a customer",
                        1 => "1 quote with a customer",
                        var n => $"{n} quotes with customers"
                    },
                    Icon = "ri-send-plane-line",
                    Tone = "info",
                    LinkUrl = Url.Page("/Quotes/Index", new { status = 1 }),
                    LinkText = "See these quotes"
                }
            ];
        }
    }
}
