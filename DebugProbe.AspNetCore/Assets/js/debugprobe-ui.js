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

function getTraceCardData(btn) {
    const card = btn.closest(".trace-card");
    if (!card) return null;

    const method = card.dataset.method;
    const url = card.dataset.url;
    if (!method || !url) return null;

    let headers = {};
    try {
        headers = JSON.parse(card.dataset.headers || '{}');
    } catch (e) {
        // Fallback or ignore
    }

    const body = card.dataset.body;

    return { card, method, url, headers, body };
}

function copyAsCSharp(btn) {
    const data = getTraceCardData(btn);
    if (!data) return;

    const snippet = buildCSharpSnippet(data.method, data.url, data.headers, data.body);

    navigator.clipboard.writeText(snippet);

    showCopiedTooltip(btn);
}

function copyAsCurl(btn) {
    const data = getTraceCardData(btn);
    if (!data) return;

    const isWindows = (navigator.platform && navigator.platform.indexOf('Win') !== -1) || 
                      (navigator.userAgent && navigator.userAgent.indexOf('Win') !== -1);

    const curlCmd = buildCurlCommand(data.method, data.url, data.headers, data.body, isWindows);

    navigator.clipboard.writeText(curlCmd);

    showCopiedTooltip(btn);
}

function getPayloadBody(card, title) {
    const panels = card.querySelectorAll(".payload-panel");
    for (const panel of panels) {
        const span = panel.querySelector("summary span");
        if (span && span.textContent.trim().toLowerCase() === title.toLowerCase()) {
            const pre = panel.querySelector("pre");
            return pre ? pre.textContent : null;
        }
    }
    return null;
}

function getEntryDataForMarkdown(btn) {
    const data = getTraceCardData(btn);
    if (!data) return null;

    const isMainRequest = data.card.classList.contains("request");

    let status = "";
    let duration = "";
    const statusEl = data.card.querySelector(".trace-card-meta .status");
    if (statusEl) {
        status = statusEl.textContent.trim();
    }
    const durationEl = data.card.querySelector(".trace-card-meta span:not(.status)");
    if (durationEl) {
        duration = durationEl.textContent.trim();
    }

    let requestBody = data.body;
    let responseBody = null;

    if (isMainRequest) {
        const responseCard = document.querySelector(".trace-card.response");
        if (responseCard) {
            responseBody = getPayloadBody(responseCard, "Body");
        }
    } else {
        responseBody = getPayloadBody(data.card, "Response Body");
    }

    let outgoingRequests = [];
    if (isMainRequest) {
        const depCards = document.querySelectorAll(".trace-card.dependency");
        depCards.forEach(depCard => {
            const depMethod = depCard.dataset.method;
            const depUrl = depCard.dataset.url;
            
            const depStatusEl = depCard.querySelector(".trace-card-meta .status");
            const depStatus = depStatusEl ? depStatusEl.textContent.trim() : "";
            
            const depDurationEl = depCard.querySelector(".trace-card-meta span:not(.status)");
            const depDuration = depDurationEl ? depDurationEl.textContent.trim() : "";
            
            if (depMethod && depUrl) {
                outgoingRequests.push({
                    method: depMethod,
                    url: depUrl,
                    status: depStatus,
                    duration: depDuration
                });
            }
        });
    }

    return {
        method: data.method,
        path: data.url,
        status: status,
        duration: duration,
        requestBody: requestBody,
        responseBody: responseBody,
        outgoingRequests: outgoingRequests
    };
}

function formatDurationToSeconds(durationStr) {
    if (!durationStr) return "";
    const ms = parseInt(durationStr.replace(/[^\d]/g, ""), 10);
    if (!isNaN(ms)) {
        return (ms / 1000).toFixed(3).replace(/\.?0+$/, "") + "s";
    }
    return durationStr;
}

function isJson(str) {
    if (!str) return false;
    const trimmed = str.trim();
    if ((trimmed.startsWith("{") && trimmed.endsWith("}")) || (trimmed.startsWith("[") && trimmed.endsWith("]"))) {
        try {
            JSON.parse(trimmed);
            return true;
        } catch (e) {
            return false;
        }
    }
    return false;
}

