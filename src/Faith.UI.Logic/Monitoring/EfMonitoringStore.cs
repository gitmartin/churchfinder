using System.Text.Json;
using Branches.Core.ActivityMonitoring;
using Branches.Core.ErrorManagement;
using Microsoft.EntityFrameworkCore;
using Faith.UI.Logic.Data;
using Faith.UI.Logic.Models;
using BspnRecord = Branches.Core.BSPN.Models.ActivityRecord;

namespace Faith.UI.Logic.Monitoring;

public sealed class EfMonitoringStore(IDbContextFactory<AppDbContext> factory) : IErrorRecordWriter, IErrorRecordStore,
  IErrorRecordDeleteStore, IActivityRecordStore, Branches.Core.BSPN.Storage.IActivityStore
{
  private readonly SemaphoreSlim writes = new(1, 1);
  public string Name => "Application database";
  public bool IsDurable => true;

  private async Task SaveAsync<T>(string id, string kind, string application, DateTimeOffset at, DateTimeOffset started, T record, CancellationToken ct)
  {
    await writes.WaitAsync(ct);
    try
    {
      await using var db = await factory.CreateDbContextAsync(ct);
      var key = kind + ":" + application + ":" + id;
      var row = await db.Monitoring.FindAsync([key], ct);
      if (row is null) { row = new() { Id = key, Kind = kind, Application = application }; db.Monitoring.Add(row); }
      row.TimestampUtc = at.UtcDateTime;
      row.StartedUtc = started.UtcDateTime;
      row.Json = JsonSerializer.Serialize(record);
      await db.SaveChangesAsync(ct);
    }
    finally { writes.Release(); }
  }

  public Task WriteAsync(ErrorManagementRecord record, CancellationToken cancellationToken) =>
    SaveAsync(record.ReferenceId, "error", record.Application, record.OccurredUtc, record.OccurredUtc, record, cancellationToken);

  public async Task<ErrorRecordReadResult> ReadAsync(DateTimeOffset startUtc, DateTimeOffset endUtc, int maximumRecords, CancellationToken cancellationToken = default)
  {
    await using var db = await factory.CreateDbContextAsync(cancellationToken);
    var rows = await db.Monitoring.AsNoTracking().Where(x => x.Kind == "error" && x.TimestampUtc >= startUtc.UtcDateTime && x.TimestampUtc <= endUtc.UtcDateTime)
      .OrderByDescending(x => x.TimestampUtc).Take(maximumRecords + 1).Select(x => x.Json).ToArrayAsync(cancellationToken);
    return new(true, Name, rows.Length > maximumRecords, rows.Take(maximumRecords).Select(x => JsonSerializer.Deserialize<ErrorManagementRecord>(x)!).ToArray());
  }

  public async Task<int> DeleteAsync(IReadOnlyCollection<ErrorRecordIdentity> records, CancellationToken cancellationToken = default)
  {
    await using var db = await factory.CreateDbContextAsync(cancellationToken);
    var ids = records.Select(x => "error:" + x.Application + ":" + x.ReferenceId).ToArray();
    return await db.Monitoring.Where(x => ids.Contains(x.Id)).ExecuteDeleteAsync(cancellationToken);
  }

  public async Task WriteAsync(IReadOnlyCollection<ActivityRecord> records, CancellationToken cancellationToken)
  {
    foreach (var row in records) await SaveAsync(row.Id.ToString(), "activity", row.Application, row.LastSeenAt, row.StartedAt, row, cancellationToken);
  }

  public async Task<ActivityReadResult> ReadAsync(string application, DateTimeOffset start, DateTimeOffset end, int maximumRecords, CancellationToken cancellationToken)
  {
    await using var db = await factory.CreateDbContextAsync(cancellationToken);
    var rows = await db.Monitoring.AsNoTracking().Where(x => x.Kind == "activity" && x.Application == application && x.TimestampUtc >= start.UtcDateTime && x.StartedUtc <= end.UtcDateTime)
      .OrderByDescending(x => x.TimestampUtc).Take(maximumRecords + 1).Select(x => x.Json).ToArrayAsync(cancellationToken);
    return new(Name, true, rows.Length > maximumRecords, rows.Take(maximumRecords).Select(x => JsonSerializer.Deserialize<ActivityRecord>(x)!).ToArray());
  }

  public ValueTask UpsertAsync(BspnRecord record, CancellationToken cancellationToken = default) =>
    new(SaveAsync(record.EventId.ToString(), "bspn", record.ApplicationName, record.LastSeenAt ?? record.ObservedAt, record.ObservedAt, record, cancellationToken));

  public async ValueTask<IReadOnlyList<BspnRecord>> ReadRecentAsync(string applicationName, int maximumRecords, CancellationToken cancellationToken = default)
  {
    await using var db = await factory.CreateDbContextAsync(cancellationToken);
    var rows = await db.Monitoring.AsNoTracking().Where(x => x.Kind == "bspn" && x.Application == applicationName)
      .OrderByDescending(x => x.TimestampUtc).Take(maximumRecords).Select(x => x.Json).ToArrayAsync(cancellationToken);
    return rows.Select(x => JsonSerializer.Deserialize<BspnRecord>(x)!).ToArray();
  }
}
