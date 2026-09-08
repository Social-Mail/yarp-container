using System.Collections.Generic;
using System.Net;

namespace DotNetReverseProxy;

public class IPAddressRange
{
    private List<IPNetwork> networks = new List<IPNetwork>();

    public IPAddressRange(IEnumerable<string> enumerable)
    {
        foreach (var a in enumerable)
        {
            this.Add(a);
        }
    }

    public bool Contains(IPAddress address)
    {
        foreach (var network in networks)
        {
            if (network.Contains(address))
            {
                return true;
            }
        }
        return false;
    }

    void Add(string ipAddress)
    {
        this.networks.Add(Parse(ipAddress));
    } 

    private static IPNetwork Parse(string ipAddress)
    {
        if(ipAddress.Contains('/'))
        {
            return IPNetwork.Parse(ipAddress);
        }
        if(ipAddress.Contains(':'))
        {
            return IPNetwork.Parse(ipAddress + "/128");
        }
        return IPNetwork.Parse(ipAddress + "/32");
    }

}