function generateMarkdownExport(entry) {
    let md = `### ${entry.method.toUpperCase()} ${entry.path} — ${entry.status} (${formatDurationToSeconds(entry.duration)})\n\n`;

    if (entry.requestBody && entry.requestBody.trim() !== "") {
        const isBodyJson = isJson(entry.requestBody);
        const lang = isBodyJson ? "json" : "";
        md += `**Request Body:**\n\`\`\`${lang}\n${entry.requestBody.trim()}\n\`\`\`\n\n`;
    }

    if (entry.responseBody && entry.responseBody.trim() !== "") {
        const isBodyJson = isJson(entry.responseBody);
        const lang = isBodyJson ? "json" : "";
        md += `**Response Body:**\n\`\`\`${lang}\n${entry.responseBody.trim()}\n\`\`\`\n\n`;
    }

    if (entry.outgoingRequests && entry.outgoingRequests.length > 0) {
        md += `**Outgoing Calls:**\n`;
        entry.outgoingRequests.forEach(req => {
            const statusStr = req.status ? ` — ${req.status}` : "";
            const durationStr = req.duration ? ` (${req.duration.replace(/\s+/g, "")})` : "";
            md += `- ${req.method.toUpperCase()} ${req.url}${statusStr}${durationStr}\n`;
        });
    }

    return md.trim();
}

function copyAsMarkdown(btn) {
    const entry = getEntryDataForMarkdown(btn);
    if (!entry) return;

    const markdown = generateMarkdownExport(entry);

    navigator.clipboard.writeText(markdown);

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

function updateUrlFilters(search, method, statusFamily) {
    const params = new URLSearchParams(window.location.search);

    if (search) {
        params.set("search", search);
    } else {
        params.delete("search");
    }

    if (method) {
        params.set("method", method);
    } else {
        params.delete("method");
    }

    if (statusFamily) {
        params.set("status", statusFamily);
    } else {
        params.delete("status");
    }

    const query = params.toString();
    const newUrl = window.location.pathname + (query ? "?" + query : "");
    window.history.replaceState(null, "", newUrl);
}

function applyRequestFilters() {
    if (!requestRows.length) return;

    const rawSearch = (requestSearch?.value ?? "").trim();
    const search = rawSearch.toLowerCase();
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

    updateUrlFilters(rawSearch, method, statusFamily);
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

// Load filters from URL on page load
if (requestRows.length > 0) {
    const params = new URLSearchParams(window.location.search);
    const searchVal = params.get("search");
    const methodVal = params.get("method");
    const statusVal = params.get("status");

    if (requestSearch && searchVal !== null) {
        requestSearch.value = searchVal;
    }
    if (methodFilter && methodVal !== null) {
        const optionExists = Array.from(methodFilter.options).some(opt => opt.value === methodVal);
        if (optionExists) {
            methodFilter.value = methodVal;
        }
    }
    if (statusFilter && statusVal !== null) {
        const optionExists = Array.from(statusFilter.options).some(opt => opt.value === statusVal);
        if (optionExists) {
            statusFilter.value = statusVal;
        }
    }

    applyRequestFilters();
}

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

// Global keyboard shortcuts listener for the DebugProbe dashboard.
document.addEventListener("keydown", (e) => {
    // Ignore keydown when a modifier key is held (Ctrl/Cmd/Alt)
    if (e.ctrlKey || e.metaKey || e.altKey) {
        return;
    }

    // Ignore repeating events from holding down keys
    if (e.repeat) {
        return;
    }

    const key = e.key;

    // "Escape" must always work, even inside input/textarea/contenteditable elements
    if (key === "Escape" || key === "Esc") {
        const backLink = document.querySelector('a[href="/debug"]') || 
                         Array.from(document.querySelectorAll("a")).find(a => a.textContent.includes("Back"));
        if (backLink) {
            backLink.click();
        } else {
            // If on the dashboard and in the search box, Esc blurs the input
            const activeEl = document.activeElement;
            if (activeEl && typeof activeEl.blur === "function") {
                activeEl.blur();
            }
        }
        return;
    }

    // Guard rail: skip all other shortcuts when in input/textarea/contenteditable
    const activeEl = document.activeElement;
    if (activeEl) {
        const tag = activeEl.tagName.toLowerCase();
        if (tag === "input" || tag === "textarea" || activeEl.isContentEditable) {
            return;
        }
    }

    switch (key) {
        case "/": {
            const searchInput = document.getElementById("requestSearch");
            if (searchInput) {
                e.preventDefault();
                searchInput.focus();
            }
            break;
        }
        case "c":
        case "C": {
            const curlBtn = document.querySelector(".trace-card.request .curl-copy-btn");
            if (curlBtn) {
                curlBtn.click();
            }
            break;
        }
    }
});

