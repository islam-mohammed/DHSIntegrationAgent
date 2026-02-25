using System.ComponentModel;
using System.Runtime.InteropServices;

namespace DHSIntegrationAgent.Infrastructure.Services;

public sealed class NetworkShareAccessor : IDisposable
{
    private readonly string _remoteName;
    private bool _connected;

    private NetworkShareAccessor(string remoteName)
    {
        _remoteName = remoteName;
        _connected = true;
    }

    public static IDisposable Connect(string remoteName, string username, string password)
    {
        if (!OperatingSystem.IsWindows())
            return new NetworkShareAccessor(remoteName);

        var netResource = new NetResource
        {
            Scope = ResourceScope.GlobalNetwork,
            ResourceType = ResourceType.Disk,
            DisplayType = ResourceDisplayType.Share,
            RemoteName = remoteName
        };

        var result = WNetAddConnection2(netResource, password, username, 0);

        if (result != 0)
        {
            throw new Win32Exception(result, $"Failed to connect to network share '{remoteName}'. Error: {result}");
        }

        return new NetworkShareAccessor(remoteName);
    }

    public void Dispose()
    {
        if (_connected)
        {
            if (OperatingSystem.IsWindows())
            {
                WNetCancelConnection2(_remoteName, 0, true);
            }
            _connected = false;
        }
    }

    [DllImport("mpr.dll")]
    private static extern int WNetAddConnection2([In] NetResource netResource, string password, string username, int flags);

    [DllImport("mpr.dll")]
    private static extern int WNetCancelConnection2(string name, int flags, bool force);

    [StructLayout(LayoutKind.Sequential)]
    private class NetResource
    {
        public ResourceScope Scope;
        public ResourceType ResourceType;
        public ResourceDisplayType DisplayType;
        public int Usage;
        public string LocalName = "";
        public string RemoteName = "";
        public string Comment = "";
        public string Provider = "";
    }

    private enum ResourceScope : int
    {
        Connected = 1,
        GlobalNetwork,
        Remembered,
        Recent,
        Context
    }

    private enum ResourceType : int
    {
        Any = 0,
        Disk = 1,
        Print = 2,
        Reserved = 8,
    }

    private enum ResourceDisplayType : int
    {
        Generic = 0x0,
        Domain = 0x01,
        Server = 0x02,
        Share = 0x03,
        File = 0x04,
        Group = 0x05,
        Network = 0x06,
        Root = 0x07,
        Shareadmin = 0x08,
        Directory = 0x09,
        Tree = 0x0a,
        Ndscontainer = 0x0b
    }
}
