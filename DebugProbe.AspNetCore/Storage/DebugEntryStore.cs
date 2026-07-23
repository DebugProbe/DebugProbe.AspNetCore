using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DebugProbe.AspNetCore.Internal.Utils;
using DebugProbe.AspNetCore.Models;
using DebugProbe.AspNetCore.Options;

namespace DebugProbe.AspNetCore.Storage;

/// <summary>
/// Stores captured DebugProbe entries in memory.
/// </summary>
/// <remarks>
/// Storage approach (Option B): The existing ConcurrentQueue&lt;DebugEntry&gt; handles the normal
/// FIFO unpinned stream. A separate ConcurrentDictionary&lt;string, DebugEntry&gt; holds pinned
/// entries, which are excluded from normal FIFO eviction. Both collections are merged at read
/// time (GetAll, Get). Total entry count (pinned + unpinned) is still bounded by MaxEntries.
///
/// Clear() resets both collections — it is a full-session reset; leaving ghost pinned entries
/// from a prior session would create a confusing split-brain state on the dashboard.
/// </remarks>
public class DebugEntryStore
{
    private static readonly Regex GuidRegex = new(@"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b", RegexOptions.Compiled);
    private static readonly Regex NumberRegex = new(@"\b\d+(\.\d+)?\b", RegexOptions.Compiled);

    /// <summary>
    /// Maximum number of entries that may be simultaneously pinned.
    /// Hardcoded as a constant — pin is a session debugging tool, 5 is generous,
    /// and exposing this as a config property would add API surface for marginal benefit.
    /// </summary>
    public const int MaxPinnedEntries = 5;

    /// <summary>
    /// Gets the static instance of DebugEntryStore.
    /// </summary>
    public static DebugEntryStore? Instance { get; private set; }

    /// <summary>
    /// Gets the exception groups.
    /// </summary>
    public ConcurrentDictionary<string, ExceptionGroup> ExceptionGroups { get; } = new();

    /// <summary>
    /// Gets environment information for the current application.
    /// </summary>
    public DebugEnvironment Environment { get; }

    // Option B: separate collections for unpinned (FIFO) and pinned (protected) entries.
    private readonly ConcurrentQueue<DebugEntry> _queue = new();
    private readonly ConcurrentDictionary<string, DebugEntry> _pinned = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DebugEnvironment> _entryEnvironments = new();
    private readonly int _limit;

    // Lock used only during the capacity-trim step in Add() to prevent races
    // between concurrent callers when both the pinned and unpinned counts are near the limit.
    // The lock scope is deliberately narrow (just the trim loop) so it is not on the hot path.
    private readonly object _trimLock = new();

    public DebugEntryStore(DebugProbeOptions options)
    {
        Instance = this;
        _limit = options.MaxEntries;

        Environment = new DebugEnvironment
        {
            Environment = EnvironmentUtils.TryGetEnvironment(),
            MachineName = System.Environment.MachineName,
            AssemblyVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(),
            TimeZone = TimeZoneInfo.Local.DisplayName,
            Culture = CultureInfo.CurrentCulture.Name,
            UiCulture = CultureInfo.CurrentUICulture.Name,
            DecimalSeparator = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator,
            DateFormat = GetDateFormat()
        };
    }

    public void Add(DebugEntry entry)
    {
        Add(entry, Environment);
    }

