function copyText(btn) {
    const pre = btn.closest(".code-block").querySelector("pre");

    const text = pre.dataset.copy ?? pre.innerText;

    navigator.clipboard.writeText(text);

    btn.innerText = "Copied";

    setTimeout(() => btn.innerText = "Copy", 1500);
}


const clearBtn = document.getElementById("clearBtn");
if (clearBtn) {
    clearBtn.addEventListener("click", async () => {
        if (!confirm("Clear all requests?")) return;

        await fetch("/debug/clear", { method: "POST" });
        location.reload();
    });
}

document.querySelectorAll(".clickable-row[data-url]").forEach(row => {
    row.addEventListener("click", () => {
        window.location.assign(row.dataset.url);
    });
});

const requestSearch = document.getElementById("requestSearch");
const methodFilter = document.getElementById("methodFilter");
const statusFilter = document.getElementById("statusFilter");
const resetFiltersBtn = document.getElementById("resetFiltersBtn");
const visibleCount = document.getElementById("visibleCount");
const emptyFilterState = document.getElementById("emptyFilterState");
const requestRows = Array.from(document.querySelectorAll("#requestTable tbody tr.clickable-row"));

function applyRequestFilters() {
    if (!requestRows.length) return;

    const search = (requestSearch?.value ?? "").trim().toLowerCase();
    const method = methodFilter?.value ?? "";
    const statusFamily = statusFilter?.value ?? "";
    let shown = 0;

    requestRows.forEach(row => {
        const matchesSearch = !search || (row.dataset.search ?? "").toLowerCase().includes(search);
        const matchesMethod = !method || row.dataset.method === method;
        const matchesStatus = !statusFamily || row.dataset.statusFamily === statusFamily;
        const isVisible = matchesSearch && matchesMethod && matchesStatus;

        row.hidden = !isVisible;
        if (isVisible) shown++;
    });

    if (visibleCount) visibleCount.innerText = shown.toString();
    if (emptyFilterState) emptyFilterState.hidden = shown > 0;
}

[requestSearch, methodFilter, statusFilter].forEach(control => {
    control?.addEventListener("input", applyRequestFilters);
    control?.addEventListener("change", applyRequestFilters);
});

resetFiltersBtn?.addEventListener("click", () => {
    if (requestSearch) requestSearch.value = "";
    if (methodFilter) methodFilter.value = "";
    if (statusFilter) statusFilter.value = "";
    applyRequestFilters();
    requestSearch?.focus();
});
