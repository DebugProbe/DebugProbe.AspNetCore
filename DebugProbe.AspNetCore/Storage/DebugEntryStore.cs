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
public class DebugEntryStore
{
    private static readonly Regex GuidRegex = new(@"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b", RegexOptions.Compiled);
    private static readonly Regex NumberRegex = new(@"\b\d+(\.\d+)?\b", RegexOptions.Compiled);

    /// <summary>
    /// Gets the static instance of DebugEntryStore.
    /// </summary>
    public static DebugEntryStore? Instance { get; private set; }

    /// <summary>
    /// Gets a snapshot of the exception groups. Later updates and clears do not change this snapshot.
    /// </summary>
    public IReadOnlyDictionary<string, ExceptionGroup> ExceptionGroups
    {
        get
        {
            lock (_sync)
                return new Dictionary<string, ExceptionGroup>(_exceptionGroups);
        }
    }

    /// <summary>
    /// Gets environment information for the current application.
    /// </summary>
    public DebugEnvironment Environment { get; }

    // A single lock keeps requests, environment mappings, and group counts consistent during Add and Clear.
    private readonly object _sync = new();
    private readonly Queue<DebugEntry> _queue = new();
    private readonly Dictionary<string, ExceptionGroup> _exceptionGroups = new();
    private readonly Dictionary<string, DebugEnvironment> _entryEnvironments = new();
    private readonly int _limit;

    public DebugEntryStore(DebugProbeOptions options)
    {
        Instance = this;
        _limit = options.MaxRequests;

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
        lock (_sync)
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

                _exceptionGroups.TryGetValue(fingerprint, out var existing);
                _exceptionGroups[fingerprint] = new ExceptionGroup
                {
                    Fingerprint = fingerprint,
                    Type = type,
                    SampleMessage = existing?.SampleMessage ?? message,
                    Count = (existing?.Count ?? 0) + 1,
                    LastSeen = DateTimeOffset.UtcNow
                };
            }

            // Keep aggregate groups bounded independently of request eviction.
            while (_exceptionGroups.Count > _limit)
            {
                var oldest = _exceptionGroups.Values.MinBy(group => group.LastSeen)!;
                _exceptionGroups.Remove(oldest.Fingerprint);
            }

            while (_queue.Count > _limit)
            {
                // ExceptionGroups counts are a running tally and must NOT be decremented on MaxRequests eviction.
                var evicted = _queue.Dequeue();
                if (evicted.Id != null)
                {
                    _entryEnvironments.Remove(evicted.Id);
                }
            }
        }
    }

    public DebugEnvironment GetEnvironment(DebugEntry entry)
    {
        lock (_sync)
        {
            if (entry.Id != null && _entryEnvironments.TryGetValue(entry.Id, out var env))
            {
                return env;
            }
            return Environment;
        }
    }

    public List<DebugEntry> GetAll()
    {
        lock (_sync)
        {
            return _queue.ToList();
        }
    }

    public DebugEntry? Get(string id)
    {
        lock (_sync)
        {
            return _queue.FirstOrDefault(x => x.Id == id);
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _queue.Clear();
            _entryEnvironments.Clear();
            _exceptionGroups.Clear();
        }
    }

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
