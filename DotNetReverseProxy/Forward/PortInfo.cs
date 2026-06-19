namespace DotNetReverseProxy;

public class PortInfo
{
    public string? UnixPort {get;set;}

    public int Port {get; set;}

    public string? Host {get;set;}

    public string? QueryHost { get; set; }
}
