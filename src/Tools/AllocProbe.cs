using System;
using System.Collections.Concurrent;
using System.Diagnostics.Tracing;
using System.Linq;
using Godot;

namespace DestructionRacing.Tools;

/// <summary>
/// Dev probe (<c>--allocprobe</c>): listens to the .NET runtime's allocation ticks (one about every 100 kB) and
/// prints which types are allocated, split into small-object and large-object heap, to find what drives the GC.
/// </summary>
public sealed class AllocProbe : EventListener
{
    private readonly ConcurrentDictionary<string, long> _small = new(), _large = new();
    public static AllocProbe? Instance { get; private set; }

    public static void Start() => Instance ??= new AllocProbe();

    protected override void OnEventSourceCreated(EventSource source)
    {
        if (source.Name == "Microsoft-Windows-DotNETRuntime") EnableEvents(source, EventLevel.Verbose, (EventKeywords)0x1);
    }

    protected override void OnEventWritten(EventWrittenEventArgs e)
    {
        if (e.EventName == null || !e.EventName.StartsWith("GCAllocationTick") || e.Payload == null) return;
        int kindIndex = e.PayloadNames!.IndexOf("AllocationKind"), typeIndex = e.PayloadNames.IndexOf("TypeName"), amountIndex = e.PayloadNames.IndexOf("AllocationAmount64");
        if (kindIndex < 0 || typeIndex < 0) return;
        uint kind = Convert.ToUInt32(e.Payload[kindIndex]);
        string type = e.Payload[typeIndex]?.ToString() ?? "?";
        long amount = amountIndex >= 0 ? Convert.ToInt64(e.Payload[amountIndex]) : 100_000;
        (kind == 1 ? _large : _small).AddOrUpdate(type, amount, (_, v) => v + amount);
    }

    public void Report()
    {
        GD.Print("[Alloc] large objects: " + string.Join(", ", _large.OrderByDescending(kv => kv.Value).Take(8).Select(kv => $"{kv.Key} {kv.Value / 1e6:0.0} MB")));
        GD.Print("[Alloc] small objects: " + string.Join(", ", _small.Where(kv => kv.Key is not ("MoreEventInfo" or "System.Diagnostics.Tracing.EventWrittenEventArgs" or "System.Collections.ObjectModel.ReadOnlyCollection`1[System.Object]" or "System.IntPtr" or "System.UInt16" or "System.Object[]" or "<>c__DisplayClass8_0")).OrderByDescending(kv => kv.Value).Take(25).Select(kv => $"{kv.Key} {kv.Value / 1e6:0.0} MB")));
        GD.Print($"[Alloc] GC {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}, paused {GC.GetTotalPauseDuration().TotalMilliseconds:0} ms, allocated {GC.GetTotalAllocatedBytes() / 1e6:0} MB");
    }
}
