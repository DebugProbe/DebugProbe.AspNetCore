window.runCompare = async function () {

    const id = getLocalTraceId();
    const base = document.getElementById('baseUrl').value.trim();
    const remoteId = document.getElementById('compareId').value.trim();

    if (!base || !remoteId) {
        alert('Fill both fields');
        return;
    }

    setCompareResult('<div class="compare-message">Comparing...</div>');
    setCompareActionsVisible(false);

    try {
        const res = await fetch(`/debug/compare/${id}?baseUrl=${encodeURIComponent(base)}&remoteTraceId=${encodeURIComponent(remoteId)}`);

        if (!res.ok) {
            const text = await res.text();
            setCompareResult(`<div class="compare-message compare-message-error">${escapeHtml(text || 'Compare failed')}</div>`);
            return;
        }

        setCompareResult(renderCompare(await res.json()));
        setCompareActionsVisible(true);
    } catch (error) {
        setCompareResult(`<div class="compare-message compare-message-error">${escapeHtml(error.message || 'Compare failed')}</div>`);
    }
};

window.openCompareInNewTab = function () {
    const url = getCompareShareUrl();

    if (url) {
        window.open(url, '_blank', 'noopener');
    }
};

window.copyCompareShareLink = async function () {
    const url = getCompareShareUrl();
    const button = document.getElementById('copyCompareLink');

    if (!url || !button) {
        return;
    }

    await navigator.clipboard.writeText(url);

    button.title = 'Copied!';
    button.setAttribute('aria-label', 'Copied!');

    window.setTimeout(() => {
        button.title = 'Copy share link';
        button.setAttribute('aria-label', 'Copy share link');
    }, 1800);
};

function getCompareShareUrl() {
    const localTraceId = getLocalTraceId();
    const base = document.getElementById('baseUrl').value.trim();
    const traceId = document.getElementById('compareId').value.trim();

    if (!base || !traceId) {
        return '';
    }

    return `${window.location.origin}/compare?baseUrl=${encodeURIComponent(base)}&traceId=${encodeURIComponent(traceId)}&localTraceId=${encodeURIComponent(localTraceId)}`;
}

document.addEventListener('DOMContentLoaded', () => {
    const baseInput = document.getElementById('baseUrl');
    const traceInput = document.getElementById('compareId');

    if (!baseInput || !traceInput) {
        return;
    }

    const params = new URLSearchParams(window.location.search);
    const baseUrl = params.get('baseUrl');
    const traceId = params.get('traceId');

    if (!baseUrl || !traceId) {
        return;
    }

    baseInput.value = baseUrl;
    traceInput.value = traceId;
    window.runCompare();
});

function setCompareResult(html) {
    document.getElementById('compareResult').innerHTML = html;
}

function getLocalTraceId() {
    const input = document.getElementById('localTraceId');
    return input?.value || window.location.pathname.split('/').pop();
}

function setCompareActionsVisible(visible) {
    ['copyCompareLink', 'openCompareTab'].forEach(id => {
        const button = document.getElementById(id);
        if (button) {
            button.hidden = !visible;
        }
    });

    const copyButton = document.getElementById('copyCompareLink');
    if (copyButton) {
        copyButton.title = 'Copy share link';
        copyButton.setAttribute('aria-label', 'Copy share link');
    }
}

function renderCompare(result) {
    const model = buildCompareModel(result);

    return [
        renderSummary(model),
        renderDifferenceGroup('Critical Differences', model.critical, true),
        renderAccordionSection('Environment / App Info', renderSectionRows(model.environmentRows), model.environmentRows.some(isChangedRow), countChangedRows(model.environmentRows)),
        renderAccordionSection('Incoming Request', renderRequestComparison(model), model.requestChanges > 0, model.requestChanges),
        renderAccordionSection('Outgoing HTTP Calls', renderOutgoingComparison(model), model.outgoingChanges > 0, model.outgoingChanges),
        renderAccordionSection('Response', renderResponseComparison(model), model.responseChanges > 0, model.responseChanges),
        renderAccordionSection('Headers / Metadata', renderHeaderMetadata(model), false, model.headerChanges),
        renderAccordionSection('Ignored Dynamic Differences', renderIgnored(model.ignored), false, model.ignored.length),
        renderAccordionSection('Raw Diff', renderRawDiff(model), false, model.rawChanges)
    ].join('');
}

