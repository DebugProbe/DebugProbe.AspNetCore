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
        const remoteUrl = `${base.replace(/\/$/, '')}/debug/json/${remoteId}`;
        const res = await fetch(`/debug/compare/${id}?url=${encodeURIComponent(remoteUrl)}`);

        if (!res.ok) {
            const text = await res.json();
            setCompareResult(`<b style="color:red">${text || 'Compare failed'}</b>`);
            return;
        }

        setCompareResult(renderCompare(await res.json()));
    } catch {
        setCompareResult('<b style="color:red">Error during compare</b>');
    }
};

function setCompareResult(html) {
    document.getElementById('compareResult').innerHTML = html;
}

function renderCompare(result) {

    const environmentRows = [
        { field: 'Environment', local: result.environment?.local, remote: result.environment?.remote },
        { field: 'Culture', local: result.culture?.local, remote: result.culture?.remote }
    ];
    var environmentRowsChangedCount = getChangedCount(environmentRows);

    const overviewRows = [
        { field: 'Method', local: result.method?.local, remote: result.method?.remote },
        { field: 'Path', local: result.path?.local, remote: result.path?.remote },
        { field: 'Status', local: result.status?.local, remote: result.status?.remote },
        { field: 'Request Time', local: result.requestTime?.local, remote: result.requestTime?.remote }
    ];
    var overviewRowsChangedCount = getChangedCount(overviewRows);

    const localRequestBodyJson = result.requestBody?.local || ''
    const remoteRequestBodyJson = result.requestBody?.remote || ''
    const requestComparison = compareJsonBodies(localRequestBodyJson, remoteRequestBodyJson);

    const localResponseBodyJson = result.responseBody?.local || ''
    const remoteResponseBodyJson = result.responseBody?.remote || ''
    const responseComparison = compareJsonBodies(localResponseBodyJson, remoteResponseBodyJson);

    return [
        renderAccordionSection(
            'Environment',
            renderSectionRows(environmentRows),
            environmentRowsChangedCount > 0 ? true : false,
            environmentRowsChangedCount
        ),
        renderAccordionSection(
            'Overview',
            renderSectionRows(overviewRows),
            overviewRowsChangedCount > 0 ? true : false,
            overviewRowsChangedCount
        ),
        renderAccordionSection(
            'Request',
            renderSideBySideJson(requestComparison, localRequestBodyJson, remoteRequestBodyJson),
            requestComparison.changes > 0 ? true : false,
            requestComparison.changes
        ),
        renderAccordionSection(
            'Response',
            renderSideBySideJson(responseComparison, localResponseBodyJson, remoteResponseBodyJson),
            responseComparison.changes > 0 ? true : false,
            responseComparison.changes
        )
    ].join('');
}

