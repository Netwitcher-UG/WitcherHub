namespace WitcherHub.Pages.Models.UI
{
    public class ModalVm
    {
        public string Id { get; set; } = "FormModal";
        public string Title { get; set; } = "Add";

        // مثال: "modal-lg" | "modal-xl"
        public string SizeClass { get; set; } = "modal-lg";

        public string SubmitText { get; set; } = "Save";
        public string CancelText { get; set; } = "Cancel";

        // إذا تبي تستخدم Handler مثل OnPostCreate() اكتب "Create"
        // إذا تبي OnPostAsync خليه null
        public string? Handler { get; set; }

        // مسار Partial الذي يحتوي حقول الفورم
        public string BodyPartialPath { get; set; } = "";

        // الموديل اللي يروح لحقول الفورم (غالباً this = PageModel)
        public object? BodyModel { get; set; }

        // إذا فيه أخطاء Validation نفتح المودال تلقائياً
        public bool AutoOpen { get; set; } = false;
    }
}