function buildCompareModel(result) {
    const local = result.localTrace || {};
    const remote = result.remoteTrace || {};
    const localEnvironment = result.localEnvironment || {};
    const remoteEnvironment = result.remoteEnvironment || {};
    const ignored = [];

    const localRequestBody = normalizeBody(local.requestBody || '', 'RequestBody', ignored, 'local', remote.requestBody || '');
    const remoteRequestBody = normalizeBody(remote.requestBody || '', 'RequestBody', ignored, 'remote', local.requestBody || '');
    const requestBodyComparison = compareJsonBodies(localRequestBody.normalized, remoteRequestBody.normalized);

    const localResponseBody = normalizeBody(local.responseBody || '', 'ResponseBody', ignored, 'local', remote.responseBody || '');
    const remoteResponseBody = normalizeBody(remote.responseBody || '', 'ResponseBody', ignored, 'remote', local.responseBody || '');
    const responseBodyComparison = compareJsonBodies(localResponseBody.normalized, remoteResponseBody.normalized);

    const requestRows = [
        row('Method', local.method, remote.method, 'Changed'),
        row('Path', normalizeUrlPath(local.path || local.requestUrl || ''), normalizeUrlPath(remote.path || remote.requestUrl || ''), 'Changed'),
        row('Query', normalizeQuery(local.query || ''), normalizeQuery(remote.query || ''), 'Changed'),
        row('Request Size', formatBytes(local.requestSize), formatBytes(remote.requestSize), 'Changed')
    ];

    const responseRows = [
        row('Status Code', local.statusCode, remote.statusCode, statusLabel(local.statusCode, remote.statusCode)),
        row('Duration', `${local.durationMs || 0} ms`, `${remote.durationMs || 0} ms`, durationLabel(local.durationMs, remote.durationMs)),
        row('Response Size', formatBytes(local.responseSize), formatBytes(remote.responseSize), 'Changed')
    ];

    const environmentRows = [
        row('Environment', localEnvironment.environment, remoteEnvironment.environment, 'Changed'),
        row('Culture', localEnvironment.culture, remoteEnvironment.culture, 'Changed'),
        row('UI Culture', localEnvironment.uiCulture, remoteEnvironment.uiCulture, 'Changed'),
        row('Machine', localEnvironment.machineName, remoteEnvironment.machineName, 'Changed'),
        row('Version', localEnvironment.assemblyVersion, remoteEnvironment.assemblyVersion, 'Changed'),
        row('Time Zone', localEnvironment.timeZone, remoteEnvironment.timeZone, 'Changed'),
        row('Decimal Separator', localEnvironment.decimalSeparator, remoteEnvironment.decimalSeparator, 'Changed'),
        row('Date Format', localEnvironment.dateFormat, remoteEnvironment.dateFormat, 'Changed')
    ];

    const requestHeaderRows = compareHeaders(local.requestHeaders, remote.requestHeaders, 'RequestHeaders', ignored);
    const responseHeaderRows = compareHeaders(local.responseHeaders, remote.responseHeaders, 'ResponseHeaders', ignored);
    const outgoing = compareOutgoing(local.outgoingRequests || [], remote.outgoingRequests || [], ignored);

    const critical = [];
    const warning = [];
    const info = [];

    if (local.statusCode !== remote.statusCode) {
        const severity = (local.statusCode >= 500 || remote.statusCode >= 500) ? critical : warning;
        severity.push(summaryItem('Response status changed', `${local.statusCode || '(missing)'} -> ${remote.statusCode || '(missing)'}`, statusLabel(local.statusCode, remote.statusCode)));
    }

    if (requestBodyComparison.changes > 0) {
        warning.push(summaryItem('Request payload differs', `${requestBodyComparison.changes} meaningful changes`, 'Changed'));
    }

    if (responseBodyComparison.changes > 0) {
        warning.push(summaryItem('Response body differs', `${responseBodyComparison.changes} meaningful changes`, 'Changed'));
    }

    const duration = durationDelta(local.durationMs, remote.durationMs);
    if (duration.isSlower) {
        warning.push(summaryItem('Remote response is slower', `${duration.delta} ms slower`, 'Slower'));
    }

    outgoing.summary.forEach(item => (item.severity === 'critical' ? critical : warning).push(item));

    countChangedRows(environmentRows) && info.push(summaryItem('Environment/app info differs', `${countChangedRows(environmentRows)} fields changed`, 'Info'));
    const meaningfulRequestHeaderChanges = countMeaningfulRows(requestHeaderRows);
    const meaningfulResponseHeaderChanges = countMeaningfulRows(responseHeaderRows);
    const uniqueIgnored = uniqueIgnoredItems(ignored);

    meaningfulRequestHeaderChanges && info.push(summaryItem('Request headers differ', `${meaningfulRequestHeaderChanges} useful headers changed`, 'Changed'));
    meaningfulResponseHeaderChanges && info.push(summaryItem('Response headers differ', `${meaningfulResponseHeaderChanges} useful headers changed`, 'Changed'));
    uniqueIgnored.length && info.push(summaryItem('Dynamic fields ignored', `${uniqueIgnored.length} normalized differences`, 'Ignored dynamic'));

    return {
        local,
        remote,
        critical,
        warning,
        info,
        ignored: uniqueIgnored,
        environmentRows,
        requestRows,
        responseRows,
        requestHeaderRows,
        responseHeaderRows,
        outgoing,
        requestBodyComparison,
        responseBodyComparison,
        localRequestBody: localRequestBody.normalized,
        remoteRequestBody: remoteRequestBody.normalized,
        localResponseBody: localResponseBody.normalized,
        remoteResponseBody: remoteResponseBody.normalized,
        requestChanges: countChangedRows(requestRows) + requestBodyComparison.changes,
        responseChanges: countChangedRows(responseRows) + responseBodyComparison.changes,
        outgoingChanges: outgoing.changeCount,
        headerChanges: countChangedRows(requestHeaderRows) + countChangedRows(responseHeaderRows),
        rawChanges: Array.isArray(result.diffs) ? result.diffs.length : 0
    };
}

