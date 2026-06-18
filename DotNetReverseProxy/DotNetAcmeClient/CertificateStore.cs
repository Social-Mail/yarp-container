using System;
using System.Threading.Tasks;

namespace DotNetReverseProxy;

public struct PortInfo
{
    public string UnixPort {get;set;}

    public int Port {get; set;}

    public string Host {get;set;}

}

public class CertificateInfo
{
    public string Cert {get;set;}

    public string Key {get;set;}
}

public class CertificateStore
{

    public async Task<PortInfo> GetPort(string hostName)
    {
        return default;
    }

    internal async Task<CertificateInfo> GetCertificate(string serverName)
    {
        throw new NotImplementedException();
    }
}