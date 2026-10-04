using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace DotNetReverseProxy.Tests.IPTests;

public class IPAddressTests
{

    [Fact]
    public void NetworkCheck()
    {
        var net = IPNetwork.Parse("172.0.0.1/24");

        var ip = IPAddress.Parse("::ffff:172.0.0.5");

        Assert.True(net.Contains(ip));
    }


}