function compareOutgoing(localCalls, remoteCalls, ignored) {
    const matches = matchOutgoingCalls(localCalls, remoteCalls);
    const summary = [];
    let changeCount = 0;

    const rows = matches.map((match, index) => {
        if (!match.local || !match.remote) {
            changeCount++;
            const missingLocal = !match.local;
            summary.push(summaryItem(
                missingLocal ? 'Extra outgoing dependency call' : 'Missing outgoing dependency call',
                `${displayCall(match.local || match.remote)}`,
                missingLocal ? 'Missing locally' : 'Missing remotely',
                'critical'
            ));

            return {
                index,
                local: match.local,
                remote: match.remote,
                label: displayCall(match.local || match.remote),
                status: missingLocal ? 'Missing locally' : 'Missing remotely',
                rows: [],
                requestBodyComparison: null,
                responseBodyComparison: null
            };
        }

        const callIgnored = [];
        const localRequestBody = normalizeBody(match.local.requestBody || '', `Outgoing[${index}].RequestBody`, callIgnored, 'local', match.remote.requestBody || '');
        const remoteRequestBody = normalizeBody(match.remote.requestBody || '', `Outgoing[${index}].RequestBody`, callIgnored, 'remote', match.local.requestBody || '');
        const localResponseBody = normalizeBody(match.local.responseBody || '', `Outgoing[${index}].ResponseBody`, callIgnored, 'local', match.remote.responseBody || '');
        const remoteResponseBody = normalizeBody(match.remote.responseBody || '', `Outgoing[${index}].ResponseBody`, callIgnored, 'remote', match.local.responseBody || '');
        ignored.push(...callIgnored);

        const requestBodyComparison = compareJsonBodies(localRequestBody.normalized, remoteRequestBody.normalized);
        const responseBodyComparison = compareJsonBodies(localResponseBody.normalized, remoteResponseBody.normalized);

        const rows = [
            row('Method', match.local.method, match.remote.method, 'Changed'),
            row('URL / Path', normalizeUrlPath(match.local.url), normalizeUrlPath(match.remote.url), 'Changed'),
            row('Status', match.local.statusCode ?? 'Failed', match.remote.statusCode ?? 'Failed', statusLabel(match.local.statusCode, match.remote.statusCode)),
            row('Duration', `${match.local.durationMs || 0} ms`, `${match.remote.durationMs || 0} ms`, durationLabel(match.local.durationMs, match.remote.durationMs)),
            row('Failure', shortException(match.local.exception), shortException(match.remote.exception), 'Failed')
        ];

        const changes = countChangedRows(rows) + requestBodyComparison.changes + responseBodyComparison.changes;
        changeCount += changes;

        if ((match.local.exception || match.remote.exception) && match.local.exception !== match.remote.exception) {
            summary.push(summaryItem('Outgoing dependency failure changed', displayCall(match.local), 'Failed', 'critical'));
        } else if (match.local.statusCode !== match.remote.statusCode) {
            summary.push(summaryItem('Outgoing dependency status changed', displayCall(match.local), `${match.local.statusCode || 'Failed'} -> ${match.remote.statusCode || 'Failed'}`, 'warning'));
        }

        const duration = durationDelta(match.local.durationMs, match.remote.durationMs);
        if (duration.isSlower) {
            summary.push(summaryItem('Outgoing dependency is slower remotely', `${displayCall(match.local)} by ${duration.delta} ms`, 'Slower', 'warning'));
        }

        return {
            index,
            local: match.local,
            remote: match.remote,
            label: displayCall(match.local),
            status: changes > 0 ? 'Changed' : 'Same',
            rows,
            requestBodyComparison,
            responseBodyComparison,
            localRequestBody: localRequestBody.normalized,
            remoteRequestBody: remoteRequestBody.normalized,
            localResponseBody: localResponseBody.normalized,
            remoteResponseBody: remoteResponseBody.normalized,
            requestHeaderRows: compareHeaders(match.local.requestHeaders, match.remote.requestHeaders, `Outgoing[${index}].RequestHeaders`, ignored),
            responseHeaderRows: compareHeaders(match.local.responseHeaders, match.remote.responseHeaders, `Outgoing[${index}].ResponseHeaders`, ignored)
        };
    });

    return { rows, summary, changeCount };
}

