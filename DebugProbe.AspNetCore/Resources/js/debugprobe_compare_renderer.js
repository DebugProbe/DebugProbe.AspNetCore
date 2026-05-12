window.runCompare = async function () {

    const id = window.location.pathname.split('/').pop();

    const base = document.getElementById('baseUrl').value.trim();

    const remoteId = document.getElementById('compareId').value.trim();

    if (!base || !remoteId) {
        alert('Fill both fields');
        return;
    }

    setCompareResult('<b style="color:orange">Comparing...</b>');

    try {

        const res =
            await fetch(
                `/debug/compare/${id}?baseUrl=${encodeURIComponent(base)}&remoteTraceId=${encodeURIComponent(remoteId)}`
            );

        if (!res.ok) {

            const text = await res.text();

            setCompareResult(`<b style="color:red">${text || 'Compare failed'}</b>`);

            return;
        }

        const result = await res.json();

        setCompareResult(renderCompare(result));

    } catch {

        setCompareResult(
            `<b style="color:red">${error}</b>`
        );
    }
};

function setCompareResult(html) {

    document.getElementById('compareResult')
        .innerHTML = html;
}

function renderCompare(result) {

    const environmentRows = [
        {
            field: 'Environment',
            local: result.environment?.local,
            remote: result.environment?.remote
        },
        {
            field: 'Culture',
            local: result.culture?.local,
            remote: result.culture?.remote
        }
    ];

    const environmentRowsChangedCount =
        getChangedCount(environmentRows);

    const overviewRows = [
        {
            field: 'Method',
            local: result.method?.local,
            remote: result.method?.remote
        },
        {
            field: 'Path',
            local: result.path?.local,
            remote: result.path?.remote
        },
        {
            field: 'Status',
            local: result.status?.local,
            remote: result.status?.remote
        },
        {
            field: 'Request Time',
            local: result.requestTime?.local,
            remote: result.requestTime?.remote
        }
    ];

    const overviewRowsChangedCount = getChangedCount(overviewRows);

    const localRequestBodyJson = result.requestBody?.local || '';

    const remoteRequestBodyJson = result.requestBody?.remote || '';

    const requestComparison = compareJsonBodies(localRequestBodyJson, remoteRequestBodyJson);

    const localResponseBodyJson = result.responseBody?.local || '';

    const remoteResponseBodyJson = result.responseBody?.remote || '';

    const responseComparison = compareJsonBodies(localResponseBodyJson, remoteResponseBodyJson);

    return [

        renderAccordionSection(
            'Environment',
            renderSectionRows(environmentRows),
            environmentRowsChangedCount > 0,
            environmentRowsChangedCount
        ),

        renderAccordionSection(
            'Overview',
            renderSectionRows(overviewRows),
            overviewRowsChangedCount > 0,
            overviewRowsChangedCount
        ),

        renderAccordionSection(
            'Request',
            renderSideBySideJson(
                requestComparison,
                localRequestBodyJson,
                remoteRequestBodyJson
            ),
            requestComparison.changes > 0,
            requestComparison.changes
        ),

        renderAccordionSection(
            'Response',
            renderSideBySideJson(
                responseComparison,
                localResponseBodyJson,
                remoteResponseBodyJson
            ),
            responseComparison.changes > 0,
            responseComparison.changes
        )

    ].join('');
}

function renderSectionRows(rows) {

    const body =
        rows.map(row => {
            const changed = row.local !== row.remote;

            const rowStyle = changed ? ' style="background:rgba(255,200,0,0.12)"' : '';

            const valueStyle = changed ? ' style="color:#e74c3c"' : '';

            return `
                <tr${rowStyle}>
                    <td>${escapeHtml(row.field)}</td>
                    <td${valueStyle}>${escapeHtml(row.local ?? '')}</td>
                    <td${valueStyle}>${escapeHtml(row.remote ?? '')}</td>
                </tr>
            `;
        }).join('');

    return `
        <table>
            <tr>
                <th>Field</th>
                <th>Local</th>
                <th>Remote</th>
            </tr>

            ${body}
        </table>
    `;
}

function renderSideBySideJson(comparison, localJson, remoteJson ) {
    return `
        <div class="json-compare">

            <div>
                <div class="compare-pane-title">
                    <b>Local</b>
                    ${renderPayloadBadge(localJson)}
                </div>

                ${renderAlignedJson(comparison.local,localJson)}
            </div>

            <div>
                <div class="compare-pane-title">
                    <b>Remote</b>
                    ${renderPayloadBadge(remoteJson)}
                </div>

                ${renderAlignedJson(comparison.remote, remoteJson )}
            </div>

        </div>
    `;
}

function renderPayloadBadge(value) {

    const payloadType = getPayloadType(value);

    return `<span class="code-badge ${payloadType.className}">${payloadType.label}</span>`;
}

function getPayloadType(value) {

    if (!value || !value.trim()) {
        return {
            label: 'Empty',
            className: 'payload-empty'
        };
    }

    try {
        JSON.parse(value);

        return {
            label: 'JSON',
            className: 'payload-json'
        };
    } catch {
        return looksLikeJson(value)
            ? {
                label: 'Invalid JSON',
                className: 'payload-invalid-json'
            }
            : {
                label: 'Plain Text',
                className: 'payload-text'
            };
    }
}

function looksLikeJson(value) {

    const trimmed = value.trimStart();

    return trimmed.startsWith('{') || trimmed.startsWith('[');
}

function renderAccordionSection(title, content, expanded = false, changes = 0) {
    return `
        <div class="accordion-section">

            <div class="accordion-header" onclick="toggleAccordion(this)">

                <div class="accordion-title">
                    ${escapeHtml(title)}
                </div>

                <div class="accordion-meta">

                    ${changes > 0 ? `<span class="code-badge diff-badge">${changes}</span>` : ''}

                    <span class="accordion-toggle">
                        ${expanded ? '-' : '+'}
                    </span>

                </div>
            </div>

            <div class="accordion-body ${expanded ? 'open' : ''}">
                ${content}
            </div>

        </div>
    `;
}

function renderAlignedJson(lines, originalJson) {

    const content =
        lines.map(line => {

            const className = line.state ? `diff-line diff-line-${line.state}` : '';

            const text = line.text ? escapeHtml(line.text) : '&nbsp;';

            return `<div class="${className}">${text}</div>`;
        }).join('');

    return `
        <div class="code-block">

            <button class="copy-btn" onclick="copyText(this)">
                Copy
            </button>

            <pre data-copy="${escapeHtml(formatCopyValue(originalJson, lines))}">${content}</pre>
        </div>
    `;
}

function formatCopyValue(json, lines) {

    try {

        return JSON.stringify(JSON.parse(json), null, 2);

    } catch {

        return lines.map(line => line.text).join('\n');
    }
}

function toggleAccordion(header) {

    const body = header.nextElementSibling;

    const toggle = header.querySelector('.accordion-toggle');

    body.classList.toggle('open');

    toggle.textContent = body.classList.contains('open') ? '-' : '+';
}

function getChangedCount(rows) {

    return rows.filter(x => x.local !== x.remote).length;
}

function escapeHtml(value) {

    return String(value)
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');
}
