using DotNetReverseProxy.Smtp;
using DotNetReverseProxy.Spf;
using System;
using System.Collections.Generic;
using System.Text;

namespace DotNetReverseProxy.Tests.Spf;

public class SpfUnitTests
{


    [Fact]
    public void ParseMechanisms()
    {
        var mx = SpfMechanism.Parse("mx");
        Assert.Equal("mx", mx.Type);

        mx = SpfMechanism.Parse("mx/24");
        Assert.Equal("mx", mx.Type);

        Assert.Equal("24", mx.Suffix);



        mx = SpfMechanism.Parse("mx:a.com/24");
        Assert.Equal("mx", mx.Type);
        Assert.Equal("a.com", mx.Value);
        Assert.Equal("24", mx.Suffix);

    }


}