function matchOutgoingCalls(localCalls, remoteCalls) {
    const remoteUnused = new Set(remoteCalls.map((_, index) => index));
    const matches = [];

    localCalls.forEach((localCall, localIndex) => {
        let best = null;
        let bestScore = -1;

        remoteUnused.forEach(remoteIndex => {
            const score = outgoingScore(localCall, remoteCalls[remoteIndex], localIndex, remoteIndex);
            if (score > bestScore) {
                best = remoteIndex;
                bestScore = score;
            }
        });

        if (best !== null && bestScore >= 4) {
            remoteUnused.delete(best);
            matches.push({ local: localCall, remote: remoteCalls[best] });
        } else {
            matches.push({ local: localCall, remote: null });
        }
    });

    remoteUnused.forEach(index => matches.push({ local: null, remote: remoteCalls[index] }));
    return matches;
}

function outgoingScore(localCall, remoteCall, localIndex, remoteIndex) {
    let score = 0;
    if ((localCall.method || '').toUpperCase() === (remoteCall.method || '').toUpperCase()) score += 3;
    if (normalizeUrlPath(localCall.url) === normalizeUrlPath(remoteCall.url)) score += 6;
    if (normalizeUrlHost(localCall.url) === normalizeUrlHost(remoteCall.url)) score += 1;
    if (localIndex === remoteIndex) score += 2;
    return score;
}

function renderSummary(model) {
    const meaningful = model.critical.length + model.warning.length + model.info.filter(x => x.label !== 'Dynamic fields ignored').length;
    const headline = `${meaningful} meaningful ${meaningful === 1 ? 'difference' : 'differences'} detected`;

    return `
        <section class="compare-summary">
            <div class="compare-summary-head">
                <strong>${escapeHtml(headline)}</strong>
                <span>${escapeHtml(model.ignored.length ? `${model.ignored.length} dynamic values ignored` : 'No dynamic values ignored')}</span>
            </div>
            ${renderSummaryBucket('Critical', model.critical)}
            ${renderSummaryBucket('Warning', model.warning)}
            ${renderSummaryBucket('Info', model.info)}
        </section>
    `;
}

