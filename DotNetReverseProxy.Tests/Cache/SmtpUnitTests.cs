using DotNetReverseProxy.RateLimiter;
using DotNetReverseProxy.Smtp;
using System.Net;

namespace DotNetReverseProxy.Tests;

public class ConcurrentCacheTests
{
    [Fact]
    public async Task Read()
    {

        var c = new ConcurrentIPCache(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));

        var key = IPAddress.Parse("1.1.1.1");

        // no error test...

        var n = c.GetOrUpdate(key, (x) => 0, (x, y) => y - 1);

        Assert.Equal(0, n);

        Assert.False(c.ContainsKey(key));

        n = c.GetOrUpdate(key, (x) => 1, (x, y) => y + 1);

        Assert.Equal(1, n);

        await Task.Delay(TimeSpan.FromSeconds(3));

        Assert.False(c.TryGetValue(key, out var n1));


        n = c.GetOrUpdate(key, (x) => 1, (x, y) => y + 2);
        Assert.Equal(1, n);
        n = c.GetOrUpdate(key, (x) => 1, (x, y) => y + 2);
        Assert.Equal(3, n);
        c.TryGetValue(key, out n1);
        Assert.Equal(3, n1);

    }

}