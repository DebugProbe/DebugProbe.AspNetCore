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