function renderSummaryBucket(title, items) {
    if (!items.length) {
        return `
            <div class="compare-summary-group">
                <h4>${escapeHtml(title)}</h4>
                <div class="compare-summary-empty">None</div>
            </div>
        `;
    }

    return `
        <div class="compare-summary-group">
            <h4>${escapeHtml(title)}</h4>
            ${items.map(item => `
                <div class="compare-summary-item">
                    <span class="compare-tag">${escapeHtml(item.status)}</span>
                    <strong>${escapeHtml(item.label)}</strong>
                    <small>${escapeHtml(item.detail)}</small>
                </div>
            `).join('')}
        </div>
    `;
}

function renderDifferenceGroup(title, items, expanded) {
    return renderAccordionSection(title, items.length ? renderSummaryBucket('', items) : '<div class="compare-empty">No critical differences</div>', expanded && items.length > 0, items.length);
}

function renderRequestComparison(model) {
    return [
        renderSectionRows(model.requestRows),
        renderAccordionSection('Normalized Request Body', renderSideBySideJson(model.requestBodyComparison, model.localRequestBody, model.remoteRequestBody), model.requestBodyComparison.changes > 0, model.requestBodyComparison.changes)
    ].join('');
}

function renderResponseComparison(model) {
    return [
        renderSectionRows(model.responseRows),
        renderAccordionSection('Normalized Response Body', renderSideBySideJson(model.responseBodyComparison, model.localResponseBody, model.remoteResponseBody), model.responseBodyComparison.changes > 0, model.responseBodyComparison.changes)
    ].join('');
}

function renderOutgoingComparison(model) {
    if (!model.outgoing.rows.length) {
        return '<div class="compare-empty">No outgoing dependency calls captured in either trace</div>';
    }

    return model.outgoing.rows.map(call => {
        const body = !call.local || !call.remote
            ? renderPresenceCall(call)
            : [
                renderSectionRows(call.rows),
                renderAccordionSection('Request Body', renderSideBySideJson(call.requestBodyComparison, call.localRequestBody, call.remoteRequestBody), call.requestBodyComparison.changes > 0, call.requestBodyComparison.changes),
                renderAccordionSection('Response Body', renderSideBySideJson(call.responseBodyComparison, call.localResponseBody, call.remoteResponseBody), call.responseBodyComparison.changes > 0, call.responseBodyComparison.changes),
                renderAccordionSection('Headers', renderSectionRows(call.requestHeaderRows) + renderSectionRows(call.responseHeaderRows), false, countChangedRows(call.requestHeaderRows) + countChangedRows(call.responseHeaderRows))
            ].join('');

        return renderAccordionSection(`${call.index + 1}. ${call.label}`, body, call.status !== 'Same', call.status);
    }).join('');
}

function renderPresenceCall(call) {
    const value = call.local || call.remote;
    return renderSectionRows([
        row('Method', call.local?.method, call.remote?.method, call.status),
        row('URL', call.local?.url, call.remote?.url, call.status),
        row('Status', call.local?.statusCode, call.remote?.statusCode, call.status),
        row('Duration', call.local ? `${call.local.durationMs || 0} ms` : '', call.remote ? `${call.remote.durationMs || 0} ms` : '', call.status),
        row('Failure', shortException(value?.exception), shortException(value?.exception), call.status)
    ]);
}

function renderHeaderMetadata(model) {
    return [
        '<h4 class="compare-subhead">Request Headers</h4>',
        renderSectionRows(model.requestHeaderRows),
        '<h4 class="compare-subhead">Response Headers</h4>',
        renderSectionRows(model.responseHeaderRows),
        '<h4 class="compare-subhead">Trace Metadata</h4>',
        renderSectionRows([
            row('Local Trace ID', model.local.id, model.remote.id, 'Ignored dynamic'),
            row('Request Time', formatTime(model.local.requestTimeUtc), formatTime(model.remote.requestTimeUtc), 'Ignored dynamic'),
            row('Captured At', formatTime(model.local.timestamp), formatTime(model.remote.timestamp), 'Ignored dynamic')
        ])
    ].join('');
}

