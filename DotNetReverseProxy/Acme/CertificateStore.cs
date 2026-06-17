using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace DotNetReverseProxy;

public class CertificateStore
{

    string storePath = "/cache/certs/";

    public CertificateStore()
    {
    }

    public async Task<ForwardInfo> GetCertificate(string hostName)
    {

        

        throw new Exception("Work in progress");

    }

}
