using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace WitcherHub.Pages.Contracts
{
    public class SignDemoModel : PageModel
    {
        public string ContractHtml { get; private set; } = "";

        public void OnGet()
        {
            // لاحقاً: هذا النص يأتي من الباك (HTML جاهز) ولا نترجمه.
            ContractHtml = @"
<h2 style='margin:0 0 6px 0;'>Software Subscription & Services Agreement</h2>
<div style='color:#666;font-size:13px;margin-bottom:14px;'>
Contract No.: <b>FH-2026-0111-0028</b> &nbsp;|&nbsp; Effective Date: <b>2026-01-11</b>
</div>

<p><b>Parties.</b> This Agreement is entered into between <b>Fekrahub Solutions GmbH</b>, Friedrichstraße 123, 10117 Berlin, Germany (“Provider”),
and <b>Al-Noor International School</b>, King Fahd Road, Riyadh, Saudi Arabia (“Customer”).</p>

<p><b>1. Scope of Services.</b> Provider grants Customer a subscription license to use the <b>Fekrahub</b> platform for school operations,
including: (i) student and staff profiles, (ii) attendance tracking, (iii) activities, schedules and events,
(iv) student enrollment contracts, (v) notifications and messaging, and (vi) reports and analytics.</p>

<p><b>2. Subscription Plan.</b> Customer subscribes to the “School Pro” plan for up to <b>250 students</b> and <b>40 staff accounts</b>.
The subscription term is <b>12 months</b> starting on the Effective Date. Additional users may be added at Provider’s then-current rates.</p>

<p><b>3. Fees & Payment.</b> The annual subscription fee is <b>€1,980</b> (excluding taxes). Payment is due within <b>14 days</b> of invoice date.
Late payment may result in temporary suspension after written notice.</p>

<p><b>4. Support & Implementation.</b> Provider will provision the customer tenant, assist with initial setup,
and provide standard support via email on business days. Target response time for standard requests is <b>1 business day</b>.</p>

<p><b>5. Data Protection.</b> Customer remains responsible for lawful collection and use of student/staff data.
Provider will process data only to provide the service and apply appropriate technical and organizational measures.</p>

<p><b>6. Confidentiality.</b> Each party shall protect the other’s confidential information and use it only for fulfilling obligations under this Agreement.</p>

<p><b>7. Termination.</b> Either party may terminate for material breach not cured within <b>14 days</b> after written notice.
Upon termination, access will be revoked and Customer may request an export of data in a reasonable format.</p>

<p><b>8. Limitation of Liability.</b> Provider’s total liability under this Agreement shall not exceed the fees paid by Customer in the <b>12 months</b>
preceding the event giving rise to the claim, to the maximum extent permitted by law.</p>

<p><b>9. Governing Law.</b> This Agreement is governed by the laws of <b>Germany</b>. Courts of <b>Berlin</b> shall have jurisdiction, where permitted.</p>

<p><b>Acceptance.</b> By signing, Customer confirms it has reviewed and accepts the terms of this Agreement.</p>
";
        }
    }
}
