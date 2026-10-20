using System.Net.NetworkInformation;
using DietTracker.Core.Interfaces;

namespace DietTracker.Infrastructure.Services;

/// <summary>
/// Reports whether the machine currently has network access. The check is cheap (interface flags)
/// and is only used to decide whether a synchronization attempt is worth starting; the engine still
/// handles provider errors gracefully.
/// </summary>
public sealed class ConnectivityMonitor : IConnectivityMonitor, IDisposable
{
    private bool _isOnline;
    private bool _disposed;

    public ConnectivityMonitor()
    {
        _isOnline = Evaluate();
        NetworkChange.NetworkAvailabilityChanged += OnAvailabilityChanged;
        NetworkChange.NetworkAddressChanged += OnAddressChanged;
    }

    public bool IsOnline => _isOnline;

    public event EventHandler<bool>? ConnectivityChanged;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        NetworkChange.NetworkAvailabilityChanged -= OnAvailabilityChanged;
        NetworkChange.NetworkAddressChanged -= OnAddressChanged;
        _disposed = true;
    }

    private void OnAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e) => Update(e.IsAvailable);

    private void OnAddressChanged(object? sender, EventArgs e) => Update(Evaluate());

    private void Update(bool isOnline)
    {
        if (_isOnline == isOnline)
        {
            return;
        }

        _isOnline = isOnline;
        ConnectivityChanged?.Invoke(this, isOnline);
    }

    private static bool Evaluate()
    {
        try
        {
            return NetworkInterface.GetIsNetworkAvailable();
        }
        catch (NetworkInformationException)
        {
            return false;
        }
    }
}