function renderSectionRows(rows) {
    const body = rows.map(row => {
        const changed = row.local !== row.remote;
        const rowStyle = changed ? ' style="background:rgba(255,200,0,0.12)"' : '';
        const valueStyle = changed ? ' style="color:#e74c3c"' : '';

        return `<tr${rowStyle}>
            <td>${escapeHtml(row.field)}</td>
            <td${valueStyle}>${escapeHtml(row.local ?? '')}</td>
            <td${valueStyle}>${escapeHtml(row.remote ?? '')}</td>
        </tr>`;
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

function renderSideBySideJson(comparison, localJson, remoteJson) {
    return `<div class="json-compare">
        <div>
            <b>Local</b>
            ${comparison.localError ? `<div class="json-error">${comparison.localError}</div>` : '' }
            ${renderAlignedJson(comparison.local, localJson)}
        </div>

        <div>
            <b>Remote</b>
            ${comparison.remoteError ? `<div class="json-error">${comparison.remoteError}</div>` : '' }
            ${renderAlignedJson(comparison.remote, remoteJson)}
        </div>
    </div>`;
}

function renderAccordionSection(title, content, expanded = false, changes = 0) {
    return `
        <div class="accordion-section">
            <div class="accordion-header" onclick="toggleAccordion(this)">
                <div class="accordion-title">${escapeHtml(title)}</div>
                <div class="accordion-meta">
                    ${changes > 0 ? `<span class="diff-badge">${changes}</span>` : '' }

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

function toggleAccordion(header) {
    const body = header.nextElementSibling;
    const toggle = header.querySelector('.accordion-toggle');

    body.classList.toggle('open');

    toggle.textContent =
        body.classList.contains('open') ? '-' : '+';
}

function getChangedCount(rows) {
    return rows.filter(x => x.local !== x.remote).length;
}


function compareJsonBodies(localJson, remoteJson) {
    const local = parseJson(localJson);
    const remote = parseJson(remoteJson);

    const localEmpty = !local.raw || local.raw.trim() === '';
    const remoteEmpty = !remote.raw || remote.raw.trim() === '';

    if (localEmpty && remoteEmpty) {
        return {
            local: [{ text: '(empty)', state: '' }],
            remote: [{ text: '(empty)', state: '' }],
            changes: 0
        };
    }

    if (
        (!local.ok && !localEmpty) ||
        (!remote.ok && !remoteEmpty)
    ) {
        return {
            local: (localEmpty ? '(empty)' : local.raw)
                .split('\n')
                .map(x => ({
                    text: x,
                    state: !local.ok && !localEmpty ? 'invalid' : ''
                })),

            remote: (remoteEmpty ? '(empty)' : remote.raw)
                .split('\n')
                .map(x => ({
                    text: x,
                    state: !remote.ok && !remoteEmpty ? 'invalid' : ''
                })),

            localError: !local.ok && !localEmpty
                ? 'Failed To Parse JSON'
                : null,

            remoteError: !remote.ok && !remoteEmpty
                ? 'Failed To Parse JSON'
                : null,

            changes: 1
        };
    }

    if (localEmpty || remoteEmpty) {
        return {
            local: localEmpty
                ? [{ text: '(empty)', state: '' }]
                : stringifyLines(local.value, 0, false)
                    .map(x => ({ text: x, state: 'added' })),

            remote: remoteEmpty
                ? [{ text: '(empty)', state: '' }]
                : stringifyLines(remote.value, 0, false)
                    .map(x => ({ text: x, state: 'added' })),

            changes: 1
        };
    }

    const rows = createRows();
    appendValue(rows, local.value, remote.value, local.ok, remote.ok, 0, false);

    return rows;
}

function parseJson(json) {
    if (!json || json.trim() === '') {
        return { ok: false, value: null, raw: '' };
    }

    try {
        return {
            ok: true,
            value: JSON.parse(json),
            raw: json
        };
    } catch (error) {
        return {
            ok: false,
            value: null,
            raw: json,
            error: error.message
        };
    }
}

function emptyComparison() {
    return {
        local: [{ text: '(empty)', state: '' }],
        remote: [{ text: '(empty)', state: '' }]
    };
}

function createRows() {
    return { local: [], remote: [], changes: 0 };
}

function pushPair(rows, localText, remoteText, state = '') {
    rows.local.push({ text: localText, state });
    rows.remote.push({ text: remoteText, state });
}

function pushPresence(rows, value, side, depth, trailingComma) {
    rows.changes++;

    const isLocal = side === 'local';
    const lines = stringifyLines(value, depth, trailingComma);

    lines.forEach(line => {
        rows.local.push({ text: isLocal ? line : '', state: isLocal ? 'added' : 'missing' });
        rows.remote.push({ text: isLocal ? '' : line, state: isLocal ? 'missing' : 'added' });
    });
}

function appendValue(rows, localValue, remoteValue, hasLocal, hasRemote, depth, trailingComma) {
    if (!hasLocal || !hasRemote) {
        pushPresence(rows, hasLocal ? localValue : remoteValue, hasLocal ? 'local' : 'remote', depth, trailingComma);
        return;
    }

    if (isObject(localValue) && isObject(remoteValue)) {
        appendObject(rows, localValue, remoteValue, depth, trailingComma);
        return;
    }

    if (Array.isArray(localValue) && Array.isArray(remoteValue)) {
        appendArray(rows, localValue, remoteValue, depth, trailingComma);
        return;
    }

    if (isContainer(localValue) || isContainer(remoteValue)) {
        appendChangedBlocks(rows, localValue, remoteValue, depth, trailingComma);
        return;
    }

    const changed = !jsonEquals(localValue, remoteValue);

    if (changed) {
        rows.changes++;
    }

    const state = changed ? 'changed' : '';
    pushPair(rows, primitiveLine(localValue, depth, trailingComma), primitiveLine(remoteValue, depth, trailingComma), state);
}

function appendObject(rows, localObject, remoteObject, depth, trailingComma) {
    pushPair(rows, `${indent(depth)}{`, `${indent(depth)}{`);

    unionKeys(localObject, remoteObject).forEach((key, index, keys) => {
        appendProperty(rows, key, localObject, remoteObject, depth + 1, index < keys.length - 1);
    });

    pushPair(rows, `${indent(depth)}}${trailingComma ? ',' : ''}`, `${indent(depth)}}${trailingComma ? ',' : ''}`);
}

function appendProperty(rows, key, localObject, remoteObject, depth, trailingComma) {
    const hasLocal = hasOwn(localObject, key);
    const hasRemote = hasOwn(remoteObject, key);
    const localValue = hasLocal ? localObject[key] : null;
    const remoteValue = hasRemote ? remoteObject[key] : null;

    if (!hasLocal || !hasRemote) {
        pushNamedPresence(rows, key, hasLocal ? localValue : remoteValue, hasLocal ? 'local' : 'remote', depth, trailingComma);
        return;
    }

    if (isObject(localValue) && isObject(remoteValue)) {
        pushPair(rows, `${indent(depth)}"${key}": {`, `${indent(depth)}"${key}": {`, jsonEquals(localValue, remoteValue) ? '' : 'changed');
        unionKeys(localValue, remoteValue).forEach((childKey, index, keys) => {
            appendProperty(rows, childKey, localValue, remoteValue, depth + 1, index < keys.length - 1);
        });
        pushPair(rows, `${indent(depth)}}${trailingComma ? ',' : ''}`, `${indent(depth)}}${trailingComma ? ',' : ''}`);
        return;
    }

    if (Array.isArray(localValue) && Array.isArray(remoteValue)) {
        pushPair(rows, `${indent(depth)}"${key}": [`, `${indent(depth)}"${key}": [`, jsonEquals(localValue, remoteValue) ? '' : 'changed');
        appendArrayItems(rows, localValue, remoteValue, depth + 1);
        pushPair(rows, `${indent(depth)}]${trailingComma ? ',' : ''}`, `${indent(depth)}]${trailingComma ? ',' : ''}`);
        return;
    }

    if (isContainer(localValue) || isContainer(remoteValue)) {
        appendChangedNamedBlocks(rows, key, localValue, remoteValue, depth, trailingComma);
        return;
    }

    const changed = !jsonEquals(localValue, remoteValue);

    if (changed) {
        rows.changes++;
    }

    const state = changed ? 'changed' : '';
    pushPair(rows, propertyLine(key, localValue, depth, trailingComma), propertyLine(key, remoteValue, depth, trailingComma), state);
}

function appendArray(rows, localArray, remoteArray, depth, trailingComma) {
    pushPair(rows, `${indent(depth)}[`, `${indent(depth)}[`);
    appendArrayItems(rows, localArray, remoteArray, depth + 1);
    pushPair(rows, `${indent(depth)}]${trailingComma ? ',' : ''}`, `${indent(depth)}]${trailingComma ? ',' : ''}`);
}

function appendArrayItems(rows, localArray, remoteArray, depth) {
    alignArrayItems(localArray, remoteArray).forEach((pair, index, pairs) => {
        appendValue(rows, pair.local, pair.remote, pair.hasLocal, pair.hasRemote, depth, index < pairs.length - 1);
    });
}

function alignArrayItems(localArray, remoteArray) {
    const pairs = [];
    let localIndex = 0;
    let remoteIndex = 0;

    while (localIndex < localArray.length || remoteIndex < remoteArray.length) {
        if (localIndex < localArray.length && remoteIndex < remoteArray.length && jsonEquals(localArray[localIndex], remoteArray[remoteIndex])) {
            pairs.push(pair(localArray[localIndex], remoteArray[remoteIndex], true, true));
            localIndex++;
            remoteIndex++;
            continue;
        }

        const localStart = localIndex;
        const remoteStart = remoteIndex;
        const next = findNextEqualItem(localArray, remoteArray, localIndex, remoteIndex);

        localIndex = next.localIndex;
        remoteIndex = next.remoteIndex;

        appendUnmatchedItems(
            pairs,
            localArray.slice(localStart, localIndex),
            remoteArray.slice(remoteStart, remoteIndex)
        );
    }

    return pairs;
}

function findNextEqualItem(localArray, remoteArray, localStart, remoteStart) {
    let best = { localIndex: localArray.length, remoteIndex: remoteArray.length, distance: Number.MAX_SAFE_INTEGER };

    for (let i = localStart; i < localArray.length; i++) {
        for (let j = remoteStart; j < remoteArray.length; j++) {
            if (!jsonEquals(localArray[i], remoteArray[j])) {
                continue;
            }

            const distance = (i - localStart) + (j - remoteStart);
            if (distance < best.distance) {
                best = { localIndex: i, remoteIndex: j, distance };
            }
        }
    }

    return best;
}

function appendUnmatchedItems(pairs, localItems, remoteItems) {
    const sharedCount = Math.min(localItems.length, remoteItems.length);
    const extraRemoteCount = Math.max(0, remoteItems.length - localItems.length);
    const extraLocalCount = Math.max(0, localItems.length - remoteItems.length);
    const remoteOffset = extraRemoteCount > 0 ? bestOffset(localItems, remoteItems, extraRemoteCount) : 0;
    const localOffset = extraLocalCount > 0 ? bestOffset(remoteItems, localItems, extraLocalCount) : 0;

    for (let i = 0; i < remoteOffset; i++) {
        pairs.push(pair(null, remoteItems[i], false, true));
    }

    for (let i = 0; i < localOffset; i++) {
        pairs.push(pair(localItems[i], null, true, false));
    }

    for (let i = 0; i < sharedCount; i++) {
        pairs.push(pair(localItems[localOffset + i], remoteItems[remoteOffset + i], true, true));
    }

    for (let i = remoteOffset + sharedCount; i < remoteItems.length; i++) {
        pairs.push(pair(null, remoteItems[i], false, true));
    }

    for (let i = localOffset + sharedCount; i < localItems.length; i++) {
        pairs.push(pair(localItems[i], null, true, false));
    }
}

function bestOffset(shorterItems, longerItems, extraCount) {
    let offset = 0;
    let score = Number.NEGATIVE_INFINITY;

    for (let i = 0; i <= extraCount; i++) {
        const currentScore = shorterItems.reduce(
            (sum, item, index) => sum + similarity(item, longerItems[i + index]),
            0
        );

        if (currentScore > score) {
            score = currentScore;
            offset = i;
        }
    }

    return offset;
}

function similarity(localValue, remoteValue) {
    if (jsonEquals(localValue, remoteValue)) {
        return 1000;
    }

    if (Array.isArray(localValue) && Array.isArray(remoteValue)) {
        return localValue.reduce(
            (score, item, index) => score + similarity(item, remoteValue[index]),
            10 - Math.abs(localValue.length - remoteValue.length)
        );
    }

    if (isObject(localValue) && isObject(remoteValue)) {
        return unionKeys(localValue, remoteValue).reduce((score, key) => {
            if (!hasOwn(localValue, key) || !hasOwn(remoteValue, key)) {
                return score - 5;
            }

            return score + 5 + (jsonEquals(localValue[key], remoteValue[key]) ? 20 : similarity(localValue[key], remoteValue[key]));
        }, 20);
    }

    return typeof localValue === typeof remoteValue ? 1 : -10;
}

function pair(local, remote, hasLocal, hasRemote) {
    return { local, remote, hasLocal, hasRemote };
}

function pushNamedPresence(rows, key, value, side, depth, trailingComma) {
    const lines = stringifyLines(value, depth, trailingComma, `"${key}": `);
    const isLocal = side === 'local';

    lines.forEach(line => {
        rows.local.push({ text: isLocal ? line : '', state: isLocal ? 'added' : 'missing' });
        rows.remote.push({ text: isLocal ? '' : line, state: isLocal ? 'missing' : 'added' });
    });
}

function appendChangedBlocks(rows, localValue, remoteValue, depth, trailingComma) {
    rows.changes++;

    const localLines = stringifyLines(localValue, depth, trailingComma);
    const remoteLines = stringifyLines(remoteValue, depth, trailingComma);
    const count = Math.max(localLines.length, remoteLines.length);

    for (let i = 0; i < count; i++) {
        rows.local.push({ text: localLines[i] || '', state: localLines[i] ? 'changed' : 'missing' });
        rows.remote.push({ text: remoteLines[i] || '', state: remoteLines[i] ? 'changed' : 'missing' });
    }
}

function appendChangedNamedBlocks(rows, key, localValue, remoteValue, depth, trailingComma) {
    rows.changes++;

    const localLines = stringifyLines(localValue, depth, trailingComma, `"${key}": `);
    const remoteLines = stringifyLines(remoteValue, depth, trailingComma, `"${key}": `);
    const count = Math.max(localLines.length, remoteLines.length);

    for (let i = 0; i < count; i++) {
        rows.local.push({ text: localLines[i] || '', state: localLines[i] ? 'changed' : 'missing' });
        rows.remote.push({ text: remoteLines[i] || '', state: remoteLines[i] ? 'changed' : 'missing' });
    }
}

function stringifyLines(value, depth, trailingComma, firstLinePrefix = '') {
    const lines = JSON.stringify(value, null, 2).split('\n');

    return lines.map((line, index) => {
        const prefix = index === 0 ? firstLinePrefix : '';
        const comma = index === lines.length - 1 && trailingComma ? ',' : '';
        return `${indent(depth)}${prefix}${line}${comma}`;
    });
}

function renderAlignedJson(lines, originalJson) {
    const content = lines.map(line => {
        const className = line.state ? `diff-line diff-line-${line.state}` : '';
        const text = line.text ? escapeHtml(line.text) : '&nbsp;';

        return `<div class="${className}">${text}</div>`;
    }).join('');

    return `<div class="code-block">
        <button class="copy-btn" onclick="copyText(this)">Copy</button>
        <pre data-copy="${escapeHtml(formatCopyValue(originalJson, lines))}">${content}</pre>
    </div>`;
}

function formatCopyValue(json, lines) {
    const parsed = parseJson(json);

    if (parsed.ok) {
        return JSON.stringify(parsed.value, null, 2);
    }

    return lines.map(line => line.text).join('\n');
}

function propertyLine(key, value, depth, trailingComma) {
    return `${indent(depth)}"${key}": ${JSON.stringify(value)}${trailingComma ? ',' : ''}`;
}

function primitiveLine(value, depth, trailingComma) {
    return `${indent(depth)}${JSON.stringify(value)}${trailingComma ? ',' : ''}`;
}

function unionKeys(localObject, remoteObject) {
    return [...new Set([...Object.keys(localObject || {}), ...Object.keys(remoteObject || {})])];
}

function hasOwn(value, key) {
    return Object.prototype.hasOwnProperty.call(value, key);
}

function isContainer(value) {
    return Array.isArray(value) || isObject(value);
}

function isObject(value) {
    return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function jsonEquals(localValue, remoteValue) {
    return JSON.stringify(localValue) === JSON.stringify(remoteValue);
}

function indent(depth) {
    return '  '.repeat(depth);
}

function escapeHtml(value) {
    return String(value)
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');
}

