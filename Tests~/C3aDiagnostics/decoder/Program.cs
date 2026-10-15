using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;

if (args.Length != 2) throw new ArgumentException("PauseDecoder <input.nettrace> <NEW output directory>");
string input = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]);
if (Directory.Exists(output)) throw new IOException("Refusing to overwrite output");
Directory.CreateDirectory(output);
using var raw = new StreamWriter(Path.Combine(output, "suspension-events.jsonl"));
using var intervals = new StreamWriter(Path.Combine(output, "suspension-intervals.jsonl"));
var inventory = new SortedDictionary<string, long>();
var pending = new Dictionary<(int, int), Suspension>();
var gaps = new List<object>();
long pairs = 0, sampleProviderEvents = 0, unknownEvents = 0;
void Emit(StreamWriter writer, object value) { writer.WriteLine(JsonSerializer.Serialize(value)); writer.Flush(); }
void Event(TraceEvent e)
{
    var payload = new Dictionary<string, object?>();
    foreach (string name in e.PayloadNames) payload[name] = e.PayloadByName(name)?.ToString();
    Emit(raw, new { provider = e.ProviderName, event_name = e.EventName, event_id = (int)e.ID, process_id = e.ProcessID, thread_id = e.ThreadID, time_ms = e.TimeStampRelativeMSec, payload });
}
using var source = new EventPipeEventSource(input);
source.Dynamic.All += _ => { };
source.AllEvents += e =>
{
    string key = e.ProviderName + ":" + e.EventName;
    inventory.TryGetValue(key, out long count); inventory[key] = count + 1;
    if (e.ProviderName == "Microsoft-DotNETCore-SampleProfiler") sampleProviderEvents++;
};
source.UnhandledEvents += e => { unknownEvents++; gaps.Add(new { kind = "unhandled_event", provider = e.ProviderName, event_name = e.EventName, event_id = (int)e.ID, time_ms = e.TimeStampRelativeMSec }); };
source.Clr.GCSuspendEEStart += e =>
{
    Event(e); var key = (e.ProcessID, (int)e.ClrInstanceID);
    if (pending.TryGetValue(key, out var prior)) gaps.Add(new { kind = "start_without_previous_restart_end", process_id = key.ProcessID, previous_start_ms = prior.Start, next_start_ms = e.TimeStampRelativeMSec });
    pending[key] = new Suspension { Start = e.TimeStampRelativeMSec, Reason = e.Reason.ToString(), ReasonCode = (int)e.Reason, Count = e.Count, StartEvent = e.EventName };
};
source.Clr.GCSuspendEEStop += e =>
{
    Event(e); var key = (e.ProcessID, (int)e.ClrInstanceID);
    if (pending.TryGetValue(key, out var item))
    {
        if (item.SuspendEnd.HasValue) { gaps.Add(new { kind = "duplicate_suspend_end", process_id = e.ProcessID, previous_ms = item.SuspendEnd, next_ms = e.TimeStampRelativeMSec }); item.Ambiguous = true; }
        item.SuspendEnd = e.TimeStampRelativeMSec;
    }
    else gaps.Add(new { kind = "suspend_end_without_start", process_id = e.ProcessID, time_ms = e.TimeStampRelativeMSec });
};
source.Clr.GCRestartEEStart += e =>
{
    Event(e); var key = (e.ProcessID, (int)e.ClrInstanceID);
    if (pending.TryGetValue(key, out var item))
    {
        if (item.RestartStart.HasValue) { gaps.Add(new { kind = "duplicate_restart_start", process_id = e.ProcessID, previous_ms = item.RestartStart, next_ms = e.TimeStampRelativeMSec }); item.Ambiguous = true; }
        item.RestartStart = e.TimeStampRelativeMSec;
    }
    else gaps.Add(new { kind = "restart_start_without_suspend", process_id = e.ProcessID, time_ms = e.TimeStampRelativeMSec });
};
source.Clr.GCRestartEEStop += e =>
{
    Event(e); var key = (e.ProcessID, (int)e.ClrInstanceID);
    if (!pending.Remove(key, out var item)) { gaps.Add(new { kind = "restart_end_without_suspend", process_id = e.ProcessID, time_ms = e.TimeStampRelativeMSec }); return; }
    bool validOrder = !item.Ambiguous && e.TimeStampRelativeMSec >= item.Start && (!item.SuspendEnd.HasValue || (item.SuspendEnd.Value >= item.Start && item.SuspendEnd.Value <= e.TimeStampRelativeMSec)) && (!item.RestartStart.HasValue || (item.RestartStart.Value >= item.Start && item.RestartStart.Value <= e.TimeStampRelativeMSec)) && (!item.SuspendEnd.HasValue || !item.RestartStart.HasValue || item.RestartStart.Value >= item.SuspendEnd.Value);
    if (!validOrder) gaps.Add(new { kind = "nonmonotonic_pause_events", process_id = e.ProcessID, start_ms = item.Start, end_ms = e.TimeStampRelativeMSec });
    if (!item.SuspendEnd.HasValue || !item.RestartStart.HasValue) gaps.Add(new { kind = "missing_inner_pause_boundary", process_id = e.ProcessID, start_ms = item.Start });
    pairs++;
    Emit(intervals, new { process_id = e.ProcessID, clr_instance_id = e.ClrInstanceID, suspend_start_event = item.StartEvent, restart_end_event = e.EventName, suspend_start_ms = item.Start, suspend_end_ms = item.SuspendEnd, restart_start_ms = item.RestartStart, restart_end_ms = e.TimeStampRelativeMSec, suspend_to_restart_ms = validOrder ? (double?)(e.TimeStampRelativeMSec - item.Start) : null, fully_suspended_ms = validOrder && item.SuspendEnd.HasValue && item.RestartStart.HasValue ? item.RestartStart.Value - item.SuspendEnd.Value : (double?)null, reason = item.Reason, reason_code = item.ReasonCode, gc_related_reason = item.ReasonCode == 1 || item.ReasonCode == 6, collection_count_field = item.Count, source = "observed paired events; no attribution from collection counts" });
};
source.Clr.GCStart += Event;
source.Clr.GCStop += Event;
source.Process();
foreach (var entry in pending) gaps.Add(new { kind = "unfinished_suspension", process_id = entry.Key.Item1, clr_instance_id = entry.Key.Item2, start_ms = entry.Value.Start });
if (source.EventsLost != 0) gaps.Add(new { kind = "events_lost", count = source.EventsLost });
if (pairs == 0) gaps.Add(new { kind = "no_complete_suspension_pairs", meaning = "No pause-duration conclusion; events may be absent, unavailable, lost, or there may be no suspension" });
if (sampleProviderEvents == 0) gaps.Add(new { kind = "no_sample_profiler_events", meaning = "No hotspot attribution possible" });
var json = new JsonSerializerOptions { WriteIndented = true };
File.WriteAllText(Path.Combine(output, "event-inventory.json"), JsonSerializer.Serialize(inventory, json));
File.WriteAllText(Path.Combine(output, "gaps.json"), JsonSerializer.Serialize(gaps, json));
File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(new { input, input_sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input))).ToLowerInvariant(), complete_suspension_pairs = pairs, sample_provider_events = sampleProviderEvents, unhandled_events = unknownEvents, events_lost = source.EventsLost, gap_count = gaps.Count, scope = "Entire traced process, including setup, 64 warmups, 4096 requested lifecycle iterations, and postvalidation. No phase markers exist; do not claim loop-only pauses.", interpretation = "Suspend-start through restart-end includes suspension coordination. Fully-suspended duration is reported only when inner events exist. SampleProfiler observes managed thread stacks, not exact on-CPU time. GC counts and stack sample percentages do not establish performance causation." }, json));
Console.WriteLine("Decoded observed event intervals; gaps preserved. No benchmark targets executed.");

sealed class Suspension
{
    public double Start; public double? SuspendEnd; public double? RestartStart; public bool Ambiguous;
    public string Reason = "", StartEvent = ""; public int ReasonCode, Count;
}
