using System.IO;

namespace DotNetAcmeClient;

public static class FileEx
{
    
    public static void EnsureDirectory(string dir)
    {
        EnsureDirectory(new DirectoryInfo(dir));
    }

    static void EnsureDirectory(DirectoryInfo dir)
    {
        if(dir.Exists)
        {
            return;
        }
        EnsureDirectory(dir.Parent);
        dir.Create();
    }
}