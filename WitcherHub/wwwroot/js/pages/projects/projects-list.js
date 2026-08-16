// Projects list actions: archive, restore, and permanent deletion.
//
// Archiving is the ordinary path and asks once. Permanent deletion asks the
// server what the project actually holds before offering anything, so the
// confirmation describes the real consequence instead of a generic warning —
// and when the server says the project holds records that must be kept, the
// dialog says so and offers no delete button at all.
(function () {
    "use strict";

    const impactUrl = document.getElementById("deletionImpactUrl")?.value;
    if (!impactUrl) return;

    let pendingProjectId = null;
    let pendingProjectTitle = "";

    const modalEl = document.getElementById("deleteProjectModal");
    const blockedEl = document.getElementById("deleteProjectBlocked");
    const allowedEl = document.getElementById("deleteProjectAllowed");
    const loadingEl = document.getElementById("deleteProjectLoading");
    const impactList = document.getElementById("deleteProjectImpact");
    const nameEcho = document.getElementById("deleteProjectNameEcho");
    const nameInput = document.getElementById("deleteProjectConfirmName");
    const submitBtn = document.getElementById("deleteProjectSubmit");

    function submitForm(formId, inputId, projectId) {
        const form = document.getElementById(formId);
        const input = document.getElementById(inputId);
        if (!form || !input) return;

        input.value = projectId;
        form.submit();
    }

    document.addEventListener("click", async function (event) {
        const trigger = event.target.closest("[data-action]");
        if (!trigger) return;

        const action = trigger.dataset.action;
        const projectId = trigger.dataset.projectId;

        if (action === "archive-project") {
            // One confirmation, and it says what archiving does — the usual
            // reason people hesitate here is not knowing whether it deletes.
            const title = trigger.dataset.projectTitle || "this project";

            const ok = window.UI?.modal?.confirm
                ? await window.UI.modal.confirm({
                    title: "Archive project",
                    message:
                        `Archive "${title}"? It leaves the active list and everything in it — quotes, ` +
                        `contracts, invoices — is kept. You can restore it at any time.`,
                    okText: "Archive"
                })
                : window.confirm(`Archive "${title}"? Nothing is deleted.`);

            if (ok) submitForm("archiveProjectForm", "archiveProjectId", projectId);
            return;
        }

        if (action === "restore-project") {
            submitForm("restoreProjectForm", "restoreProjectId", projectId);
            return;
        }

        if (action === "delete-project") {
            pendingProjectId = projectId;
            pendingProjectTitle = trigger.dataset.projectTitle || "";

            openDeleteDialog();
            await loadImpact(projectId);
        }
    });

    function openDeleteDialog() {
        // Reset to the loading state every time: a dialog that still shows the
        // last project's impact while fetching this one's is worse than a blank.
        blockedEl.classList.add("d-none");
        allowedEl.classList.add("d-none");
        loadingEl.classList.remove("d-none");

        submitBtn.disabled = true;
        nameInput.value = "";

        if (window.bootstrap?.Modal) {
            window.bootstrap.Modal.getOrCreateInstance(modalEl).show();
        }
    }

    async function loadImpact(projectId) {
        try {
            const response = await fetch(`${impactUrl}&projectId=${encodeURIComponent(projectId)}`, {
                headers: { "Accept": "application/json" }
            });

            if (!response.ok) throw new Error(`HTTP ${response.status}`);

            const impact = await response.json();
            loadingEl.classList.add("d-none");

            if (impact.blocked) {
                // No delete button offered at all. Being told why, with the
                // alternative named, beats a button that fails when pressed.
                blockedEl.textContent = impact.reason;
                blockedEl.classList.remove("d-none");
                submitBtn.disabled = true;
                return;
            }

            impactList.innerHTML = "";

            if (impact.willDelete && impact.willDelete.length) {
                impact.willDelete.forEach(function (item) {
                    const li = document.createElement("li");
                    li.textContent = item;
                    impactList.appendChild(li);
                });
            } else {
                const li = document.createElement("li");
                li.textContent = "Nothing — this project is empty.";
                impactList.appendChild(li);
            }

            nameEcho.textContent = pendingProjectTitle;
            allowedEl.classList.remove("d-none");
        } catch (error) {
            loadingEl.classList.add("d-none");
            blockedEl.textContent =
                "What this project holds could not be checked, so deletion is not offered. Try again in a moment.";
            blockedEl.classList.remove("d-none");

            if (window.console) console.error(error);
        }
    }

    // The typed name has to match before the button works. This is the one
    // action here that cannot be undone, and it sits in the same menu as
    // Archive, which can.
    nameInput?.addEventListener("input", function () {
        submitBtn.disabled = nameInput.value.trim() !== pendingProjectTitle.trim();
    });

    submitBtn?.addEventListener("click", function () {
        if (submitBtn.disabled || !pendingProjectId) return;

        submitBtn.disabled = true;
        submitForm("deleteProjectForm", "deleteProjectId", pendingProjectId);
    });
})();