function renderIgnored(items) {
    if (!items.length) {
        return '<div class="compare-empty">No dynamic differences were normalized</div>';
    }

    return renderSectionRows(items.map(item => ({
        field: item.path,
        local: item.local,
        remote: item.remote,
        status: 'Ignored dynamic',
        changed: true
    })));
}

function renderRawDiff(model) {
    return [
        '<h4 class="compare-subhead">Request Body</h4>',
        renderSideBySideJson(compareJsonBodies(normalizeJsonPayload(model.local.requestBody || ''), normalizeJsonPayload(model.remote.requestBody || '')), normalizeJsonPayload(model.local.requestBody || ''), normalizeJsonPayload(model.remote.requestBody || '')),
        '<h4 class="compare-subhead">Response Body</h4>',
        renderSideBySideJson(compareJsonBodies(normalizeJsonPayload(model.local.responseBody || ''), normalizeJsonPayload(model.remote.responseBody || '')), normalizeJsonPayload(model.local.responseBody || ''), normalizeJsonPayload(model.remote.responseBody || ''))
    ].join('');
}

function renderSectionRows(rows) {
    if (!rows.length) {
        return '<div class="compare-empty">No fields captured</div>';
    }

    const body = rows.map(item => {
        const changed = isChangedRow(item);
        return `
            <div class="compare-row ${changed ? 'compare-row-changed' : ''}">
                <span>${escapeHtml(item.field)}</span>
                <code>${escapeHtml(item.local ?? '')}</code>
                <code>${escapeHtml(item.remote ?? '')}</code>
                <strong>${escapeHtml(changed ? (item.status || 'Changed') : 'Same')}</strong>
            </div>
        `;
    }).join('');

    return `
        <div class="compare-table">
            <div class="compare-row compare-row-head">
                <span>Field</span>
                <span>Local</span>
                <span>Remote</span>
                <span>Status</span>
            </div>
            ${body}
        </div>
    `;
}

function renderSideBySideJson(comparison, localJson, remoteJson) {
    return `
        <div class="json-compare">
            <div class="compare-pane">
                <div class="compare-pane-title"><span>Local</span></div>
                ${renderAlignedJson(comparison.local, localJson)}
            </div>
            <div class="compare-pane">
                <div class="compare-pane-title"><span>Remote</span></div>
                ${renderAlignedJson(comparison.remote, remoteJson)}
            </div>
        </div>
    `;
}

