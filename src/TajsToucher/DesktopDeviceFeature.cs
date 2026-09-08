using TajsToucher.Devices;

namespace TajsToucher;

internal sealed class DesktopDeviceFeature : IAsyncDisposable
{
    private readonly DeviceEventBroker broker;
    public IDeviceService Service { get; }

    public DesktopDeviceFeature(IDeviceService service)
    {
        Service = service;
        broker = new DeviceEventBroker();
        service.Signal += broker.Publish;
    }

    public async ValueTask DisposeAsync()
    {
        try { await Service.DisposeAsync(); }
        finally
        {
            Service.Signal -= broker.Publish;
            await broker.DisposeAsync();
        }
    }
}
