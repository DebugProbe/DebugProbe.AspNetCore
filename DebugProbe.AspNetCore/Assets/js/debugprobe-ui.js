function copyText(btn) {
    const pre = btn.closest(".code-block").querySelector("pre");

    const text = pre.dataset.copy ?? pre.innerText;

    navigator.clipboard.writeText(text);

    btn.innerText = "Copied";

    setTimeout(() => btn.innerText = "Copy", 1500);
}

function buildCurlCommand(method, url, headers, body, isWindows) {
    function escapeSingleQuote(str) {
        if (!str) return "";
        return str.replace(/'/g, "'\\''");
    }

    function escapeDoubleQuote(str) {
        if (!str) return "";
        return str.replace(/"/g, '\\"').replace(/%/g, '%%');
    }

    const quote = isWindows ? '"' : "'";
    const escape = isWindows ? escapeDoubleQuote : escapeSingleQuote;

    let curlCmd = `curl -X ${method.toUpperCase()} ${quote}${escape(url)}${quote}`;

    // Process headers
    for (const [key, value] of Object.entries(headers)) {
        if (!key || !value) continue;
        const trimmedVal = value.trim();
        if (trimmedVal === "[REDACTED]" || trimmedVal === "") continue;

        curlCmd += ` -H ${quote}${escape(key)}: ${escape(value)}${quote}`;
    }

    // Process body (skip if empty or truncated indicator)
    if (body && body.trim() !== "" && body !== "[Body too large]") {
        curlCmd += ` -d ${quote}${escape(body)}${quote}`;
    }

    return curlCmd;
}

function showCopiedTooltip(btn) {
    const tooltip = document.createElement("div");
    tooltip.className = "copied-tooltip";
    tooltip.textContent = "Copied!";
    document.body.appendChild(tooltip);

    const rect = btn.getBoundingClientRect();
    tooltip.style.left = (rect.left + window.scrollX + rect.width / 2) + "px";
    tooltip.style.top = (rect.top + window.scrollY) + "px";

    setTimeout(() => {
        tooltip.remove();
    }, 1500);
}

function escapeCSharpString(str) {
    if (!str) return "";
    return str
        .replace(/\\/g, "\\\\")
        .replace(/"/g, '\\"')
        .replace(/\r/g, '\\r')
        .replace(/\n/g, '\\n');
}

function buildCSharpSnippet(method, url, headers, body) {
    const escapedUrl = escapeCSharpString(url);
    const methodFormatted = method.charAt(0).toUpperCase() + method.slice(1).toLowerCase();
    let snippet = `var request = new HttpRequestMessage(HttpMethod.${methodFormatted}, "${escapedUrl}");\n`;

    for (const [key, value] of Object.entries(headers)) {
        if (!key || !value) continue;
        const trimmedVal = value.trim();
        if (trimmedVal === "[REDACTED]" || trimmedVal === "") continue;

        snippet += `request.Headers.Add("${escapeCSharpString(key)}", "${escapeCSharpString(value)}");\n`;
    }

    if (body && body.trim() !== "" && body !== "[Body too large]") {
        let contentType = "application/json";
        for (const [key, value] of Object.entries(headers)) {
            if (key.toLowerCase() === "content-type" && value && value.trim() !== "" && value.trim() !== "[REDACTED]") {
                contentType = value.split(";")[0].trim();
                break;
            }
        }
        snippet += `request.Content = new StringContent("${escapeCSharpString(body)}", Encoding.UTF8, "${escapeCSharpString(contentType)}");\n`;
    }

    snippet += `var response = await httpClient.SendAsync(request);`;
    return snippet;
}

function copyAsCSharp(btn) {
    const card = btn.closest(".trace-card");
    if (!card) return;

    const method = card.dataset.method;
    const url = card.dataset.url;
    if (!method || !url) return;

    let headers = {};
    try {
        headers = JSON.parse(card.dataset.headers || '{}');
    } catch (e) {
        // Fallback or ignore
    }

    const body = card.dataset.body;

    const snippet = buildCSharpSnippet(method, url, headers, body);

    navigator.clipboard.writeText(snippet);

    showCopiedTooltip(btn);
}

function copyAsCurl(btn) {
    const card = btn.closest(".trace-card");
    if (!card) return;

    const method = card.dataset.method;
    const url = card.dataset.url;
    if (!method || !url) return;

    let headers = {};
    try {
        headers = JSON.parse(card.dataset.headers || '{}');
    } catch (e) {
        // Fallback or ignore
    }

    const body = card.dataset.body;

    const isWindows = (navigator.platform && navigator.platform.indexOf('Win') !== -1) || 
                      (navigator.userAgent && navigator.userAgent.indexOf('Win') !== -1);

    const curlCmd = buildCurlCommand(method, url, headers, body, isWindows);

    navigator.clipboard.writeText(curlCmd);

    showCopiedTooltip(btn);
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

// Phase 2: Waterfall Timeline Interactivity
document.addEventListener("DOMContentLoaded", () => {
    let tooltip = document.getElementById("wfTooltip");
    if (!tooltip) {
        tooltip = document.createElement("div");
        tooltip.id = "wfTooltip";
        tooltip.className = "wf-tooltip";
        document.body.appendChild(tooltip);
    }

    const bars = document.querySelectorAll(".wf-bar");
    bars.forEach(bar => {
        bar.addEventListener("mouseenter", () => {
            const start = bar.getAttribute("data-wf-start");
            const duration = bar.getAttribute("data-wf-duration");
            const url = bar.getAttribute("data-wf-url");
            const status = bar.getAttribute("data-wf-status") || "N/A";
            const isError = bar.classList.contains("wf-bar--error");
            
            tooltip.innerHTML = "";
            
            const urlEl = document.createElement("div");
            urlEl.className = "wf-tooltip-url";
            urlEl.textContent = url;
            
            const startEl = document.createElement("div");
            startEl.innerHTML = "<strong>Start:</strong> +" + escapeHtml(start) + " ms";
            
            const durationEl = document.createElement("div");
            durationEl.innerHTML = "<strong>Duration:</strong> " + escapeHtml(duration) + " ms";
            
            const statusEl = document.createElement("div");
            const statusSpan = document.createElement("span");
            statusSpan.style.color = isError ? "#f87171" : "#4ade80";
            statusSpan.style.fontWeight = "bold";
            statusSpan.textContent = status;
            statusEl.innerHTML = "<strong>Status:</strong> ";
            statusEl.appendChild(statusSpan);
            
            tooltip.appendChild(urlEl);
            tooltip.appendChild(startEl);
            tooltip.appendChild(durationEl);
            tooltip.appendChild(statusEl);

            tooltip.style.display = "block";
        });

        bar.addEventListener("mousemove", (e) => {
            const tooltipWidth = tooltip.offsetWidth;
            const tooltipHeight = tooltip.offsetHeight;
            let left = e.pageX + 10;
            let top = e.pageY + 10;

            if (left + tooltipWidth > window.innerWidth + window.pageXOffset) {
                left = e.pageX - tooltipWidth - 10;
            }
            if (top + tooltipHeight > window.innerHeight + window.pageYOffset) {
                top = e.pageY - tooltipHeight - 10;
            }

            tooltip.style.left = left + "px";
            tooltip.style.top = top + "px";
        });

        bar.addEventListener("mouseleave", () => {
            tooltip.style.display = "none";
        });
    });

    function escapeHtml(str) {
        if (!str) return "";
        return str
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&#039;");
    }
});