    public void Add(DebugEntry entry, DebugEnvironment environment)
    {
        _queue.Enqueue(entry);
        if (environment != null && entry.Id != null)
        {
            _entryEnvironments[entry.Id] = environment;
        }

        if (TryParseException(entry.ResponseBody, out var type, out var message))
        {
            var normalizedMessage = NormalizeMessage(message);
            var fingerprint = ComputeHash(type + normalizedMessage);

            ExceptionGroups.AddOrUpdate(fingerprint,
                key => new ExceptionGroup
                {
                    Fingerprint = fingerprint,
                    Type = type,
                    SampleMessage = message,
                    Count = 1,
                    LastSeen = DateTimeOffset.UtcNow
                },
                (key, existing) => new ExceptionGroup
                {
                    Fingerprint = existing.Fingerprint,
                    Type = existing.Type,
                    SampleMessage = existing.SampleMessage,
                    Count = existing.Count + 1,
                    LastSeen = DateTimeOffset.UtcNow
                });
        }

        // Trim unpinned entries until total (pinned + unpinned) fits within MaxEntries.
        // Only unpinned entries are evicted; pinned entries are protected.
        // The narrow lock here prevents two concurrent Add() calls from both reading
        // stale counts and over-trimming or under-trimming.
        lock (_trimLock)
        {
            while (_queue.Count + _pinned.Count > _limit)
            {
                // ExceptionGroups counts are a running tally and must NOT be decremented on eviction.
                if (_queue.TryDequeue(out var evicted) && evicted.Id != null)
                {
                    _entryEnvironments.TryRemove(evicted.Id, out _);
                }
                else
                {
                    // Queue is empty — all remaining capacity is consumed by pinned entries.
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Attempts to toggle the pinned state of the entry with the given ID.
    /// Returns (success: true, newPinnedState, error: null) on success,
    /// or (success: false, _, error) when the cap would be exceeded.
    /// </summary>
    public (bool Success, bool IsPinned, string? Error) TryPin(string id)
    {
        // Look in both collections.
        var entry = _queue.FirstOrDefault(x => x.Id == id)
                 ?? (_pinned.TryGetValue(id, out var p) ? p : null);

        if (entry is null)
        {
            return (false, false, "Entry not found.");
        }

        if (entry.IsPinned)
        {
            // Unpin: remove from pinned dict, mark IsPinned=false,
            // re-add to queue so it participates in normal FIFO eviction again.
            if (_pinned.TryRemove(id, out _))
            {
                entry.IsPinned = false;

                // Re-enqueue so the entry is visible in the normal stream.
                // This may briefly exceed MaxEntries by one before the next Add() trims,
                // which is acceptable — the entry was already stored.
                _queue.Enqueue(entry);
            }

            return (true, false, null);
        }
        else
        {
            // Pin: enforce the cap first.
            if (_pinned.Count >= MaxPinnedEntries)
            {
                return (false, false,
                    $"Pin cap reached. At most {MaxPinnedEntries} entries may be pinned simultaneously.");
            }

            entry.IsPinned = true;
            _pinned[id] = entry;

            // The entry remains in _queue too until it would have been evicted naturally,
            // but GetAll() deduplicates by ID, so it is only shown once on the dashboard.

            return (true, true, null);
        }
    }

    public DebugEnvironment GetEnvironment(DebugEntry entry)
    {
        if (entry.Id != null && _entryEnvironments.TryGetValue(entry.Id, out var env))
        {
            return env;
        }
        return Environment;
    }

    /// <summary>
    /// Returns all stored entries, with pinned entries first (deduplicated by ID).
    /// </summary>
    public List<DebugEntry> GetAll()
    {
        // Merge: pinned entries + unpinned entries (exclude IDs already in pinned).
        var pinnedIds = new HashSet<string>(_pinned.Keys, StringComparer.Ordinal);
        var unpinned = _queue.Where(e => e.Id == null || !pinnedIds.Contains(e.Id)).ToList();

        var result = new List<DebugEntry>(_pinned.Values);
        result.AddRange(unpinned);
        return result;
    }

    public DebugEntry? Get(string id)
    {
        // Check pinned first (O(1)), then queue.
        if (_pinned.TryGetValue(id, out var pinned))
        {
            return pinned;
        }

        return _queue.FirstOrDefault(x => x.Id == id);
    }

    /// <summary>
    /// Clears all entries, including pinned ones.
    /// This is a full session reset; leaving pinned entries after a clear would
    /// create a confusing split-brain state where the dashboard shows ghost entries.
    /// </summary>
    public void Clear()
    {
        while (_queue.TryDequeue(out _)) { }
        _pinned.Clear();
        _entryEnvironments.Clear();
        ExceptionGroups.Clear();
    }

    /// <summary>
    /// Returns the count of currently pinned entries.
    /// </summary>
    public int PinnedCount => _pinned.Count;

    private static bool TryParseException(string? body, out string type, out string message)
    {
        type = string.Empty;
        message = string.Empty;

        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        var firstLine = body.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(firstLine))
        {
            return false;
        }

        var colonIndex = firstLine.IndexOf(':');
        if (colonIndex <= 0)
        {
            var trimmed = firstLine.Trim();
            if (!trimmed.Contains(' ') && trimmed.EndsWith("Exception"))
            {
                type = trimmed;
                message = string.Empty;
                return true;
            }
            return false;
        }

        var potentialType = firstLine[..colonIndex].Trim();
        if (potentialType.Contains(' ') || !potentialType.EndsWith("Exception"))
        {
            return false;
        }

        type = potentialType;
        message = firstLine[(colonIndex + 1)..].Trim();
        return true;
    }

    private static string NormalizeMessage(string message)
    {
        if (string.IsNullOrEmpty(message))
            return string.Empty;

        var normalized = GuidRegex.Replace(message, "*");
        normalized = NumberRegex.Replace(normalized, "*");
        return normalized;
    }

    private static string ComputeHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }

    private static string GetDateFormat()
    {
        var shortDatePattern = CultureInfo.CurrentCulture.DateTimeFormat.ShortDatePattern;
        var index = shortDatePattern.LastIndexOf('y');
        var dataFormat = index >= 0 ? shortDatePattern[..(index + 1)] : shortDatePattern;

        return dataFormat;
    }
}