function renderAccordionSection(title, content, expanded = false, changes = 0) {
    const count = typeof changes === 'number' ? (changes > 0 ? `${changes} changes` : 'No changes') : changes;
    return `
        <details class="compare-section payload-panel"${expanded ? ' open' : ''}>
            <summary>
                <span>${escapeHtml(title)}</span>
                <small>${escapeHtml(count)}</small>
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

    const content = lines.map(line => {
        const className = line.state ? `diff-line diff-line-${line.state}` : '';
        const text = line.text ? escapeHtml(line.text) : '&nbsp;';
        return `<div class="${className}">${text}</div>`;
    }).join('');

    return `
        <div class="code-block">
            <button class="copy-btn" onclick="copyText(this)">Copy</button>
            <pre data-copy="${escapeHtml(formatCopyValue(originalJson, lines))}">${content}</pre>
        </div>
    `;
}

function compareHeaders(localHeaders = {}, remoteHeaders = {}, path, ignored) {
    const keys = unionKeys(localHeaders, remoteHeaders).filter(isUsefulHeader).sort((a, b) => a.localeCompare(b));

    return keys.map(key => {
        const local = normalizeScalar(localHeaders[key], `${path}.${key}`, key);
        const remote = normalizeScalar(remoteHeaders[key], `${path}.${key}`, key);
        const changed = local.normalized !== remote.normalized;

        if (changed && local.dynamic && remote.dynamic) {
            ignored.push({ path: `${path}.${key}`, local: localHeaders[key], remote: remoteHeaders[key] });
            return row(key, localHeaders[key], remoteHeaders[key], 'Ignored dynamic', true);
        }

        return row(key, local.normalized, remote.normalized, 'Changed');
    });
}

function normalizeBody(value, path, ignored, side, otherValue) {
    if (!value || !value.trim()) {
        return { normalized: '' };
    }

    try {
        const current = expandJsonStrings(JSON.parse(value));
        const other = otherValue && otherValue.trim() ? expandJsonStrings(JSON.parse(otherValue)) : null;
        const normalized = normalizeJsonValue(current, path, other, ignored);
        return { normalized: JSON.stringify(normalized, null, 2) };
    } catch {
        const current = normalizeScalar(value, path, path);
        const other = normalizeScalar(otherValue, path, path);
        if (value !== otherValue && current.normalized === other.normalized && current.dynamic) {
            ignored.push({ path, local: side === 'local' ? value : otherValue, remote: side === 'remote' ? value : otherValue });
        }
        return { normalized: current.normalized };
    }
}

function normalizeJsonValue(value, path, otherValue, ignored) {
    if (Array.isArray(value)) {
        return value.map((item, index) => normalizeJsonValue(item, `${path}[${index}]`, Array.isArray(otherValue) ? otherValue[index] : undefined, ignored));
    }

    if (value && typeof value === 'object') {
        return Object.fromEntries(Object.entries(value).map(([key, child]) => [
            key,
            normalizeJsonValue(child, `${path}.${key}`, otherValue && typeof otherValue === 'object' ? otherValue[key] : undefined, ignored)
        ]));
    }

    const key = lastPathSegment(path);
    const current = normalizeScalar(value, path, key);
    const other = normalizeScalar(otherValue, path, key);

    if (value !== otherValue && current.normalized === other.normalized && current.dynamic) {
        ignored.push({ path, local: value, remote: otherValue });
    }

    return current.normalized;
}

function normalizeScalar(value, path, key) {
    if (value === undefined || value === null) {
        return { normalized: value, dynamic: false };
    }

    const text = String(value);
    const lowerKey = String(key || '').toLowerCase();

    if (isSensitiveKey(lowerKey)) {
        return { normalized: '[ignored dynamic value: sensitive]', dynamic: true };
    }

    if (isDynamicKey(lowerKey)) {
        return { normalized: `[ignored dynamic value: ${dynamicReason(lowerKey)}]`, dynamic: true };
    }

    if (isDynamicValue(text)) {
        return { normalized: '[ignored dynamic value]', dynamic: true };
    }

    return { normalized: value, dynamic: false };
}

function isSensitiveKey(key) {
    return key.includes('authorization') || key.includes('cookie') || key.includes('token') || key.includes('secret') || key.includes('password') || key.includes('apikey') || key.includes('api-key');
}

function isDynamicKey(key) {
    if (['id', 'guid', 'uuid', 'timestamp', 'createdat', 'updatedat', 'requestid', 'correlationid', 'traceid', 'spanid', 'nonce', 'signature'].includes(key)) return true;
    return /(^|[_-])(id|guid|uuid|token|nonce|signature)$/.test(key) ||
        /(id|guid|uuid|timestamp|requestid|correlationid|traceid|spanid)$/.test(key) ||
        key.includes('date') ||
        key.endsWith('time') ||
        key.endsWith('at');
}

function dynamicReason(key) {
    if (key.includes('date') || key.includes('time') || key.endsWith('at')) return 'timestamp';
    if (key.includes('token') || key.includes('secret')) return 'token';
    return 'id';
}

function isDynamicValue(value) {
    const text = String(value).trim();
    if (!text) return false;
    if (/^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(text)) return true;
    if (/^\d{4}-\d{2}-\d{2}(t|\s)\d{2}:\d{2}:\d{2}/i.test(text)) return true;
    if (/^\d{13,}$/.test(text)) return true;
    if (/^eyJ[a-z0-9_-]+\.[a-z0-9_-]+\.[a-z0-9_-]+$/i.test(text)) return true;
    if (/^[a-f0-9]{24,}$/i.test(text)) return true;
    return /^[a-z0-9_-]{32,}$/i.test(text) && /[0-9]/.test(text) && /[a-z]/i.test(text);
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
        if ((trimmed.startsWith('{') && trimmed.endsWith('}')) || (trimmed.startsWith('[') && trimmed.endsWith(']'))) {
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
        return Object.fromEntries(Object.entries(value).map(([key, child]) => [key, expandJsonStrings(child)]));
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

function row(field, local, remote, status, forceChanged = false) {
    return {
        field,
        local: local ?? '',
        remote: remote ?? '',
        status,
        changed: forceChanged || String(local ?? '') !== String(remote ?? '')
    };
}

function isChangedRow(item) {
    return item.changed || String(item.local ?? '') !== String(item.remote ?? '');
}

function countChangedRows(rows) {
    return rows.filter(isChangedRow).length;
}

function countMeaningfulRows(rows) {
    return rows.filter(item => isChangedRow(item) && item.status !== 'Ignored dynamic').length;
}

function uniqueIgnoredItems(items) {
    const seen = new Set();
    return items.filter(item => {
        const key = `${item.path}|${item.local}|${item.remote}`;
        if (seen.has(key)) return false;
        seen.add(key);
        return true;
    });
}

function summaryItem(label, detail, status, severity = 'info') {
    return { label, detail, status, severity };
}

function durationDelta(localMs, remoteMs) {
    const local = Number(localMs || 0);
    const remote = Number(remoteMs || 0);
    const delta = remote - local;
    return { delta, isSlower: delta >= 500 && remote >= local * 1.5 };
}

function durationLabel(localMs, remoteMs) {
    const delta = durationDelta(localMs, remoteMs);
    if (delta.isSlower) return 'Slower';
    return Number(localMs || 0) === Number(remoteMs || 0) ? 'Same' : 'Changed';
}

function statusLabel(localStatus, remoteStatus) {
    if (localStatus === remoteStatus) return 'Same';
    if (!localStatus) return 'Missing locally';
    if (!remoteStatus) return 'Missing remotely';
    if (localStatus >= 500 || remoteStatus >= 500) return 'Failed';
    return 'Changed';
}

function displayCall(call) {
    return `${call.method || ''} ${normalizeUrlPath(call.url || '')}`.trim();
}

function normalizeUrlPath(value) {
    if (!value) return '';

    try {
        const url = new URL(value, 'http://debugprobe.local');
        const query = normalizeQuery(url.search);
        const path = normalizePathSegments(url.pathname);
        return `${path}${query ? `?${query}` : ''}`;
    } catch {
        return normalizeQueryInText(value);
    }
}

function normalizeUrlHost(value) {
    try {
        return new URL(value, 'http://debugprobe.local').host;
    } catch {
        return '';
    }
}

function normalizePathSegments(path) {
    return String(path || '')
        .split('/')
        .map(segment => isDynamicValue(segment) ? '[ignored-dynamic]' : segment)
        .join('/');
}

function normalizeQuery(value) {
    const query = String(value || '').replace(/^\?/, '');
    if (!query) return '';

    const params = new URLSearchParams(query);
    return [...params.entries()]
        .filter(([key]) => !isDynamicKey(key.toLowerCase()) && !isSensitiveKey(key.toLowerCase()))
        .map(([key, val]) => [key, normalizeScalar(val, key, key).normalized])
        .sort((a, b) => a[0].localeCompare(b[0]))
        .map(([key, val]) => `${key}=${val}`)
        .join('&');
}

function normalizeQueryInText(value) {
    const parts = String(value).split('?');
    return parts.length === 1 ? value : `${parts[0]}?${normalizeQuery(parts.slice(1).join('?'))}`;
}

function isUsefulHeader(key) {
    const lower = String(key || '').toLowerCase();
    return !['date', 'server', 'x-powered-by', 'content-length'].includes(lower);
}

function shortException(value) {
    if (!value) return '';
    return String(value).split('\n')[0].slice(0, 180);
}

function lastPathSegment(path) {
    const normalized = String(path || '').replace(/\[[0-9]+\]/g, '');
    const parts = normalized.split('.');
    return parts[parts.length - 1] || normalized;
}

function formatBytes(value) {
    const bytes = Number(value || 0);
    if (bytes >= 1048576) return `${(bytes / 1048576).toFixed(1)} MB`;
    if (bytes >= 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${bytes} B`;
}

function formatTime(value) {
    if (!value) return '';
    try {
        return new Date(value).toLocaleString();
    } catch {
        return value;
    }
}

function unionKeys(localObject = {}, remoteObject = {}) {
    return [...new Set([...Object.keys(localObject || {}), ...Object.keys(remoteObject || {})])];
}

function escapeHtml(value) {
    return String(value ?? '')
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');
}
