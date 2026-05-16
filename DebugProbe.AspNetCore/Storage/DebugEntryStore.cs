using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using DebugProbe.AspNetCore.Internal.Utils;
using DebugProbe.AspNetCore.Models;
using DebugProbe.AspNetCore.Options;

namespace DebugProbe.AspNetCore.Storage;

/// <summary>
/// In-memory store for DebugEntry instances with a configurable size limit.
/// </summary>
public class DebugEntryStore
{
    public DebugEnvironment Environment { get; }

    private readonly ConcurrentQueue<DebugEntry> _queue = new();
    private readonly int _limit;

    public DebugEntryStore(DebugProbeOptions options)
    {
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
        while (_queue.Count > _limit)
            _queue.TryDequeue(out _);
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
    }

    private string GetDateFormat()
    {
        var shortDatePattern = CultureInfo.CurrentCulture.DateTimeFormat.ShortDatePattern;
        var index = shortDatePattern.LastIndexOf('y');
        var dataFormat = index >= 0 ? shortDatePattern[..(index + 1)] : shortDatePattern;

        return dataFormat;
    }
}