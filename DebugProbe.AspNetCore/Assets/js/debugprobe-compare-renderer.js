window.runCompare = async function () {

    const id = window.location.pathname.split('/').pop();

    const base = document.getElementById('baseUrl').value.trim();

    const remoteId = document.getElementById('compareId').value.trim();

    if (!base || !remoteId) {
        alert('Fill both fields');
        return;
    }

    setCompareResult('<div class="compare-message">Comparing...</div>');

    try {

        const res =
            await fetch(
                `/debug/compare/${id}?baseUrl=${encodeURIComponent(base)}&remoteTraceId=${encodeURIComponent(remoteId)}`
            );

        if (!res.ok) {

            const text = await res.text();

            setCompareResult(`<div class="compare-message compare-message-error">${escapeHtml(text || 'Compare failed')}</div>`);

            return;
        }

        const result = await res.json();

        setCompareResult(renderCompare(result));

    } catch (error) {

        setCompareResult(
            `<div class="compare-message compare-message-error">${escapeHtml(error.message || 'Compare failed')}</div>`
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

    const localRequestBodyJson = normalizeJsonPayload(result.requestBody?.local || '');

    const remoteRequestBodyJson = normalizeJsonPayload(result.requestBody?.remote || '');

    const requestComparison = compareJsonBodies(localRequestBodyJson, remoteRequestBodyJson);

    const localResponseBodyJson = normalizeJsonPayload(result.responseBody?.local || '');

    const remoteResponseBodyJson = normalizeJsonPayload(result.responseBody?.remote || '');

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

            return `
                <div class="compare-row ${changed ? 'compare-row-changed' : ''}">
                    <span>${escapeHtml(row.field)}</span>
                    <code>${escapeHtml(row.local ?? '')}</code>
                    <code>${escapeHtml(row.remote ?? '')}</code>
                </div>
            `;
        }).join('');

    return `
        <div class="compare-table">
            <div class="compare-row compare-row-head">
                <span>Field</span>
                <span>Local</span>
                <span>Remote</span>
            </div>
            ${body}
        </div>
    `;
}

function renderSideBySideJson(comparison, localJson, remoteJson ) {
    return `
        <div class="json-compare">

            <div class="compare-pane">
                <div class="compare-pane-title">
                    <span>Local</span>
                </div>

                ${renderAlignedJson(comparison.local,localJson)}
            </div>

            <div class="compare-pane">
                <div class="compare-pane-title">
                    <span>Remote</span>
                </div>

                ${renderAlignedJson(comparison.remote, remoteJson )}
            </div>

        </div>
    `;
}

function renderAccordionSection(title, content, expanded = false, changes = 0) {
    return `
        <details class="compare-section payload-panel"${expanded ? ' open' : ''}>
            <summary>
                <span>${escapeHtml(title)}</span>
                <small>${changes > 0 ? `${changes} changes` : 'No changes'}</small>
            </summary>
            <div class="compare-section-body">
                ${content}
            </div>
        </details>
    `;
}

function renderAlignedJson(lines, originalJson) {
    if (!originalJson || originalJson.trim() === '') {
        return '<div class="compare-empty">Empty body</div>';
    }

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

function normalizeJsonPayload(value) {
    if (!value || !value.trim()) {
        return '';
    }

    try {
        return JSON.stringify(expandJsonStrings(JSON.parse(value)), null, 2);
    } catch {
        return value;
    }
}

function expandJsonStrings(value) {
    if (typeof value === 'string') {
        const trimmed = value.trim();

        if (
            (trimmed.startsWith('{') && trimmed.endsWith('}')) ||
            (trimmed.startsWith('[') && trimmed.endsWith(']'))
        ) {
            try {
                return expandJsonStrings(JSON.parse(trimmed));
            } catch {
                return value;
            }
        }

        return value;
    }

    if (Array.isArray(value)) {
        return value.map(expandJsonStrings);
    }

    if (value && typeof value === 'object') {
        return Object.fromEntries(
            Object.entries(value).map(([key, child]) => [key, expandJsonStrings(child)])
        );
    }

    return value;
}

function formatCopyValue(json, lines) {

    try {

        return JSON.stringify(JSON.parse(json), null, 2);

    } catch {

        return lines.map(line => line.text).join('\n');
    }
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
