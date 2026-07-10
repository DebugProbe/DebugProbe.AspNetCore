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
public class DebugEntryStore
{
    private static readonly Regex GuidRegex = new(@"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b", RegexOptions.Compiled);
    private static readonly Regex NumberRegex = new(@"\b\d+(\.\d+)?\b", RegexOptions.Compiled);

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

    private readonly ConcurrentQueue<DebugEntry> _queue = new();
    private readonly int _limit;

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
        _queue.Enqueue(entry);

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

        while (_queue.Count > _limit)
        {
            // ExceptionGroups counts are a running tally and must NOT be decremented on MaxEntries eviction.
            _queue.TryDequeue(out _);
        }
    }

    public List<DebugEntry> GetAll()
    {
        return _queue.ToList();
    }

    public DebugEntry? Get(string id)
    {
        return _queue.FirstOrDefault(x => x.Id == id);
    }

    public void Clear()
    {
        while (_queue.TryDequeue(out _)) { }
        ExceptionGroups.Clear();
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
