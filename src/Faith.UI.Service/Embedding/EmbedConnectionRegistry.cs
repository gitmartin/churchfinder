using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;

namespace Faith.UI.Service.Embedding;

public sealed record EmbedConnection(string ConnectionId, string StreamToken, string ResizeToken);

public sealed class EmbedConnectionRegistry : BackgroundService
{
  private const int MaximumConnections = 1000;
  private static readonly TimeSpan IdleLifetime = TimeSpan.FromMinutes(30);
  private readonly object gate = new();
  private readonly Dictionary<string, Lease> leases = new(StringComparer.Ordinal);

  public EmbedConnection? Register(string parentOrigin, string component)
  {
    lock (gate)
    {
      RemoveExpired();
      if (leases.Count >= MaximumConnections) return null;
      var connection = new EmbedConnection(RandomToken(16), RandomToken(32), RandomToken(32));
      leases.Add(connection.ConnectionId, new(connection, parentOrigin, component));
      return connection;
    }
  }

  public bool TryGetForFrame(string connectionId, string resizeToken, string parentOrigin, string component)
  {
    lock (gate)
    {
      if (!TryGetLive(connectionId, out var lease)
        || !TokenMatches(lease.Connection.ResizeToken, resizeToken)
        || lease.ParentOrigin != parentOrigin || lease.Component != component) return false;
      lease.LastSeen = DateTimeOffset.UtcNow;
      return true;
    }
  }

  internal ChannelReader<double>? OpenStream(string connectionId, string streamToken, string parentOrigin)
  {
    lock (gate)
    {
      if (!TryGetLive(connectionId, out var lease)
        || !TokenMatches(lease.Connection.StreamToken, streamToken)
        || lease.ParentOrigin != parentOrigin) return null;
      lease.Stream?.Writer.TryComplete();
      lease.Stream = Channel.CreateBounded<double>(new BoundedChannelOptions(1)
      {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = false
      });
      if (lease.LastHeight is double height) lease.Stream.Writer.TryWrite(height);
      lease.LastSeen = DateTimeOffset.UtcNow;
      return lease.Stream.Reader;
    }
  }

  internal bool TouchStream(string connectionId, ChannelReader<double> reader)
  {
    lock (gate)
    {
      if (!TryGetLive(connectionId, out var lease) || lease.Stream?.Reader != reader) return false;
      lease.LastSeen = DateTimeOffset.UtcNow;
      return true;
    }
  }

  internal void CloseStream(string connectionId, ChannelReader<double> reader)
  {
    lock (gate)
    {
      if (!leases.TryGetValue(connectionId, out var lease) || lease.Stream?.Reader != reader) return;
      lease.Stream.Writer.TryComplete();
      lease.Stream = null;
      lease.LastSeen = DateTimeOffset.UtcNow;
    }
  }

  internal bool Publish(string connectionId, string resizeToken, double height)
  {
    lock (gate)
    {
      if (!TryGetLive(connectionId, out var lease)
        || !TokenMatches(lease.Connection.ResizeToken, resizeToken)) return false;
      lease.LastSeen = DateTimeOffset.UtcNow;
      lease.LastHeight = height;
      lease.Stream?.Writer.TryWrite(height);
      return true;
    }
  }

  internal bool Delete(string connectionId, string streamToken, string parentOrigin)
  {
    lock (gate)
    {
      if (!TryGetLive(connectionId, out var lease)
        || !TokenMatches(lease.Connection.StreamToken, streamToken)
        || lease.ParentOrigin != parentOrigin) return false;
      leases.Remove(connectionId);
      lease.Stream?.Writer.TryComplete();
      return true;
    }
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
    try
    {
      while (await timer.WaitForNextTickAsync(stoppingToken))
        lock (gate) RemoveExpired();
    }
    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    finally
    {
      lock (gate)
      {
        foreach (var lease in leases.Values) lease.Stream?.Writer.TryComplete();
        leases.Clear();
      }
    }
  }

  private bool TryGetLive(string connectionId, out Lease lease)
  {
    if (leases.TryGetValue(connectionId, out lease!) && lease.LastSeen > DateTimeOffset.UtcNow - IdleLifetime)
      return true;
    if (lease is not null)
    {
      leases.Remove(connectionId);
      lease.Stream?.Writer.TryComplete();
    }
    return false;
  }

  private void RemoveExpired()
  {
    var cutoff = DateTimeOffset.UtcNow - IdleLifetime;
    foreach (var entry in leases.Where(entry => entry.Value.LastSeen <= cutoff).ToArray())
    {
      leases.Remove(entry.Key);
      entry.Value.Stream?.Writer.TryComplete();
    }
  }

  private static string RandomToken(int bytes) => Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes)).ToLowerInvariant();
  private static bool TokenMatches(string expected, string actual) => actual.Length == expected.Length
    && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual));

  private sealed class Lease(EmbedConnection connection, string parentOrigin, string component)
  {
    public EmbedConnection Connection { get; } = connection;
    public string ParentOrigin { get; } = parentOrigin;
    public string Component { get; } = component;
    public DateTimeOffset LastSeen { get; set; } = DateTimeOffset.UtcNow;
    public double? LastHeight { get; set; }
    public Channel<double>? Stream { get; set; }
  }
}
